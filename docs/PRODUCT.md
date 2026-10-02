# Producto

**Corte:** 27 de septiembre de 2026. La visión y las hipótesis no implican validación comercial ni disponibilidad productiva.

## Posicionamiento

VANTA busca ser un agente de ventas con IA para comercios que ya venden por WhatsApp: responder consultas basadas en catálogo y stock, guiar una compra y dar al merchant herramientas para supervisar conversaciones y pedidos.

## Usuario y problema

Merchant paraguayo pequeño/mediano con consultas repetitivas de precio, disponibilidad, variantes, delivery y pago. Moda/calzado y belleza son segmentos candidatos, sujetos a entrevistas y primeros pilotos.

## Vertical slice implementado

- onboarding, tenant isolation y configuración de tienda;
- catálogo, variantes, inventario y reservas;
- WhatsApp adapters/webhooks, conversaciones, handoff humano y envío con jobs;
- herramientas de agente deterministas, Knowledge/pgvector y providers externos configurables;
- carritos, pedidos, checkout público, seguimiento e historial;
- eventos comerciales, atribución basada en datos persistidos y dashboard;
- pago mock en Development/Testing y transferencia manual como opción de producción.

## Pago manual

El merchant configura banco, beneficiario, cuenta, CI/RUC y alias. El checkout presenta esas instrucciones y se encola una notificación WhatsApp cuando hay canal activo. El estado sólo cambia a `Paid` tras confirmación autenticada del merchant. No existe integración automática con banco/procesador, conciliación ni refund bancario dentro de VANTA. El comprobante no constituye confirmación.

## Límites de entrega

La selección de tools del agente es determinista; no hay LLM function-calling autónomo. AI y WhatsApp requieren credenciales externas para operar con servicios reales. Campañas, billing, automatización de carrito abandonado, object storage y logística de carriers no están implementados como módulos productivos.

El overview `/dashboard` y la lista de pedidos consumen el resumen y las órdenes tenant-scoped del API; con una cuenta nueva muestran “Sin datos”/estado vacío. La landing no contiene cifras, testimonios, pricing ni catálogo falsos; cualquier vista conceptual se marca como ilustrativa. Los E2E crean tenants/productos únicos. En Compose el API corre en `Development` con provider mock; el checkout público en modo `manual-transfer` requiere una configuración de test/prod que registre ese provider.

## Criterios de validación

1. Merchant configura tienda y banco sin filtrar datos entre tenants.
2. Producto/variante y stock son correctos.
3. Conversación recomienda sólo datos existentes.
4. Pedido reserva inventario y expira de forma idempotente si no se paga.
5. Transferencia no se marca pagada antes de validar el abono y confirmar manualmente.
6. WhatsApp entrega instrucciones/recibo o registra fallo recuperable.
7. Dashboard muestra estado y atribución sin métricas fabricadas.

Los gates de release vigentes están en [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
