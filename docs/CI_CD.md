# CI/CD — VANTA

## Containers

### Backend (`apps/api/src/Vanta.Api/Dockerfile`)

Multi-stage build:

1. **build** — `mcr.microsoft.com/dotnet/sdk:10.0-alpine`. Restores the csproj alone first (cached layer), then copies source and runs `dotnet publish`.
2. **final** — `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`. Only the published output is copied in; the SDK never ships.

Applied objectives:

- **Runtime-only final stage**: Alpine base; image size varies by build and must be measured from the published artifact when relevant.
- **Reproducible**: restore/build steps depend only on the csproj and source copied into the image, no host-machine state leaks in.
- **Non-root**: a dedicated `vanta` user/group (fixed UID/GID `5678`) is created explicitly rather than relying on undocumented base-image defaults; `USER vanta` is set before the entrypoint runs.
- **Health check**: `HEALTHCHECK` calls `GET /health/live` (liveness only — does not depend on Postgres, matching `docs/OBSERVABILITY.md`).
- **No secrets in the image**: no `appsettings.Development.json`, `.env`, or credentials are copied in (see `.dockerignore`). All configuration is read from environment variables at runtime (`ConnectionStrings__Default`, `Jwt__SigningKey`, `WhatsApp__*`, `Ai__*`).

### Frontend (`apps/web/Dockerfile`)

Multi-stage build: `deps` (npm ci) → `build` (`next build`, with `output: "standalone"` set in `next.config.ts`) → `final` (`node:20-alpine`, runtime only).

- **Standalone runtime**: Next.js bundles the production `node_modules` subset required at runtime; image size varies by build and must be measured from the published artifact when relevant.
- **Reproducible**: `npm ci` against the committed `package-lock.json`, never `npm install`.
- **Non-root**: same fixed-UID `vanta` user pattern as the API image.
- **Health check**: `HEALTHCHECK` requests `/login` (a public, unauthenticated page) over `wget`.
- **No secrets in the image**: the only runtime configuration is `API_BASE_URL`, pointing at the API service; the JWT itself lives only in an httpOnly cookie set at request time, never baked into the image.

### `.dockerignore`

One root-level `.dockerignore` covers both build contexts (`bin/`, `obj/`, `node_modules/`, `.next/`, `.git/`, test artifacts, `.env*` except `.env.example`, `appsettings.Development.json`). This is what actually guarantees "no secrets inside images" — the Docker daemon never even receives those files as build context.

## docker-compose (development)

`infrastructure/docker-compose.yml` runs the local stack: `postgres` (pgvector), `redis`, `api`, `web` and the optional `whatsapp-bridge` for linked-device sessions.

```powershell
docker compose -f infrastructure/docker-compose.yml up --build
```

- API: `http://localhost:5052` — applies EF Core migrations on startup (`ASPNETCORE_ENVIRONMENT=Development` inside the container only). A merchant account is created through registration; no fixed demo password or sample business data is seeded.
- Web: `http://localhost:3000`.
- WhatsApp/AI providers default to `mock` — no external credentials needed to run this.
- To use real WhatsApp/AI credentials locally, copy `infrastructure/.env.example` to `infrastructure/.env` and fill in values; `infrastructure/.env` is gitignored and is picked up automatically by `docker compose` (default `.env` lookup is relative to the compose file's directory).
- `depends_on: condition: service_healthy` gates `api` on Postgres and `web` on the API, so `docker compose up` produces a working stack without manual ordering.

Redis is provisioned but **not yet consumed by the application** — there is no Redis-dependent code today (verified: no `StackExchange.Redis` or similar reference). It stays in `docker-compose.yml` because `docs/ARCHITECTURE.md` already reserves it, so the container is available the moment a real need appears, without a docker-compose change.

## CI pipeline

### Pull Request (`.github/workflows/pr.yml`)

Sequential gate on every PR into `main`, in the order requested:

```text
lint (web) → typecheck (web) → build (web + api + docker images) → unit tests → integration tests
```

- **lint / typecheck**: `apps/web` only (`eslint`, `tsc --noEmit`). The backend doesn't have a separate lint step yet — `dotnet build` surfaces analyzer warnings as part of the `build` stage.
- **build**: `next build`, `dotnet build --configuration Release`, and a build (not push) of API, web and WhatsApp bridge Dockerfiles — this catches a broken image before it reaches `main`.
- **unit tests**: the full xUnit suite (`tests/unit/Vanta.Api.UnitTests.csproj`) **except** the small set of tests that require a real Postgres instance (`*ConcurrencyTests` and `KnowledgeSemanticSearchTests` — these use `UseNpgsql` directly against a hardcoded local connection string; see `docs/DECISIONS.md`/test file headers for why). Runs against the in-memory `VantaWebApplicationFactory`, no services needed — fast.
- **integration tests**: only those Postgres-dependent tests, against a real `postgres:pgvector` service container spun up by the job. Migrations are applied first via `dotnet ef database update` so the schema matches what the tests expect.

### Main (`.github/workflows/main.yml`)

```text
push to main → build + full API tests → push SHA-tagged images to GHCR
```

- Re-runs `npm run typecheck`, `npm run build`, `dotnet build` and the complete API test suite against a PostgreSQL service container on `main` before any image is pushed (defense in depth — a PR could theoretically be merged without the exact same commit having been built).
- Builds and pushes API, web and WhatsApp bridge images to GitHub Container Registry only after the gate passes. API y bridge se publican para `linux/amd64` y `linux/arm64`; cada imagen recibe un tag inmutable por commit SHA y un tag flotante `staging`.
- The workflow writes the validated commit SHA and the exact published image references to the GitHub Actions step summary, and checks that both SHA tags equal `github.sha`. The SHA-tagged images are the release artifacts for that run.
- Render sincroniza `render.yaml` y despliega staging únicamente después de que los checks de GitHub pasan (`autoDeployTrigger: checksPass`). Vercel despliega el frontend desde `main`. No existe despliegue automático a producción; véase [DEPLOYMENT_STAGING.md](DEPLOYMENT_STAGING.md).

### Required repository configuration

| Secret / Environment | Purpose |
|---|---|
| `GITHUB_TOKEN` | Already provided automatically by GitHub Actions; used to push images to GHCR. No PAT needed. |

No manually provisioned secret is required for this CI slice. The app's runtime secrets (`WhatsApp__*`, `Ai__*`, `Jwt__SigningKey`, `ConnectionStrings__Default`) are not used by the validation jobs and must remain in the eventual runtime environment, never in CI or image build arguments.

### Production database backup (`.github/workflows/database-backup.yml`)

The scheduled/manual backup workflow does not deploy the application. It creates a PostgreSQL custom-format dump, encrypts it with a passphrase held in the protected `production` environment, and uploads it through an AWS OIDC role to a private, versioned S3-compatible bucket. Required configuration:

- `PRODUCTION_DATABASE_URL` secret;
- `PRODUCTION_BACKUP_PASSPHRASE` secret;
- `PRODUCTION_BACKUP_AWS_ROLE_ARN` secret and `PRODUCTION_BACKUP_AWS_REGION` variable;
- `PRODUCTION_BACKUP_BUCKET_URI` secret pointing to a private bucket with lifecycle retention.

The restore drill is intentionally manual and must target an isolated database. Set `VANTA_RESTORE_DATABASE_URL` locally or in a protected operations environment and run `scripts/restore-postgres.ps1 -BackupFile <file> -WhatIf` first, then without `-WhatIf` after approval. Validate migrations, tenant counts, health checks and payment/webhook invariants after restore. The backup workflow must be exercised successfully before production data is accepted.
