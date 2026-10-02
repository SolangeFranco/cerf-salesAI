# VANTA — Handoff técnico

**Corte:** 27 de septiembre de 2026
**Naturaleza:** snapshot de código y pruebas; no certifica un entorno desplegado.

## Estado

VANTA es un modular monolith de commerce conversacional: API ASP.NET Core .NET 10, web Next.js 16, PostgreSQL 16 + pgvector, jobs persistidos y adapters para WhatsApp/AI/pagos. Tiene un vertical slice demostrable localmente; no está listo para producción.

## Capacidades implementadas

- Identidad, tenant scope, catálogo/variantes, inventario transaccional y reservas.
- Clientes, carritos, pedidos y state machines con historiales.
- Conversaciones, herramientas AI controladas, handoff, WhatsApp webhook/delivery.
- Knowledge/pgvector, eventos de commerce, atribución determinista y dashboard.
- Storefront/checkout público y datos bancarios por tenant.
- `manual-transfer` en producción: pago Pending hasta confirmación autenticada del merchant. `mock` sólo en Development/Testing.
- Jobs PostgreSQL para delivery y expiración de reservas.

Implementado no significa desplegado, con credenciales reales o validado con merchants.

## Límites actuales

No hay procesador automático, conciliación bancaria ni refund de transferencia en VANTA. El merchant verifica el crédito y confirma; refund se hace fuera del sistema. No hay subscription billing, campañas, abandoned-cart outreach, object storage ni carrier integration. La IA selecciona tools determinísticamente; la quality/costo de provider externo no está validada.

## Entorno y despliegue

Docker Compose es local e incluye Postgres, Redis no utilizado, API, web y WhatsApp bridge. Railway está bajo evaluación; documentos cloud son propuestas fechadas, no contratos ni infraestructura creada. No se ha desplegado staging/production ni se acepta data real.

## Verificaciones registradas

- Backend tests: 279 passed, 0 failed, 0 skipped (28 Sep 2026).
- API Release build, web typecheck y production build: pass (26–27 Sep 2026).
- ESLint con `--quiet`: sin errores, warnings pendientes.
- E2E 27 Sep final: **6/6 pasan** serialmente con fixtures aislados; onboarding, AI, handoff, pedido, transferencia manual y storefront/checkout.
- Dashboard y lista de pedidos consultan datos tenant-scoped; landing no muestra cifras, planes, testimonios ni resultados inventados.
- Usuario/password de demo fijo retirado del arranque. Base local vaciada/recreada conservando el volumen y migrada; queda sin filas de tenant al cierre de validación.
- Se añadió metadata `[DbContext]`/`[Migration]` faltante a `AddProductSkuSequence`, detectada al probar con DB limpia.
- Backup/restore real, proveedores y observabilidad externa: sin evidencia de verificación.

## Prioridad de entrega

1. E2E determinista y restauración probada.
2. Credenciales, dominio, TLS, OTLP/alertas y secrets con aprobación.
3. Aprobar reconciliación manual y ejecutar piloto pequeño con monitoreo.
4. Seleccionar procesador automático sólo si la operación lo necesita.

Usar [README.md](../README.md) para setup, [API.md](API.md) para contratos, [DECISIONS.md](DECISIONS.md) para ADRs y [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) para los gates vigentes.
