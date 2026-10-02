# VANTA

VANTA es una plataforma SaaS en validación para convertir conversaciones comerciales de WhatsApp en consultas de catálogo, carritos y pedidos. Su nombre y posicionamiento siguen siendo de trabajo.

## Estado del proyecto

**Corte documental:** 27 de septiembre de 2026. El producto tiene un vertical slice amplio y ejecutable localmente, pero **no está listo para producción y no se ha desplegado**.

El checkout admite transferencia bancaria manual: el comercio configura sus datos, el cliente recibe instrucciones por WhatsApp y el pago sólo cambia a `Paid` cuando un usuario autenticado del tenant confirma el depósito. No hay procesador automático, conciliación bancaria ni reembolso de transferencias dentro de VANTA. El provider `mock` es para Development/Testing.

Persisten gates de lanzamiento: restore drill de backup, aceptación con proveedores reales, credenciales externas, observabilidad y aprobación del procedimiento operativo de pagos. Los E2E locales aislados están verdes. El estado y la evidencia se mantienen en [docs/PRODUCTION_READINESS.md](docs/PRODUCTION_READINESS.md) y [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md).

La ruta de staging gratuito sin tarjeta preparada para Vercel + Supabase + Render está documentada paso a paso en [docs/DEPLOYMENT_STAGING.md](docs/DEPLOYMENT_STAGING.md).

## Inicio rápido

Requisitos: Docker y Docker Compose. Desde la raíz del repositorio:

```sh
docker compose -f infrastructure/docker-compose.yml up --build -d
```

URLs locales:

- Web: <http://localhost:3000>
- API: <http://localhost:5052>
- Readiness de API: <http://localhost:5052/health/ready>
- Bridge WhatsApp vinculado: <http://localhost:5080>

Compose inicia PostgreSQL con pgvector, Redis (reservado, no consumido actualmente), API, web y bridge. Los providers locales de AI y WhatsApp usan defaults de desarrollo/mock; las credenciales reales no son necesarias para iniciar. No ejecutes `docker compose down -v` salvo que quieras borrar intencionalmente los volúmenes locales.

## Verificación

```sh
npm --prefix apps/web run lint
npm --prefix apps/web run typecheck
npm --prefix apps/web run build
dotnet build apps/api/Vanta.slnx --configuration Release
dotnet test tests/unit/Vanta.Api.UnitTests.csproj --configuration Release
```

Los tests que requieren PostgreSQL real necesitan el servicio pgvector y migraciones aplicadas. E2E se ejecuta desde `apps/web` con `npm run test:e2e`; consulta [docs/CI_CD.md](docs/CI_CD.md) antes de interpretar un resultado aislado.

## Mapa del repositorio

- `apps/api`: API ASP.NET Core .NET 10, modular monolith.
- `apps/web`: Next.js App Router, dashboard, storefront, checkout y BFF.
- `apps/whatsapp-bridge`: adapter opcional para sesiones WhatsApp vinculadas.
- `tests/unit`: pruebas de dominio y API in-process; algunas clases conectan a PostgreSQL.
- `infrastructure`: Docker Compose local.
- `docs`: especificación, decisiones, operación y estado de preparación.

Empieza por [docs/README.md](docs/README.md), [PROJECT.md](PROJECT.md) y [AGENTS.md](AGENTS.md). El historial conversacional no es fuente de requisitos vigente; [CONVERSATION_CONTEXT.md](CONVERSATION_CONTEXT.md) resume decisiones y enlaza a los documentos canónicos.

