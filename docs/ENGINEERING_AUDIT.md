# VANTA Engineering Audit

> **Historical document.** This audit was written on 2026-09-16 and contains findings from an earlier code state. It is retained for context, but it is not the current source of truth. For the current implementation and validation results, use [VANTA_TECHNICAL_HANDOFF.md](VANTA_TECHNICAL_HANDOFF.md), [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) and [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).

> Several findings below were subsequently addressed, including tenant isolation hardening, rate limiting, API hardening, background jobs, Knowledge/pgvector, commerce events, configuration guards, main-branch test gating and OTLP configuration. Do not use the old test counts or “current architecture” statements below as present-tense facts.

**Fecha:** 2026-09-16  
**Alcance:** auditoría read-only del estado actual del repositorio  
**Estado:** análisis completado; no se modificó código, configuración ni infraestructura. Solo se creó este documento.

## Executive Summary

VANTA es un monolito modular en una etapa de validación avanzada de un vertical slice: registro de merchant, catálogo con variantes, stock, carrito conversacional, órdenes, pagos mock, conversaciones WhatsApp, agente de ventas controlado, dashboard y flujo público de tienda/checkout.

La base tiene buenas decisiones de diseño: separación por módulos, consultas tenant-scoped en la mayoría de endpoints, claves de idempotencia para varias operaciones, snapshots de precio en órdenes, máquina de estados de pedidos, abstracciones para proveedores y pruebas de aislamiento básico.

Sin embargo, **no está lista para manejar datos reales ni exposición pública**. Los bloqueadores principales son:

- **P0:** secretos de terceros y credenciales operativas están committed en configuración; el flujo Meta permite omitir la validación HMAC cuando `AppSecret` está vacío; existen carreras de stock en checkout/order creation; el checkout público no tiene idempotencia ni protección anti-abuso.
- **P1:** el frontend protege el dashboard verificando solo la presencia de una cookie, no su validez; el backend no tiene autorización por roles; el sender Meta usa configuración global aunque existan múltiples cuentas/tenants; webhooks y efectos de IA se procesan sin una frontera fiable de outbox/transacción; el seeder crea una cuenta demo predecible.
- **P2/P3:** el lint falla, faltan pruebas de concurrencia y de seguridad, no hay rate limiting, paginación ni ProblemDetails consistente, la observabilidad es local/console-only y Docker no representa una topología de producción.

**Resultado de validaciones ejecutadas:**

- `npm run build`: **pasa**; Next.js compila y TypeScript finaliza correctamente.
- `npm run lint`: **falla** con 3 errores y 35 warnings.
- `dotnet test tests/unit/Vanta.Api.UnitTests.csproj --no-restore`: **32 pasan, 1 falla**. Falla `PaymentsTests.Public_ecommerce_paid_order_queues_whatsapp_receipt` porque no encuentra el mensaje de recibo esperado.
- El smoke E2E existente cubre login, dashboard, productos, clientes, conversaciones y una respuesta mock del agente, pero no cubre los flujos críticos públicos/concurrentes.

### Prioridad de salida

Antes de cualquier despliegue accesible desde Internet: revocar/rotar secretos, exigir firma de webhooks, corregir concurrencia de inventario, diseñar idempotencia pública, endurecer autenticación/autorización, y reparar el test rojo del flujo de pago/WhatsApp.

## Current Architecture

### Runtime

- **API:** ASP.NET Core/.NET 10, minimal APIs, un proyecto `Vanta.Api` dentro de `apps/api`.
- **Web:** Next.js 16.3.4, React 19, TypeScript, App Router, Tailwind/shadcn y route handlers BFF.
- **Persistencia:** EF Core 10 sobre PostgreSQL/Npgsql; InMemory se usa en tests.
- **Infraestructura local:** PostgreSQL `pgvector/pgvector:pg16` y Redis 7 mediante Docker Compose.
- **Auth:** ASP.NET Core Identity para usuarios y JWT HMAC para llamadas al API; el BFF guarda el JWT en cookie `httpOnly`.
- **Observabilidad:** Serilog y OpenTelemetry con exporters de consola.
- **Proveedores:** `IPaymentProvider`, `IAgentProvider`, `IWhatsAppMessageSender`, `IMetaMediaClient` y providers mock/Meta.

### Módulos observados

- Identity y Tenants
- Catalog y variantes
- Commerce y endpoints públicos
- Orders y carrito
- Customers
- Payments
- Conversations y mensajes
- WhatsApp y delivery worker
- AI Sales/agent tools
- Importación de Instagram

La arquitectura documentada también contempla Inventory, Knowledge/RAG, Campaigns, Analytics y Billing, pero no se observan como módulos completos en el estado actual.

## Strengths

- La decisión de modular monolith es adecuada para el estado pre-PMF y reduce complejidad operacional.
- La mayoría de recursos tenant-owned incluyen `TenantId` y las consultas revisadas filtran por tenant.
- Hay pruebas explícitas de aislamiento básico entre tenants y de detalle de órdenes.
- Productos y variantes usan índices únicos tenant-scoped para SKU.
- Orden y pago usan índices únicos tenant-scoped para idempotency keys.
- Las órdenes almacenan snapshots de nombre, SKU y precio, lo que protege la lectura histórica frente a cambios posteriores del catálogo.
- Existe una máquina de estados para órdenes con transiciones explícitas y `OrderStatusHistory`.
- Los pagos no se marcan como pagados por una redirección del browser; el estado mock se cambia desde backend.
- El agente usa `IAgentTools` con consultas tenant-scoped y no recibe acceso directo al `DbContext` desde el provider.
- La integración Meta tiene parsing separado y usa comparación constant-time para el verify token y, cuando está configurado, HMAC SHA-256.
- Uploads tienen límite de 5 MB y allowlist de JPEG/PNG/WebP.
- El BFF evita exponer el JWT a JavaScript del navegador.
- Hay health check de PostgreSQL, migraciones EF Core y pruebas de integración en proceso.
- El frontend buildable y el smoke E2E proporcionan una base útil para una demo de validación.

## P0 Findings

### P0-1. Secretos y credenciales reales committed en configuración

**Evidencia:** `apps/api/src/Vanta.Api/appsettings.json` y `appsettings.Development.json` contienen un Google/Gemini API key, tokens de WhatsApp Meta y tokens de verificación en texto plano. También hay contraseña PostgreSQL de desarrollo en `appsettings.json` y `infrastructure/docker-compose.yml`.

**Impacto:** cualquier clon, artifact, log de revisión o histórico Git puede permitir consumo de API o acceso al canal WhatsApp. Aunque se etiqueten como desarrollo, los valores tienen formato de credenciales operativas y deben considerarse comprometidos.

**Acción requerida:** revocar y rotar inmediatamente todos los valores expuestos; retirar secretos del historial si corresponde; usar User Secrets en desarrollo y un secret manager en ambientes compartidos; agregar validación de startup que rechace placeholders/secretos committed fuera de Development.

### P0-2. Validación HMAC de Meta es opcional aunque el provider está configurado como Meta

**Evidencia:** `WhatsAppEndpoints.ReceiveMetaWebhookAsync` solo valida `X-Hub-Signature-256` dentro de `if (!string.IsNullOrWhiteSpace(configuration.AppSecret))`. En la configuración actual `WhatsApp:Provider` es `meta`, pero `AppSecret` está vacío.

**Impacto:** un tercero puede enviar payloads al endpoint Meta sin demostrar que provienen de Meta. Esto permite crear conversaciones/mensajes falsos y disparar el agente; combinado con futuras herramientas con side effects, puede causar órdenes, mensajes o acciones fraudulentas.

**Acción requerida:** hacer la firma obligatoria para Meta; fallar startup o devolver 503/401 si falta `AppSecret`; validar algoritmo, formato, tamaño y body raw; añadir replay protection/idempotencia por evento.

### P0-3. Carrera de concurrencia en reserva/decremento de stock

**Evidencia:** `OrderEndpoints.CreateAsync`, `PublicCommerceEndpoints.CreateOrderAsync` y `AgentTools.CheckoutCartAsync` leen `StockQuantity`, verifican que alcanza, decrementan la entidad en memoria y guardan después. No hay lock de fila, update condicional atómico, token de concurrencia ni transacción serializable.

**Impacto:** dos requests concurrentes pueden observar el mismo stock y vender más unidades de las existentes o sobrescribir decrementos. Es un problema de integridad comercial y puede generar órdenes confirmadas imposibles de cumplir.

**Acción requerida:** usar decremento condicional atómico (`UPDATE ... WHERE StockQuantity >= quantity`), o transacción con locking/aislamiento apropiado; agregar pruebas concurrentes contra PostgreSQL real, no solo InMemory.

### P0-4. Checkout público crea órdenes y consume stock sin idempotencia del cliente

**Evidencia:** `POST /api/public/store/{slug}/orders` es anonymous y genera `public-{Guid}` como idempotency key interno. No acepta ni valida una clave del comprador, fingerprint de intento, nonce ni mecanismo equivalente.

**Impacto:** reintentos de red, doble click, refresh o replay crean órdenes y pagos duplicados, además de volver a consumir stock. El índice único interno no ayuda porque cada request fabrica una clave diferente.

**Acción requerida:** aceptar una idempotency key pública con límites y vincularla al tenant/store; guardar request hash y respuesta original; aplicar expiración y rate limiting; procesar la creación de orden, pago y stock en una transacción.

## P1 Findings

### P1-1. Protección del dashboard basada solo en presencia de cookie

**Evidencia:** `apps/web/src/middleware.ts` redirige si no existe `vanta_session`, pero no valida firma, expiración, issuer, audience ni que el token siga siendo válido. La validación real ocurre más tarde en algunos server calls.

**Impacto:** un usuario con una cookie arbitraria puede acceder a la shell del dashboard y generar estados inconsistentes de UX. No constituye por sí sola bypass del API, pero es una frontera de autenticación débil y puede exponer páginas/acciones BFF que no validen correctamente la sesión.

**Acción requerida:** validar token/ sesión en el BFF o hacer que cada route handler requiera una sesión verificada; borrar cookie inválida/expirada y redirigir; considerar refresh/revocación.

### P1-2. No existe autorización por roles o permisos

**Evidencia:** `AddIdentityCore` y `AddRoles` están registrados, pero el JWT solo contiene `sub`, `email` y `tenant_id`; los endpoints usan `RequireAuthorization()` y tenant claim, sin políticas/roles/resource permissions.

**Impacto:** cualquier usuario autenticado del tenant puede ejecutar operaciones administrativas como editar catálogo, cambiar stock, conectar canales, modificar estados, enviar mensajes y usar herramientas del agente. Cuando se agreguen staff/admin, habrá escalación horizontal dentro del tenant.

**Acción requerida:** definir roles/permissions mínimos y políticas endpoint-by-endpoint; centralizar autorización de recursos; probar matriz de permisos.

### P1-3. Sender Meta usa configuración global, no la cuenta/canal de la conversación

**Evidencia:** se persiste `WhatsAppChannelAccount` por tenant y se permite conectar `PhoneNumberId`, pero `MetaWhatsAppSender` usa `WhatsAppProviderOptions.PhoneNumberId` y `AccessToken` global. `WhatsAppDeliveryService` solo obtiene la conversación por tenant y envía mediante el sender singleton.

**Impacto:** con más de una cuenta Meta o más de un tenant configurado, los mensajes pueden salir por el Phone Number ID/token equivocado. Es un fallo de routing, aislamiento operativo y posible exposición entre cuentas.

**Acción requerida:** resolver credenciales/canal por `Conversation.ChannelAccountId`; almacenar secretos cifrados o referencias a secret manager; hacer el sender channel-aware; añadir test multi-tenant/multi-channel.

### P1-4. Webhook inbound ejecuta IA síncronamente y puede perder respuesta tras retry

**Evidencia:** el endpoint guarda el inbound y luego llama `await agent.RespondAsync(...)` dentro de la misma request. Si el agente/provider falla después de guardar el mensaje, Meta puede reintentar; el duplicate check detectará el mensaje y devolverá duplicate sin volver a ejecutar la respuesta.

**Impacto:** el mensaje queda persistido sin outbound response, aunque el proveedor haya reintentado correctamente. Además, una llamada externa lenta puede exceder el timeout del webhook.

**Acción requerida:** persistir evento y encolar trabajo idempotente; responder rápido al webhook; worker retryable con estado de procesamiento, backoff y dead-letter/alerta.

### P1-5. Delivery worker no reclama mensajes de forma atómica

**Evidencia:** `WhatsAppDeliveryService` selecciona hasta 20 mensajes Pending sin marcar/claim antes de enviar. Más de una instancia del API puede seleccionar el mismo lote y enviar duplicados.

**Impacto:** al escalar horizontalmente, aumenta el riesgo de mensajes duplicados. El estado se guarda después del envío y no existe lease, `FOR UPDATE SKIP LOCKED` o distributed lock.

**Acción requerida:** implementar outbox con estados `Pending/Processing/Sent/Failed`, lease/visibility timeout y claim atómico por worker; conservar idempotency key del proveedor cuando exista.

### P1-6. RESUELTO — Cuenta demo y contraseña predecibles en seeder

**Evidencia actual:** se eliminó `DevelopmentDataSeeder` y su invocación de startup. Development aplica migraciones, pero no crea identidades ni datos demo fijos.

**Validación:** la suite E2E registra merchants únicos por test y la base local recién migrada queda sin usuarios, tenants ni datos comerciales.

**Acción aplicada:** retirado el seed automático y eliminada la dependencia E2E de credenciales compartidas.

### P1-7. Checkout público expone datos de pedido mediante referencia no autenticada

**Evidencia:** `GET /api/public/checkout/{paymentReference}` es anonymous y devuelve tienda, número de pedido, items, cantidades, precios, total y estado de pago. La UI lo consume sin autenticación.

**Impacto:** la referencia funciona como bearer token. Si se filtra en logs, analytics, historial, referer o WhatsApp, cualquiera puede consultar información comercial y personal indirecta del pedido. No hay expiración, rate limit ni binding al cliente.

**Acción requerida:** usar token opaco de alta entropía con expiración y scope, minimizar datos expuestos, evitar indexación/cache, añadir rate limiting y, cuando sea viable, verificación adicional del comprador.

### P1-8. Error de integración en flujo pago público/WhatsApp

**Evidencia reproducida:** `dotnet test ...` terminó con 32 tests correctos y 1 error en `PaymentsTests.Public_ecommerce_paid_order_queues_whatsapp_receipt`. El test no encuentra el mensaje outbound que contiene `Recibo VANTA`.

**Impacto:** el contrato esperado de pago confirmado -> recibo WhatsApp no es confiable. Es un fallo de comportamiento en un flujo crítico de revenue.

**Acción requerida:** trazar la asociación entre orden pública, conversación y cliente; garantizar que el webhook/evento encola el recibo para la conversación correcta; añadir una prueba de integración contra PostgreSQL y prueba de retry.

## P2 Findings

### P2-1. Pruebas usan EF InMemory para casi todos los límites de persistencia

El `VantaWebApplicationFactory` sustituye PostgreSQL por InMemory. Esto no reproduce concurrencia, constraints reales, aislamiento transaccional, comportamiento de `ExecuteUpdate`, tipos/índices PostgreSQL ni errores de serialización de provider. Existe riesgo de falsos positivos en idempotencia y stock.

### P2-2. No hay transacciones explícitas en operaciones multi-entidad críticas

Registro, creación de orden, checkout de carrito, orden pública, pago confirmado y webhook actualizan varias tablas/entidades y/o generan mensajes. `SaveChangesAsync` es atómico por contexto en condiciones normales, pero las llamadas externas y múltiples unidades de trabajo no están coordinadas; tampoco hay outbox transaccional ni reconciliación.

### P2-3. Webhook payment mock acepta `TenantId` suministrado por caller

El endpoint anónimo de mock recibe `TenantId` en el body y usa ese valor para localizar pago/orden. La firma es el literal global `mock-dev`, por lo que cualquiera que conozca el contrato puede intentar confirmar pagos de cualquier tenant en el entorno donde el mock esté disponible. Debe estar estrictamente limitado a Development/Test o eliminarse del deployment.

### P2-4. Meta signature se calcula, pero no hay replay protection de evento

La deduplicación de mensajes usa `ExternalMessageId`, pero no existe un registro de evento Meta con timestamp/event ID y ventana de replay. Delivery statuses tampoco están asociados explícitamente a account/tenant en la consulta de actualización.

### P2-5. Consultas sin paginación ni límites suficientes

Listados de productos, órdenes, clientes, conversaciones y mensajes cargan colecciones completas. En `MessagesAsync` se convierten imágenes a Base64 y se devuelven todos los mensajes. Esto puede producir respuestas grandes, alto uso de memoria y latencia creciente.

### P2-6. Imágenes se almacenan como bytea/Base64 en PostgreSQL y respuestas API

El modelo devuelve imágenes completas como Base64 en productos, mensajes, store público y herramientas de IA. Esto aumenta tamaño de DB, payloads, GC y costo de red. No hay object storage, thumbnails, URLs firmadas, compresión/resize consistente ni paginación de media.

### P2-7. Upload validation confía principalmente en Content-Type

Los endpoints de imágenes allowlist MIME y tamaño, pero no validan de forma consistente magic bytes/decode antes de persistir. El importador descarga URLs externas y usa metadata de respuesta. Esto deja espacio a contenido inválido, polyglots o abuso de consumo.

### P2-8. Importación Instagram construye token en URL

`InstagramImportEndpoints` envía `access_token` como query parameter al Graph API. Esto puede aparecer en logs de proxy, traces, historial de URLs o herramientas de diagnóstico. Debe enviarse en header/autenticación del cliente y aislarse de logs.

### P2-9. Validación de requests y errores no es consistente

Hay DataAnnotations útiles, pero faltan límites explícitos para algunos strings/endpoints, manejo uniforme de `ProblemDetails` RFC 7807, nombres de campos y status codes. El API no versiona rutas y algunos endpoints devuelven objetos anónimos distintos para errores similares.

### P2-10. Registro de tenant y usuario carece de transacción de negocio

`RegisterAsync` agrega tenant, crea usuario mediante Identity y después guarda el contexto. Si una de las fases falla, pueden quedar estados parciales u órfanos según el provider. Debe existir transacción o flujo compensatorio con constraint/FK apropiado.

### P2-11. Phone/customer identity no está normalizada de forma fuerte

La unicidad de clientes usa el string de teléfono tenant-scoped, pero los endpoints aceptan distintas representaciones (`+595...`, espacios, guiones). Esto permite duplicados lógicos y asociaciones incorrectas entre WhatsApp y checkout público.

### P2-12. Cart y checkout de browser confían en estado local desactualizado

La tienda pública guarda productos, variantes y cantidades en `localStorage`; el checkout manda IDs/cantidades al servidor, que sí recalcula precio, pero no hay refresh/validación UX de cambios de stock antes de enviar ni idempotencia. También se parsea JSON de localStorage sin manejo de datos corruptos.

### P2-13. AI sales no tiene auditoría de tool calls ni límites operativos suficientes

Hay tools controladas, pero no se persiste un audit trail estructurado de prompt/contexto/tool/action, actor, resultado y decisión. No se observan límites de costo/tokens, timeout específico del provider, circuit breaker, fallback por tenant, redacción de PII o política de retención.

### P2-14. Prompt injection desde catálogo y contenido de usuario

Los nombres/SKU y mensajes entran en prompts como texto. Un merchant o cliente puede insertar instrucciones que alteren el comportamiento del provider. La superficie es menor porque el provider está abstraído y el mock domina, pero debe tratarse como dato no confiable y pasarse estructurado/escapado.

### P2-15. Sales agent tiene lógica heurística acoplada a texto y puede ejecutar acciones ambiguas

La confirmación depende de patrones como `sí`, `dale` o `ok` y del último outbound/inbound. En conversaciones concurrentes o mensajes fuera de orden podría agregar/confirmar el producto equivocado. Los side effects deberían requerir intención estructurada, estado de conversación y confirmación asociada a un cart/version.

### P2-16. Frontend lint falla

`npm run lint` produce 3 errores en `dashboard/settings/page.tsx`, `store/[slug]/cart/page.tsx` y `components/dashboard/dashboard-greeting.tsx` por `react-hooks/set-state-in-effect`, además de 35 warnings de imports/variables sin uso y `<img>` sin optimización. El build no sustituye lint en CI.

## P3 Findings

### P3-1. Dashboard contiene métricas decorativas o no plenamente respaldadas por datos

`dashboard/page.tsx` muestra valores como `$128,450`, `+12.8%`, `+15.6%` y barras/gráficos estáticos mientras la API solo devuelve counts y `aiRevenueAttributed=false`. Aunque sea una demo visual, contradice la regla del proyecto de no mostrar revenue/analytics fabricados y puede inducir a error.

### P3-2. Middleware Next 16 usa convención deprecada

El build reporta que la convención `middleware` está deprecated en favor de `proxy`. No rompe hoy, pero debe planificarse migración para evitar futuras incompatibilidades.

### P3-3. OpenTelemetry exporta solo a consola

Hay instrumentation HTTP/ASP.NET y metrics, pero no exporter/collector persistente, alertas, dashboards ni correlación operacional configurable. Es útil para desarrollo, insuficiente para incident response.

### P3-4. No hay rate limiting ni protección anti-abuso visible

No se observa `AddRateLimiter`/políticas para login, registro, búsqueda AI, uploads, webhooks o checkout público. Esto habilita brute force, scraping, spam y consumo costoso de providers.

### P3-5. Headers y protección web no están formalizados

No se observa configuración explícita de HSTS/secure headers, CSP, anti-clickjacking o política de cache para respuestas públicas sensibles. `UseHttpsRedirection` existe, pero el perfil HTTP sigue presente para desarrollo y no hay baseline documentado de reverse proxy.

### P3-6. Docker Compose es solo desarrollo

Postgres y Redis exponen puertos al host, usan credenciales estáticas, no tienen TLS, backup, límites de recursos, redes privadas, rotación ni configuración de producción. Redis no se usa visiblemente en el runtime revisado.

### P3-7. API/documentación de despliegue está desactualizada

`README.md` aún describe la base como “no business features implemented”, mientras el código ya contiene catálogo, órdenes, pagos, WhatsApp y checkout. Esta divergencia dificulta onboarding y puede ocultar requisitos operativos.

### P3-8. Falta CI/CD y controles automatizados visibles

No se observa pipeline que ejecute build, lint, tests, secret scanning, dependency audit, migration check, E2E y policy checks antes de merge/deploy.

## Security Findings

### Confirmados

- Secretos en archivos tracked/configuración local.
- HMAC Meta desactivable por configuración vacía.
- Endpoints mock anónimos con firmas constantes (`mock-dev`), peligrosos si se publican.
- Checkout público bearer-only, sin expiración/rate limit.
- Ausencia de rate limiting visible.
- JWT con signing key placeholder si se despliega sin override.
- Autorización basada en tenant claim manual y sin RBAC.

### Controles positivos

- JWT valida issuer, audience, signing key y lifetime.
- Cookies BFF son `httpOnly` y `sameSite=lax`.
- Consultas revisadas filtran tenant en la mayoría de recursos.
- Meta verify token usa comparación constant-time.
- Uploads tienen tamaño y MIME allowlist.

### Riesgos pendientes de evidencia

- No se revisó un escaneo histórico de Git ni secret scanning formal.
- No se verificó cifrado en reposo, backup/restore ni rotación en un ambiente de despliegue.
- No se observó WAF/reverse proxy/CSP de producción.
- No se realizó penetration test ni prueba automatizada de IDOR para cada recurso.

## Data Integrity Findings

- Stock decrementado con read-check-write no atómico.
- Checkout público sin idempotencia del consumidor.
- Payment/provider call ocurre antes de persistir en algunos flujos; un fallo posterior puede dejar intentos huérfanos o no reconciliados.
- Webhook duplicate check seguido de insert no está protegido contra carreras por una transacción/constraint suficientemente aplicada en todos los paths.
- Orden pública genera `Number` con timestamp + `Random.Shared` sin constraint única visible; colisiones son improbables pero no imposibles.
- Cart no reserva stock, decisión válida para MVP, pero requiere política explícita de expiración/revalidación al checkout.
- El test de recibo público muestra una inconsistencia real entre pago confirmado, conversación y notificación.
- No hay ledger/audit trail de inventario: se registra el valor final, no el motivo, actor, reserva, liberación o ajuste.

## AI Findings

### Estado actual

- Existe `IAgentProvider` y provider smart/mock.
- Existe `IAgentTools` con búsqueda, stock, carrito, checkout, cliente, alertas y mensajes.
- El agente filtra productos por tenant y usa stock de variantes.
- La integración de LLM y RAG completo no está demostrada como productiva.

### Riesgos

- Prompt injection desde datos de catálogo/mensajes.
- Ambigüedad en confirmaciones y selección de variante.
- Side effects sin audit event formal.
- Respuesta IA síncrona dentro del webhook.
- Sin presupuesto/timeout/circuit breaker/telemetría de costo visible.
- No hay handoff/human pause persistido como control operativo completo.
- No hay prueba exhaustiva de que nunca inventa precio, stock, pago o shipping en providers reales.

## Payment Findings

### Fortalezas

- Provider interface y mock provider.
- Payment status separado de order status.
- Idempotency key en creación autenticada.
- Webhook event table e historial de orden.
- Browser redirect no marca pago automáticamente.

### Gaps/bloqueadores

- No hay provider real, firma real por provider ni reconciliación.
- Mock webhook usa secreto constante y recibe tenant desde el payload.
- Checkout público carece de idempotencia y protección contra replay.
- Confirmación y webhook pueden competir sin locking/unique event processing robusto.
- El flujo público de recibo WhatsApp tiene un test rojo.
- `ProviderReference` se expone públicamente y sirve para leer checkout.
- Faltan refund, failure, expiration, chargeback, reconciliation y audit ledger.

## WhatsApp Findings

### Fortalezas

- Provider abstraction.
- Normalización Meta separada.
- Dedupe por external message ID.
- Delivery status rank para evitar retrocesos normales.
- Retry con backoff básico y límite de intentos.
- Soporte mock y Meta.

### Gaps/bloqueadores

- AppSecret vacío desactiva autenticación de webhook.
- Recepción invoca IA sincrónicamente.
- Delivery worker no hace claim/lease atómico.
- Sender no es channel-aware y usa token/phone global.
- No hay rate limiting, circuit breaker o DLQ visible.
- Retry puede guardar `ProviderMessageId` sobre el mismo registro sin idempotency provider guarantee.
- No hay event log de webhook raw, timestamp, provider event ID y resultado.
- Hay paths mock anónimos con firma hardcoded.

## Testing Gaps

### Cobertura existente

- Health endpoint.
- Auth/tenant básico.
- Catalog y SKU.
- Cart básico.
- Orders/idempotency/state machine/details/history.
- Payments/mock webhook.
- WhatsApp mock, agent, imágenes y delivery.
- Meta parser.
- Tenant isolation básico.
- Playwright smoke de login/dashboard/catalog/conversations/agent.

### Faltantes prioritarios

- Concurrencia real de stock con PostgreSQL.
- Doble submit/replay del checkout público.
- IDOR matrix para cada endpoint y cada tenant.
- Roles/permissions.
- JWT expirado, issuer/audience incorrectos, signing key rotation y revocación.
- Meta HMAC válido/inválido, body alterado, replay y AppSecret ausente.
- Payment webhook vs mock-confirm concurrentes.
- Provider reference guessing/leakage/expiry.
- Delivery worker con dos instancias y reclaim de leases.
- Agent prompt injection, ambiguidad de variantes y mensajes fuera de orden.
- Upload magic bytes, corrupt images, decompression bomb, SSRF/redirects en imports.
- Public store/checkout end-to-end.
- Backup/restore y migraciones sobre PostgreSQL real.
- Load/performance tests para listados e imágenes.

## Deployment Gaps

- Secret manager y rotación no implementados.
- No hay evidencia de pipeline CI/CD con gates.
- No hay configuración productiva de reverse proxy, TLS termination, HSTS, headers, CSP o CORS documentado.
- Docker Compose es local y usa passwords/ports estáticos.
- No hay backup/restore verificado para PostgreSQL.
- No hay migración automatizada segura ni estrategia de rollback.
- No hay worker deployment/locking distribuido para WhatsApp.
- No hay alertas, SLOs, dashboards ni centralización de logs/traces.
- No hay rate limits, quotas de AI ni control de costos.
- No hay política de retención/borrado para PII, mensajes, imágenes, pagos y webhooks.
- `README.md` y documentación de estado no reflejan el alcance actual.
- Una suite backend está roja y frontend lint está rojo; no hay baseline verde para release.

## Recommended Roadmap

### Fase 0: Contención inmediata antes de Internet

1. Revocar/rotar Gemini, WhatsApp, webhook y cualquier credencial expuesta.
2. Eliminar secretos de configuración tracked y del historial según política de seguridad.
3. Deshabilitar todos los endpoints mock en ambientes no Development/Test.
4. Hacer obligatorio HMAC Meta y añadir replay protection.
5. Cambiar/revocar signing key JWT y rechazar el placeholder fuera de Development.
6. Desactivar o proteger el seeder demo en ambientes compartidos.

### Fase 1: Integridad de commerce/payment

1. Implementar reserva/decremento atómico de stock y probar con PostgreSQL concurrente.
2. Añadir idempotencia pública con request hash, TTL y respuesta replayable.
3. Encapsular orden + stock + payment record + outbox en transacción consistente.
4. Diseñar estados de payment completos, reconciliación, expiración y refunds.
5. Corregir el flujo de recibo público/WhatsApp y dejar 33/33 o la nueva suite completamente verde.

### Fase 2: Auth, authorization y tenant security

1. Validar sesión real en middleware/BFF y limpiar cookies inválidas.
2. Definir RBAC/policies y resource authorization.
3. Centralizar tenant context/queries; evaluar PostgreSQL RLS para recursos críticos.
4. Añadir tests exhaustivos de IDOR y cross-tenant write/read.
5. Normalizar teléfonos y definir identidad customer/channel.

### Fase 3: WhatsApp fiable

1. Convertir inbound y outbound a outbox/inbox con claim leases.
2. Hacer sender dependiente de `ChannelAccountId` y secretos por canal.
3. Persistir eventos raw, idempotency keys, intentos, errores y correlación.
4. Agregar backoff, DLQ, circuit breaker y métricas de proveedor.
5. Procesar IA fuera de la request del webhook.

### Fase 4: AI production controls

1. Registrar tool calls y efectos auditables sin guardar secretos ni PII innecesaria.
2. Pasar catálogo/contexto como datos estructurados y tratar todo contenido como no confiable.
3. Exigir confirmaciones ligadas a cart/version/variant.
4. Agregar límites de tiempo, tokens, costo, retries y fallback.
5. Implementar handoff, pause/resume y reglas de escalación.
6. Crear evaluación de conversaciones y pruebas de no alucinación para precio/stock/pago.

### Fase 5: Operabilidad y performance

1. Paginación, filtros, índices basados en queries y límites de payload.
2. Migrar imágenes a object storage con thumbnails/URLs firmadas.
3. Añadir rate limiting, quotas, secure headers y políticas de cache.
4. Exportar OpenTelemetry a collector/backend real y crear alertas.
5. Crear CI con build, lint, tests, E2E, secret scan, dependency scan y migration checks.
6. Documentar despliegue, backup/restore, rollback, incident response y retención de datos.

### Fase 6: UX y producto

1. Retirar métricas decorativas o sustituirlas por datos reales con attribution model.
2. Reparar lint y warnings relevantes antes de ampliar superficie.
3. Añadir estados de loading/error/empty y revalidación de stock en checkout.
4. Completar dashboard con analytics reales después de definir modelo de eventos.
5. Mantener documentación y roadmap sincronizados con el código.

## Audit Limitations

- La auditoría fue estática más validaciones locales; no fue un pentest ni una revisión de infraestructura cloud.
- No se inspeccionaron secretos históricos con herramientas de secret scanning ni se verificó el remoto Git.
- No se ejecutó una carga concurrente contra PostgreSQL de producción.
- La suite E2E visible depende de servicios locales y datos seeded.
- El informe refleja el árbol observado el 2026-09-16. Las contradicciones entre documentos históricos y código actual deben resolverse como parte del roadmap.

## Remediation Status — Tenant Isolation Pass

**Fecha de revisión:** 2026-09-16

Se implementaron correcciones específicas para el aislamiento multi-tenant:

- El webhook de pagos mock ya no acepta `TenantId` en el payload. El tenant se deriva del registro de pago localizado por proveedor y referencia.
- Se agregó una prueba de privilege escalation que intenta usar un `TenantId` ajeno junto con la referencia de pago de otro tenant.
- Se agregó una suite de acceso cross-tenant que prueba lecturas de productos, customers, órdenes, conversaciones y AI search, además de modificaciones de producto, stock, imágenes, mensajes y estados de órdenes.
- El middleware de Next.js dejó de confiar únicamente en la presencia de la cookie y valida la sesión contra `/api/tenants/me` antes de permitir el dashboard.
- `docs/SECURITY.md` y `docs/DECISIONS.md` documentan que el tenant autenticado es la única fuente válida de contexto para operaciones protegidas.

Validación de este pass:

- `TenantAccessControlTests`: **1/1 pasa**.
- `PaymentsTests`: **pasa** después de liberar el proceso local que bloqueaba el binario durante la primera ejecución.
- Suite backend completa posterior: **34/35 pasa**; permanece el fallo conocido `PaymentsTests.Public_ecommerce_paid_order_queues_whatsapp_receipt`.
- API build: **pasa**.
- Web build: **pasa**.
- Web lint: continúa fallando con **3 errores y 35 warnings preexistentes**.

Estos cambios no resuelven todavía los riesgos de concurrencia de stock, firmas HMAC reales/obligatorias, RBAC, rate limiting, referencias públicas de checkout ni el routing multi-canal de WhatsApp; permanecen como trabajo posterior.
