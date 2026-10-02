# Background Jobs

## Arquitectura

VANTA usa `background_jobs` en PostgreSQL y un `BackgroundJobWorker` dentro de la API. Jobs llevan tenant, tipo, key idempotente, estado, payload pequeño, lease, intentos, disponibilidad, error y timestamps. No se usa broker externo; Redis está provisionado en Compose pero no participa.

## Garantías implementadas

- Clave única `(TenantId, Type, IdempotencyKey)` para enqueue idempotente.
- Retry con backoff y estado terminal `Failed` después del máximo configurado.
- Lease permite recuperar trabajos si el proceso cae.
- Claim condicional en PostgreSQL para evitar que dos workers ejecuten la misma operación.
- Tenant context retenido; el worker valida tenant y recurso antes de efectos.
- El estado/errores persisten; dashboards y alertas operativas externas no están provisionados.

## WhatsApp delivery

El worker recoge mensajes outbound pendientes, encola/reutiliza jobs idempotentes, envía por adapter configurado y registra estado/errores/reintentos. La request de negocio no espera la llamada de delivery. Meta/bridge reales necesitan credenciales y smoke tests de proveedor.

## Expiración de reserva de orden

Pedidos `AwaitingPayment` llevan `ReservationExpiresAt`, configurable con `OrderReservations:ExpirationMinutes` (default 30, rango 1–1440). El worker identifica vencidos, encola un job por orden y el procesador, de forma transaccional:

1. Intenta transición condicional del pago pendiente a `Expired`.
2. Si gana la carrera, cancela la orden y libera reservas activas.
3. Registra historial de pago/orden y movimiento de inventario.
4. Si webhook/verificación ya confirmó, expiración no cambia la orden.
5. Si el pago se confirma después de expirar, conserva orden cancelada/liberación y marca pago `PaidAfterExpiration`; requiere revisión humana.

Para transferencias manuales también se aplica TTL desde `AwaitingPayment`. Un depósito posterior a vencimiento no recrea reservas automáticamente; el merchant debe escalar la discrepancia. La política de refund manual se opera fuera de VANTA.

La migración añade `ReservationExpiresAt`, índice y backfill histórico. Ver ADR-026 y tests de `OrderExpirationTests`/`LatePaymentAfterExpirationTests`.

## No implementado como jobs

- Enviar campañas, follow-ups o recuperación automática de carritos.
- Procesar AI inbound de forma asíncrona (conservar contrato actual de webhook).
- Ingesta Knowledge asíncrona.
- Agregación analítica periódica.

## Operación pendiente

Antes de producción, provisionar alertas para jobs Failed, edad del job pendiente más antiguo, retries agotados, lease expirado y fallo de delivery; documentar replay/retry con auditoría.
