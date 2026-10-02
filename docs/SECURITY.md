# Seguridad

**Alcance:** invariantes implementadas y brechas operativas conocidas. No constituye certificación ni penetration test.

## Identidad y autorización

- Auth de API usa JWT validado; tenant context se deriva de `tenant_id` del principal.
- Toda lectura/escritura tenant-owned debe filtrar por tenant autenticado; cross-tenant devuelve `404`.
- BFF/middleware ayuda a proteger UX, pero no reemplaza autorización del API.
- No hay RBAC granular Owner/Editor/Viewer implementado.

## Secretos y datos

- No almacenar secretos, tokens, passwords, signing keys ni credentials reales en Git, Docker, tests, docs o logs.
- Usar variables seguras o secret manager por entorno; rotar credenciales si hubo exposición.
- No registrar cuerpos, prompts completos, authorization/cookies, PII innecesaria ni datos bancarios.
- Campos bancarios son tenant-owned. Sólo checkout asociado a referencia opaca `manual-transfer` expone las instrucciones al cliente.

## Pagos

- Production registra sólo `manual-transfer`; `mock` queda en Development/Testing y providers desconocidos se rechazan.
- Provider manual crea/consulta `Pending`, no valida webhooks ni ejecuta refunds.
- `POST /api/payments/{id}/manual-confirm` exige usuario autenticado y tenant coincidente. Antes, el merchant debe comprobar el abono en el banco por un canal independiente.
- Una captura, mensaje del cliente, flag frontend o redirect nunca confirma un depósito.
- Webhook/verify son para provider automático cuando exista; cada integración requerirá firma, idempotencia y pruebas propias.
- Las transiciones se centralizan en `PaymentStateMachine`/`PaymentService` con historial de fuente.

## WhatsApp y webhooks

- Meta valida verify token/firma y resuelve tenant a través de channel account; no confiar en tenant ID del body.
- Registrar idempotencia de evento y mensaje; operaciones deben ser retry-safe.
- Bridge vinculado usa secretos específicos y callbacks firmados; HTTPS/secrets reales son obligatorios fuera de local.

## IA

- LLM no recibe DB/SQL. `AgentToolExecutor` acepta sólo tools registradas, con inputs validados y tenant/conversation proporcionados por backend.
- Catálogo, mensajes y Knowledge son contenido no confiable; sanearlos y no permitir que instrucciones embebidas creen side effects.
- Tool selection actual es determinista; la IA no confirma manual-transfer.

## Baseline API

ProblemDetails genérico, body limits, rate limits por IP, secure headers, CORS cerrado por defecto, HTTPS redirection y request logging sin body/secrets. Límites MIME/tamaño de uploads no equivalen a scanning antimalware/normalización completa.

## Gates operativos

Antes de production: revisión de seguridad/privacidad, secrets y rotación probados, restore drill, alertas y responsables, dominio/TLS y procedimiento de incidentes. La instrumentación por sí sola no acredita que esos servicios estén configurados. Ver [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).