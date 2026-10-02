# VANTA — Project Brief

**Corte de estado:** 27 de septiembre de 2026
**Nombre:** VANTA es un nombre de trabajo; no se considera marca legalmente disponible.

## 1. Misión

Construir una plataforma SaaS para comercios paraguayos que convierta conversaciones de WhatsApp en descubrimiento de productos, carritos y pedidos, con IA controlada y resultados medibles. La tesis debe validarse con merchants; no se asume product-market fit.

## 2. Cliente inicial e hipótesis

Cliente objetivo: comercio paraguayo pequeño o mediano que recibe consultas por WhatsApp/Instagram, maneja catálogo y stock, y responde preguntas repetitivas de precio, disponibilidad, variantes, delivery y pago.

Moda, calzado y belleza son verticales candidatas, no una decisión irreversible. La adquisición inicial es founder-led con pocos design partners y aprendizaje semanal. Tamaño de mercado, conversión y disposición a pagar deben sustentarse con investigación reciente y entrevistas.

## 3. Producto actual

El repositorio contiene un vertical slice con:

- autenticación, tenant isolation, catálogo, variantes, inventario y reservas;
- clientes, conversaciones, estados AI/humano y handoff;
- canales y webhooks WhatsApp con adapters Meta, mock y bridge vinculado;
- selección determinista de herramientas de IA con catálogo cerrado, Knowledge/pgvector y providers externos opcionales;
- carritos, órdenes, checkout público, historial y eventos comerciales;
- pagos mock sólo para Development/Testing y transferencia manual para producción;
- jobs persistidos para delivery WhatsApp y expiración de reservas;
- dashboard, storefront y checkout Next.js.

“Implementado” no significa credenciales configuradas, aceptación de proveedor externo ni despliegue productivo. Ver [docs/PRODUCT.md](docs/PRODUCT.md) y [docs/VANTA_TECHNICAL_HANDOFF.md](docs/VANTA_TECHNICAL_HANDOFF.md).

## 4. Pago manual y límite de producción

El merchant guarda banco, beneficiario, número de cuenta, CI/RUC y alias. El checkout muestra instrucciones y VANTA encola un mensaje WhatsApp cuando existe canal. El pago permanece `Pending` hasta que un usuario autenticado del tenant verifica el abono y ejecuta la confirmación manual. VANTA no consulta bancos, no confirma por comprobante y no procesa reembolsos manuales.

No existe un procesador automático integrado. La transferencia manual es una opción operativa para un piloto controlado, sujeta a procedimiento aprobado. VANTA no está lista para producción ni se ha desplegado.

## 5. Arquitectura y stack

- API: .NET 10, ASP.NET Core Minimal APIs, EF Core, modular monolith.
- Web: Next.js 16, React 19, TypeScript, Tailwind y componentes del proyecto.
- Datos: PostgreSQL 16 + pgvector. Redis aparece en Compose, pero no se usa desde la aplicación.
- Asíncrono: `background_jobs` persistidos en PostgreSQL y worker alojado en la API.
- Integraciones volátiles aisladas mediante interfaces/adapters; el LLM nunca accede a SQL ni `VantaDbContext`.

Microservicios, Redis en runtime y nuevas nubes requieren necesidad medida y decisión documentada.

## 6. Invariantes de ingeniería

1. Modular monolith primero; no inventar requisitos comerciales.
2. Todo dato tenant-owned queda filtrado por tenant autenticado.
3. AI sólo opera mediante herramientas explícitas, validadas y auditables.
4. Pagos son idempotentes y state-machine based; nunca confiar en redirects ni estado del navegador.
5. WhatsApp, pagos y AI mantienen adapters específicos detrás de interfaces.
6. No guardar secretos en código, tests, documentación, logs o chat.
7. Revenue de IA sólo se muestra con atribución persistida y definida.
8. Cambios de esquema requieren migración EF Core explícita; no borrar volúmenes de datos locales como parte de validaciones.

## 7. Criterio de validación

Prioridad: registrar merchant → configurar tienda/productos → recibir conversación → recomendar producto → crear pedido → cobrar/confirmar → revisar resultado en dashboard. Validar cada tramo con datos controlados antes de ampliar roadmap.

El estado de build/tests está fechado en [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md). Gates externos de backup/restore, E2E, credenciales, observabilidad y conciliación se detallan en [docs/PRODUCTION_READINESS.md](docs/PRODUCTION_READINESS.md).

## 8. No objetivos actuales

No implementar ahora microservicios, campañas masivas, suscripciones/billing, fulfillment parcial, catálogo Instagram por scraping, base vectorial aparte o proveedor de pago sin decisión comercial/operativa. Mantenerlos como hipótesis de roadmap.
