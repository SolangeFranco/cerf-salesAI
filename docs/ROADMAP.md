# Roadmap

**Actualizado:** 27 de septiembre de 2026. El roadmap expresa prioridades; no afirma que exista despliegue ni PMF.

## Estado actual

El vertical slice cubre merchant, catálogo/stock, conversaciones WhatsApp, tools AI deterministas, Knowledge, carrito/pedido, checkout público, pagos manuales, dashboard y jobs básicos. El objetivo inmediato es estabilizar y validar con merchants, no añadir amplitud.

## Prioridad 0 — Cerrar preparación operativa

1. Completar E2E deterministas para onboarding, conversation, pedido, storefront y pago manual.
2. Ejecutar backup real y restore drill aislado; medir RPO/RTO.
3. Configurar credenciales y canales productivos sólo con aprobación y secret manager.
4. Provisionar backend OTLP, alertas, dominio/TLS, rollback y procedimiento de incidentes.
5. Aprobar proceso de conciliación: merchant verifica abono bancario antes de confirmar; definir ownership y manejo de refunds fuera de VANTA.

No se acepta tráfico/datos reales antes de completar gates y revisión humana. Estado vivo: [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).

## Prioridad 1 — Piloto controlado

Con gates aprobados, incorporar pocos design partners, acompañar onboarding, medir tiempo hasta primera venta y observar errores de pago, WhatsApp, inventario y delivery. Mantener transferencia manual; cada depósito se verifica fuera del sistema y se confirma por acción tenant-authenticated.

## Prioridad 2 — Integraciones y confiabilidad

- Implementar un procesador automático sólo tras seleccionar proveedor, revisar requisitos y obtener credenciales sandbox.
- Validar calidad/costo del AI con catálogos reales y política de fallback observable.
- Mejorar recuperación operativa de jobs fallidos, cuotas de abuso y retención de datos.
- Decidir object storage/RBAC/ingestion async con evidencia de uso.

## Futuro

Billing/subscriptions, campañas, abandono de carrito, catálogo de Meta, carrier integrations, fulfillment parcial y expansión LATAM requieren validación y decisiones separadas.

## Fuera de alcance sin evidencia

Microservicios, broker distribuido, Redis runtime, vector database aparte y despliegue multi-región. Añadirlos sólo para resolver una limitación medida.
