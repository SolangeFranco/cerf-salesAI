# Production Readiness

**Revisión:** 27 de septiembre de 2026
**Veredicto:** **NO LISTO PARA PRODUCCIÓN. No se ha desplegado producción ni staging según la evidencia disponible.**

### Verificación local del 27 de septiembre

- Compose local quedó iniciado sin eliminar volúmenes: Postgres, API, web y Redis saludables; WhatsApp bridge activo.
- `/health/live` y `/health/ready`: HTTP 200. La base local se recreó al final de las pruebas y la API volvió a aplicar migraciones.
- Suite Playwright final: **6 passed, 0 failed**, serial y con merchants/productos/slugs aislados; incluye onboarding, catálogo, AI, handoff, pedido, checkout público y transferencia manual.
- Tests backend final: **279 passed, 0 failed, 0 skipped**.
- Se corrigió el descubrimiento de `AddProductSkuSequence`; una base nueva ahora crea `product_sku_sequence` y pasa el flujo de producto.
- Se retiró el usuario/password fijo y el seeder demo de Development. Dashboard/Orders consultan APIs tenant-scoped y muestran estados vacíos; landing sin KPIs, testimonios, precios o resultados no verificados.
- Meta usa el `PhoneNumberId` del canal tenant-scoped para outbound. Production rechaza la connection string local/development y exige `Commerce:BaseUrl` HTTPS externo.
- Las tarifas de delivery ya no se precargan con importes comerciales; el merchant debe configurarlas durante onboarding.

## Resumen ejecutivo

El repo contiene un vertical slice funcional y pruebas backend amplias. La producción se configura con `Payment:Provider=manual-transfer`: no existe procesador automático. El pago queda pendiente hasta que un usuario autenticado del tenant confirma, tras verificar el depósito en la cuenta bancaria. VANTA no consulta bancos ni procesa refunds manuales.

El guard de producción rechaza `mock` y providers no registrados, y exige configuración externa de JWT, WhatsApp Meta, AI y OTLP. Tener validadores o variables no significa que servicios/credenciales reales estén provisionados.

## Hechos comprobados en código/tests

- `ManualTransferPaymentProvider`: creación/status pendientes, webhooks inválidos y refund no soportado.
- Configuración bancaria tenant-scoped; checkout público muestra instrucciones sólo para pagos manuales.
- Instrucciones outbound se encolan por WhatsApp si hay canal; merchant-confirm es tenant-authenticated y auditado.
- Expiración de `AwaitingPayment` utiliza `ReservationExpiresAt`, job idempotente, transición del pago y liberación de reservas; el pago tardío queda `PaidAfterExpiration` para revisión.
- Validación registrada el 28 Sep 2026: backend unit/in-process 279 passed; E2E 6/6; API Release build y web typecheck/production build pasaron. ESLint no reportó errores; persisten warnings.

## Gates de no-go

| Gate | Estado | Evidencia pendiente |
|---|---|---|
| Backup/restore | NO VERIFICADO | Configurar destino/secretos, ejecutar backup y restaurar en DB aislada; documentar RPO/RTO |
| E2E locales | VERDE (mock/local) | 6/6 pasan con fixtures aislados; proveedores reales y producción requieren aceptación independiente |
| Dashboard/KPIs | CORREGIDO LOCALMENTE | Datos tenant-scoped o estado vacío; no presenta valores ficticios |
| WhatsApp/AI externos | NO VERIFICADO | Credenciales aprobadas, webhooks públicos firmados, pruebas de aceptación y alertas |
| Observabilidad externa | NO CONFIGURADA | Backend OTLP, dashboards, alertas y responsables |
| Pago manual | IMPLEMENTADO; operación NO APROBADA | Procedimiento, responsables, verificación bancaria, soporte y refund externo |
| Entorno cloud | STAGING EN PREPARACIÓN; PRODUCCIÓN NO DESPLEGADA | Supabase creado; ruta Vercel/Supabase/Render preparada; faltan Render, Vercel y smoke cloud |
| Seguridad/privacidad | PARCIAL | Revisión final, retención/borrado, respuesta a incidentes y límites de acceso |

## Procedimiento mínimo para transferencia

1. Merchant carga banco, beneficiario, cuenta, CI/RUC y alias correctos.
2. Cliente recibe datos e importe por checkout/WhatsApp.
3. Merchant comprueba el abono real en la cuenta bancaria; no confiar en captura o redirect.
4. Merchant confirma desde VANTA con sesión del tenant correcto.
5. Para refund, operar fuera del sistema y conservar evidencia externa; VANTA no lo marca como refund completado.

## Riesgos y límites aceptados sólo por decisión explícita

- No hay provider automático ni reconciliación.
- Imágenes siguen en PostgreSQL/Base64; object storage pendiente.
- RBAC granular no está aplicado.
- AI/tool-selection es determinista y la calidad del provider real no está evaluada.
- No hay load test ni SLO medidos.
- Jobs tienen retries/estado, pero el backend de alertas/replay operativo falta.

## Condición de salida

No desplegar ni aceptar datos reales hasta cerrar los gates, hacer una revisión final del checklist y recibir aprobación explícita de infraestructura/operación. Ver [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) y [DEPLOYMENT_PRODUCTION.md](DEPLOYMENT_PRODUCTION.md).
