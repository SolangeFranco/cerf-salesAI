# Desarrollo y staging — opciones

**Estado al 27 de septiembre de 2026:** no hay evidencia de entorno staging cloud provisionado. Este documento es guía comparativa, no aprobación de proveedor ni compra.

## Necesidades técnicas

- La API ASP.NET Core y su `BackgroundJobWorker` son procesos de larga duración; no diseñar el backend como función serverless efímera.
- La base requiere PostgreSQL y pgvector.
- WhatsApp/Meta requiere URL HTTPS pública y estable cuando se pruebe el provider real.
- El BFF web corre en Node.js; API y web se pueden ejecutar como contenedores separados.
- Redis no está conectado al runtime y no se requiere para arrancar.

## Desarrollo local recomendado

Desde la raíz:

```sh
docker compose -f infrastructure/docker-compose.yml up --build -d
```

Compose inicia PostgreSQL/pgvector, Redis sin uso runtime, API, web y WhatsApp bridge. AI/WhatsApp usan defaults de desarrollo/mock. URLs y comandos completos están en [README.md](../README.md). Preservar los volúmenes; `down -v` elimina datos.

## Staging: opciones por evaluar

Railway, Render u otro PaaS pueden alojar API/web Docker; Postgres administrado debe soportar pgvector y backup/restore demostrables. Comparar cada oferta en su pricing y documentación oficial vigente antes de decidir. Requisitos de long-running worker, sleep/cold start, logs, dominios, networking, cuota, backup, rollback y costo deben evaluarse con pruebas, no con la tabla de precios histórica.

Para la prueba gratuita sin tarjeta se preparó una ruta reproducible con Vercel Hobby, Supabase Free y Render Free. El Blueprint, variables, migraciones, smoke test y límites están en [DEPLOYMENT_STAGING.md](DEPLOYMENT_STAGING.md).

No se declara proveedor preferido ni deploy automático. El workflow actual de GitHub ejecuta quality gates y publica imágenes por SHA; no despliega a staging/producción. Véase [CI_CD.md](CI_CD.md).

## Separación de entornos

- No reutilizar DB ni secrets de desarrollo en staging/producción.
- Usar secretos de entorno; no crear o completar archivos `.env` con credenciales reales sin decisión explícita.
- Empezar con providers mock para pruebas funcionales locales.
- Usar Meta/AI reales sólo con sandbox o credenciales aprobadas, endpoint HTTPS, límites de gasto y plan de revocación.
- No cargar datos reales de clientes antes de las condiciones de [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md).

## Decisiones antes del provisionamiento

1. Aprobar proveedor, región, plan y límite mensual.
2. Elegir Postgres/pgvector y verificar una restauración de prueba.
3. Definir dominio, TLS, CORS y webhook.
4. Definir secrets/rotación, OTLP, dashboards y alertas.
5. Mantener staging aislado y desplegar sólo un SHA validado; probar rollback.

Las comparativas de precios de septiembre de 2026 no se copian aquí como valores actuales: consultar fuentes oficiales de cada proveedor al momento de contratar.
