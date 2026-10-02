# Observabilidad

## Estado

El API emite logs JSON a stdout y está instrumentado con OpenTelemetry para traces/métricas. `OpenTelemetry:OtlpEndpoint` configura exportación OTLP; en Development/Testing puede usarse Console exporter. El código de export no significa que exista collector, backend, alertas o responsables provisionados. **Producción no está configurada ni desplegada.**

## Logging y privacidad

Cada request registra `RequestId`, `CorrelationId`, método, ruta, status, duración y servicio. El baseline no registra bodies ni Authorization/cookies. No incluir tokens, secretos, connection strings, prompts completos, teléfono/dirección del cliente ni datos bancarios en logs, tags o excepciones serializadas.

## Métricas y trazas

El meter `Vanta.Api` expone medidas de requests, errores, duración, webhooks, pagos, IA, jobs y WhatsApp. ASP.NET Core/HttpClient están instrumentados; Activities de dominio cubren operaciones seleccionadas. Los tags deben ser de baja cardinalidad y no incluir tenant/customer IDs.

Health checks:

- `GET /health/live`: proceso, sin dependencia de DB.
- `GET /health/ready`: proceso y PostgreSQL.
- `GET /health`: alias de readiness.

Respuestas de health no deben revelar hosts, credenciales ni excepciones internas.

## Configuración

Producción requiere `OpenTelemetry:OtlpEndpoint` mediante configuración segura. Secretos de API, WhatsApp, AI y DB se gestionan fuera de Git. Nunca incluir valores reales en Markdown o `.env.example`.

## Gaps operativos

No hay backend OTLP, dashboards, alertas externas, on-call/runbook aprobado ni baseline de SLO/p95/p99/load-test demostrado. Antes de recibir datos reales configurar retención, alertas de readiness/5xx, DB, jobs, webhooks, pagos, proveedor AI y presupuesto. Registrar quién responde cada alerta y probar su notificación.

Ver [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) y [DEPLOYMENT_PRODUCTION.md](DEPLOYMENT_PRODUCTION.md).
