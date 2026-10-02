# API

**Estado:** REST sobre el modular monolith. Las rutas actuales usan `/api`; no existe versionado `/api/v1` ni API separada de billing/admin.

## Mapa de rutas

| Área | Rutas principales | Acceso |
|---|---|---|
| Identidad | `/api/auth/*` | Anónimo, con rate limit |
| Tenant | `GET /api/tenants/me`, `PUT /api/tenants/settings` | JWT, tenant del principal |
| Catálogo e inventario | `/api/products/*` | JWT, tenant-scoped |
| Clientes/carritos | `/api/customers/*`, `/api/conversations/{id}/cart*` | JWT, tenant-scoped |
| Pedidos | `/api/orders`, `/api/orders/{id}`, `/api/orders/{id}/status` | JWT, tenant-scoped |
| Pagos | `/api/orders/{id}/payments*`, `/api/payments/{id}/*` | JWT, tenant-scoped |
| Conversaciones | `/api/conversations/*` | JWT, tenant-scoped |
| Canales y webhooks | `/api/channels/whatsapp/*`, `/api/webhooks/whatsapp/*`, `/api/webhooks/payments/*` | JWT para administración; firma/provider en webhooks |
| Bridge WhatsApp | `POST /api/webhooks/whatsapp/linked-device` | Anónimo a nivel HTTP; bridge secret validado |
| Knowledge y analítica | `/api/knowledge/*`, `/api/analytics/*` | JWT, tenant-scoped |
| Storefront | `/api/public/store/{slug}/*`, `/api/public/checkout/{paymentReference}` | Anónimo, rate-limited |
| Health | `/health/live`, `/health/ready`, `/health` | Anónimo; respuesta sin datos sensibles |

El inventario exacto se define en los endpoint modules bajo `apps/api/src/Vanta.Api/Modules/`. Este mapa no sustituye OpenAPI.

## Settings bancarios

`PUT /api/tenants/settings` guarda `BankName`, `BankBeneficiaryName`, `BankAccountNumber`, `BankDocumentNumber` y `BankAlias`, además de preferencias de tienda y delivery. La operación usa exclusivamente el tenant del JWT.

## Transferencia manual

- `POST /api/orders/{id}/payments/manual-transfer`: requiere `Idempotency-Key`, pedido listo para pago y banco, beneficiario, cuenta y documento configurados. Crea pago `Pending`; repetir la clave devuelve la misma operación e instrucciones.
- `POST /api/payments/{id}/manual-confirm`: acción autenticada/tenant-scoped para que el comercio marque el pago tras verificar el abono real.
- `POST /api/payments/{id}/verify`: el provider manual siempre informa `Pending`; no confirma depósitos.
- `POST /api/payments/{id}/refund`: para `manual-transfer` responde `409`; el reembolso se gestiona fuera de VANTA.
- `GET /api/public/checkout/{paymentReference}` muestra datos bancarios sólo cuando el pago usa `manual-transfer`; nunca expone `ProviderReference`.

Pedidos públicos en modo transferencia encolan instrucciones por WhatsApp cuando existe canal activo. BFF puede usar método distinto en sus route handlers y traducir al método de API; revisar ambas capas al cambiar contratos.

## Invariantes

- Todo recurso de tenant requiere autorización y filtro por `TenantId`; otro tenant responde `404`.
- Usar idempotencia en pedidos y pagos con efectos persistentes.
- `Paid` sólo procede de webhook validado, verificación server-to-server, confirmación manual autenticada o confirmación mock en Development/Testing.
- Redirect o comprobante del cliente nunca confirman pago.
- Errores no exponen stack traces, secretos ni detalles internos.
- Ejemplos `/api/v1/*` históricos son objetivos futuros, no rutas activas.
