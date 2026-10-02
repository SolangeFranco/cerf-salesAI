# Despliegue de producción — propuesta

**Estado al 27 de septiembre de 2026:** no implementado; no se crearon recursos cloud y no se ejecutaron despliegues. Este documento presenta una arquitectura candidata, no una decisión de compra ni autorización de despliegue.

## 1. Estado actual del runtime

- API .NET 10 y web Next.js 16 se empaquetan en Docker.
- PostgreSQL 16 + pgvector es la base transaccional; `background_jobs` y worker viven con la API.
- Redis está en Compose local pero no lo consume el runtime.
- Object storage no está implementado; archivos/imágenes persisten en PostgreSQL.
- Producción sólo registra `manual-transfer`; no hay procesador automático.
- OTLP puede configurarse en API, pero no hay backend/dashboards/alertas provisionados.
- Scripts/workflow de backup existen, pero no hay restore drill con recursos productivos.

## 2. Requisitos antes de elegir plataforma

La plataforma debe ejecutar procesos .NET y Node de larga duración, PostgreSQL compatible con pgvector, webhooks HTTPS estables, jobs con proceso siempre activo, secretos protegidos, backups restaurables, logs/metrics/traces y rollback. Requisitos de región, dominio, presupuesto y RPO/RTO requieren aprobación del owner.

## 3. Opciones de arquitectura

### Opción PaaS con Postgres administrado

Dos servicios de aplicación (API y web) en proveedor de contenedores, PostgreSQL administrado separado, TLS/domain, secret manager, storage privado si se migra imágenes, y backend OTel. Railway, Render y alternativas equivalentes requieren comparación vigente de costo, backups, SLA, límites y comportamiento de procesos permanentes. Ninguna opción está seleccionada por este documento.

### Opción cloud administrada

AWS ECS/Fargate + RDS/S3/CloudWatch u Azure Container Apps + PostgreSQL Flexible/Blob/Monitor. Dan control/red/servicios gestionados adicionales a cambio de más configuración y costo. No se justifica Kubernetes/microservicios para el volumen aún no medido.

### Datos

PostgreSQL debe soportar pgvector y TLS. Una copia administrada/PITR no basta: mantener export cifrado independiente, políticas de retención/acceso, alarmas y restores periódicos en una base aislada. El proveedor elegido y su plan deben demostrar los objetivos de recuperación acordados.

## 4. Seguridad y operación requeridas

- Dominio propio, TLS, CORS explícito y webhook público estable.
- Secrets por entorno: signing key, connection string, Meta, AI, bridge y backup; nunca en imágenes o Git.
- `Payment:Provider=manual-transfer` para la implementación actual. Mock no debe registrarse en producción.
- Confirmación bancaria según procedimiento aprobado; no confiar en recibos ni redirects.
- OpenTelemetry exportado a backend provisionado; dashboard/alertas para API, DB, jobs, webhooks, pagos y costos.
- Migración única por release, estrategia expand/contract, imágenes inmutables por SHA y rollback probado.
- Backups/restore comprobados antes de datos reales.

## 5. Release sugerido (no ejecutado)

1. Aprobar proveedor, región, dominio y presupuesto.
2. Provisionar entorno aislado con base y secretos; probar migraciones y health.
3. Conectar WhatsApp/AI de prueba y probar checkout manual de punta a punta.
4. Completar E2E y restore drill; comprobar alertas y rollback.
5. Aprobar procedimiento de operación manual de pagos y privacidad.
6. Obtener autorización explícita para producción; desplegar una imagen SHA aprobada y observar el smoke test.

CI actualmente valida/build y publica imágenes SHA; no hace deploy automático, como documenta [CI_CD.md](CI_CD.md).

## 6. Costos y referencias

No hay estimación vigente confiable. Las tablas de precios de proveedores cambian con región, plan, uso, egress, retención, HA y moneda. Antes de contratar, consultar la página oficial actual y calcular workload con límites de gasto. No inferir que Railway, Render, Neon, Cloudflare, AWS o Azure estén aprobados o provisionados.

## 7. Pendientes de decisión

- proveedor y región de compute/database;
- dominio y DNS/TLS;
- presupuesto mensual y alarmas;
- RPO/RTO y retención;
- backend OTel/error tracking;
- object storage y migración de blobs;
- operación manual de pago y proceso de refund externo;
- aprobación de producción.

Hasta resolverlos, la recomendación es mantener el entorno local y no aceptar datos reales.