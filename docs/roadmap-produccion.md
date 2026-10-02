# Auditoría ejecutiva y roadmap de producción para VANTA

## Resumen ejecutivo

Revisé backend, frontend, catálogo, clientes, pedidos, pagos, inventario, WhatsApp/IA, conocimiento, jobs, analytics, observabilidad y checkout.

La base técnica es sólida: modular monolith, aislamiento por tenant, máquinas de estado, idempotencia, reservas de inventario y buena cobertura de tests. Sin embargo, todavía no consideraría el sistema listo para producción pública.

## Riesgos prioritarios

### 1) Pagos reales pendientes
Actualmente existe principalmente `MockPaymentProvider`. Antes de producción se necesita integrar Bancard, Pagopar o Tigo Money con sandbox, webhooks, reconciliación y refunds.

### 2) Imágenes almacenadas en PostgreSQL
Las imágenes se guardan como `byte[]`. Funciona para demo, pero crecerá demasiado rápido. Conviene migrar a S3, Cloudflare R2 o similar, guardando solo metadata y URLs en PostgreSQL.

### 3) Backups y restore no probados
Tener backups sin probar la restauración no garantiza recuperación. Hay que definir RPO/RTO y ejecutar un restore real en staging.

### 4) Observabilidad incompleta
Actualmente los logs y métricas dependen principalmente de consola. Faltan almacenamiento histórico, alertas y dashboards.

### 5) Paginación backend ausente
Productos, clientes y pedidos cargan grandes volúmenes en memoria. Esto funcionará para demos, pero no para cientos o miles de registros.

### 6) Búsqueda limitada
La búsqueda de productos y pedidos es principalmente filtrado en frontend o matching textual. Conviene agregar búsqueda PostgreSQL/FTS y luego búsqueda semántica.

### 7) Reservas de stock sin expiración automática
Una orden en `AwaitingPayment` puede mantener stock reservado indefinidamente.

### 8) IA conversacional todavía determinística
El agente funciona, pero el routing depende mucho de regex y detección de palabras. Falta memoria multiturno y clasificación robusta de intención.

## Mejoras de IA con mayor potencial diferencial

### 1. Agente de ventas multiturno
Agregar memoria resumida por conversación:
- Preferencias del cliente
- Talles y colores
- Presupuesto
- Productos vistos
- Objeciones
- Estado de compra
- Nivel de intención

Esto diferenciaría más a VANTA que agregar simplemente otro chatbot.

### 2. Copiloto de conversación para el vendedor
Mientras el comerciante atiende una conversación, mostrar:
- Resumen automático
- Próxima mejor acción
- Productos recomendados
- Respuestas sugeridas
- Riesgo de abandono
- Señal de frustración
- Botón de handoff recomendado

### 3. Inteligencia de inventario
Usar las ventas para detectar:
- Productos que se agotarán pronto
- Talles o variantes más demandadas
- Productos lentos
- Reorden recomendado
- Stock ideal por temporada
- Riesgo de sobrestock

### 4. Catálogo asistido por IA
Al subir una imagen:
- Generar nombre
- Descripción comercial
- Categoría
- Tags
- Variantes sugeridas
- SEO
- Texto para WhatsApp e Instagram
- Advertencia de imagen de baja calidad

### 5. Recuperación inteligente de carritos
No sólo enviar un mensaje fijo. La IA debería elegir:
- Momento de contacto
- Canal
- Mensaje
- Producto alternativo
- Incentivo permitido
- Escalamiento humano

### 6. Analítica accionable
El dashboard debería responder:
- “¿Por qué bajaron mis ventas?”
- “¿Qué producto debería promocionar?”
- “¿Qué clientes están por abandonar?”
- “¿Qué conversaciones generan más ingresos?”
- “¿Qué variante debería reponer?”

No sólo mostrar métricas, sino explicar causas y recomendar acciones.

## Orden recomendado

### Fase 1: Antes de producción
- Integrar proveedor de pago real
- Probar backup y restore
- Migrar imágenes a object storage
- Validar secretos y configuración al iniciar
- Agregar observabilidad persistente
- Agregar expiración de reservas
- Agregar tests para descuentos, stock distribuido, ProductNumber y checkout

### Fase 2: Escalabilidad operativa
- Paginación backend
- Búsqueda PostgreSQL FTS
- Carrito persistente para clientes anónimos
- Historial y edición completa de clientes
- Timeline de pedidos
- Bulk actions de productos
- Importación CSV/Excel

### Fase 3: Diferenciación con IA
- Memoria multiturno
- Copiloto de conversaciones
- Recuperación inteligente de carritos
- Catálogo asistido por imagen
- Forecast de inventario
- Segmentación y predicción de churn
- Analítica con recomendaciones

## Mi evaluación
VANTA tiene una buena base para un demo comercial convincente y una arquitectura más rigurosa que muchos MVPs. El mayor riesgo sería invertir demasiado pronto en más funcionalidades de IA antes de resolver pagos, backups, imágenes, observabilidad y escalabilidad básica.

La ventaja competitiva más fuerte debería ser:

Un agente de ventas de WhatsApp que recuerda el contexto, entiende el negocio del merchant y convierte conversaciones en acciones medibles.

Esa propuesta es más defendible que competir sólo por catálogo, tienda online o chatbot genérico.

## Instrucciones para Copilot / sub-agentes

Necesito ejecutar una auditoría técnica y roadmap de mejora para el proyecto VANTA.  
El sistema ya tiene una base sólida: backend modular monolith, frontend, catálogo, clientes, pedidos, pagos, inventario, WhatsApp/IA, base de conocimiento, jobs, analytics, observabilidad y checkout.

El objetivo no es implementar todo en un solo PR.  
Divide el trabajo en sub-tareas independientes, priorizadas y revisables. Cada sub-agente debe:

1. Analizar el estado actual del código.
2. Identificar archivos/módulos afectados.
3. Implementar cambios mínimos pero completos.
4. Agregar o actualizar tests.
5. Documentar decisiones técnicas.
6. Crear un PR pequeño y enfocado.
7. Evitar romper APIs existentes salvo que sea necesario y esté documentado.

Prioridad general:
1. Producción segura
2. Escalabilidad operativa
3. Diferenciación con IA

## Fase 1 — Antes de producción

### Sub-agente 1: Integración de proveedor de pagos real
Objetivo: reemplazar o complementar `MockPaymentProvider` con una integración real.
Tareas:
- Revisar módulo actual de pagos
- Mantener mock solo para tests/dev
- Agregar provider configurable
- Implementar creación, confirmación y refunds
- Idempotencia y webhooks
- Documentar configuraciones

Criterios de aceptación:
- Provider seleccionable por env
- Mock sigue siendo válido para dev/test
- Webhooks idempotentes
- Pedido actualizado correctamente
- Documentación de sandbox/producción

### Sub-agente 2: Migración de imágenes a object storage
Objetivo: no guardar imágenes como `byte[]` en PostgreSQL.
Tareas:
- Revisar modelos con imágenes
- Diseñar metadata mínima
- Crear `ObjectStorageService`
- Soportar S3 o R2
- Migración y scripts
- Frontend consume URLs

Criterios de aceptación:
- DB solo guarda metadata
- Nuevas imágenes no se guardan como bytes
- Tests de upload y download
- Documentación de bucket

### Sub-agente 3: Backups y restore probado
Objetivo: definir y probar backup/restore.
Tareas:
- Revisar stack de DB
- Definir RPO/RTO
- Script de backup
- Runbook de restore
- Validar staging
- Checklist post-restore

Criterios de aceptación:
- Existe dry-run/restore validado en staging
- Documentación clara
- Se define RPO/RTO

### Sub-agente 4: Observabilidad persistente
Objetivo: migrar de logs de consola a observabilidad útil para producción.
Tareas:
- Logging estructurado
- Correlation ID
- Metrías y dashboards
- Alertas
- Trazabilidad de pagos, checkout, stocks, jobs e IA

Criterios:
- Logs estructurados
- Request ID para traces
- Métricas mínimas disponibles
- Alarma básica documentada

### Sub-agente 5: Expiración automática de reservas de stock
Objetivo: liberar stock reservado en `AwaitingPayment`.
Tareas:
- Revisar estados y reservas
- Agregar expiración
- Job periódico
- Cambio de estado de pedido
- Liberar stock
- Tests

Criterios de aceptación:
- Reserva no queda indefinida
- Job idempotente
- Tests para reservas vencidas

### Sub-agente 6: Validación de secretos y configuración al iniciar
Objetivo: detectar configuración faltante o inválida temprano.
Tareas:
- Revisar env vars
- Validar configuración crítica
- Fallar rápido en producción
- Documentar `.env.example`

Criterios:
- app falla en producción si faltan secrets
- no se usan mocks por accidente
- config actualizada

### Sub-agente 7: Tests críticos
Objetivo: reforzar flujo de negocio.
Tareas:
- descuentos
- stock distribuido
- checkout
- `ProductNumber`
- pagos fallidos/confirmados
- reservas concurrentes

Criterios:
- flujos básicos cubiertos
- idempotencia validada
- tests automatizados

## Fase 2 — Escalabilidad operativa

### Sub-agente 8: Paginación backend
Objetivo: evitar cargar grandes cantidades de datos.
Tareas:
- endpoints paginados
- filtros y orden
- limit/offset o cursor
- frontend actualizado

### Sub-agente 9: Búsqueda PostgreSQL FTS
Objetivo: mejorar busqueda con índices de texto completo.
Tareas:
- definir tabla/index
- agregar ranking
- tests

### Sub-agente 10: Carrito persistente para anónimos
Objetivo: no perder carrito si client no está logueado.
Tareas:
- persistencia segura
- expiración
- merge con usuario logueado

### Sub-agente 11: Historial y edición completa de clientes
Objetivo: permitir editar y consultar historial.
Tareas:
- historial de pedidos/conversaciones
- edición segura
- permisos por tenant

### Sub-agente 12: Timeline de pedidos
Objetivo: mostrar evolución del pedido.
Tareas:
- eventos del ciclo de vida
- API y frontend
- timeline auditable

### Sub-agente 13: Bulk actions de productos
Objetivo: operaciones masivas sobre catálogo.
Tareas:
- seleccionar múltiples
- activar/desactivar, cambiar categoría, stock o precio
- validación por tenant

### Sub-agente 14: Importación CSV/Excel
Objetivo: carga masiva.
Tareas:
- parsing
- validación por fila
- preview
- upsert si aplica
- tests

## Fase 3 — Diferenciación con IA

### Sub-agente 15: Memoria multiturno
Objetivo: recordar contexto del cliente.
Tareas:
- resumir la conversación
- guardar preferencias, talles, presupuesto, productos vistos, intenciones
- tests y seguridad

### Sub-agente 16: Copiloto de conversación
Objetivo: ayudar al vendedor.
Tareas:
- resumen automático
- próxima acción
- respuesta sugerida
- nivel de riesgo y abandono

### Sub-agente 17: Recuperación inteligente de carritos
Objetivo: recuperar carritos abandonados de forma contextual.
Tareas:
- decidir momento
- canal y mensaje
- alternativa y incentivo
- límites anti-spam

### Sub-agente 18: Catálogo asistido por imagen
Objetivo: sugerir nombre, descripción, tags, variantes, SEO y texto promocional.
Tareas:
- pipeline de análisis
- preview antes de guardar
- fallback seguro

### Sub-agente 19: Inteligencia de inventario
Objetivo: recomendar reorden y forecast.
Tareas:
- detectar agotamiento
- tallas/variantes demandadas
- stock recomendado
- riesgo de overstock

### Sub-agente 20: Segmentación y predicción de churn
Objetivo: detectar clientes de riesgo.
Tareas:
- segmentación por comportamiento
- métricas clave
- recomendaciones

### Sub-agente 21: Analítica accionable
Objetivo: explicar causalidades y recomendar acciones.
Tareas:
- responder preguntas de negocio
- insights accionables
- razonamiento de causa y recomendación

## Orden recomendado de ejecución

### Bloque A — Producción mínima segura
1. Pagos reales
2. Expiración de reservas
3. Validación de configuración/secrets
4. Backups y restore
5. Migración de imágenes
6. Observabilidad persistente
7. Tests críticos

### Bloque B — Escalabilidad operativa
1. Paginación backend
2. PostgreSQL FTS
3. Carrito persistente
4. Timeline de pedidos
5. Historial de clientes
6. Bulk actions
7. Importación CSV/Excel

### Bloque C — Diferenciación IA
1. Memoria multiturno
2. Copiloto de conversaciones
3. Recuperación inteligente de carritos
4. Catálogo asistido por imagen
5. Forecast de inventario
6. Segmentación y predicción de churn
7. Analítica accionable

## Recomendación final
No priorices nuevas features de IA antes de resolver producción básica.

La ventaja competitiva principal de VANTA debe ser:
Un agente de ventas de WhatsApp que recuerda el contexto, entiende el negocio del merchant y convierte conversaciones en acciones medibles.

Pero esa ventaja sólo será sostenible si primero se aseguran pagos, backups, imágenes, observabilidad, stock y escalabilidad básica.