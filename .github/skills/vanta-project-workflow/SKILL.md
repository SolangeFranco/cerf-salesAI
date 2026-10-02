---
name: vanta-project-workflow
description: 'Install, run, validate, audit, or extend the VANTA .NET 10 and Next.js project. Use for bootstrap, dependencies, Docker Compose, migrations, tests, E2E, module implementation, and production-readiness checks.'
argument-hint: 'Describe the setup, verification, or module task'
user-invocable: true
disable-model-invocation: false
---

# VANTA Project Workflow

## Sources of truth

Read these in order before substantial work:

1. `PROJECT.md`
2. `AGENTS.md`
3. `docs/DECISIONS.md`
4. The relevant document under `docs/`
5. Existing code and neighboring tests

The product target is a thin, credible merchant demo. Do not expand the whole roadmap speculatively.

## Local bootstrap

Required: Docker, Docker Compose, .NET SDK compatible with `global.json`, Node.js 20+, and npm.

```bash
dotnet restore apps/api/Vanta.slnx
npm ci --prefix apps/web
docker compose -f infrastructure/docker-compose.yml up --build -d
```

The default Compose configuration uses mock WhatsApp and AI providers and needs no external credentials. Do not create `infrastructure/.env` unless the user intentionally opts into a real provider.

Expected endpoints:

- Web: `http://localhost:3000`
- API: `http://localhost:5052`
- API readiness: `http://localhost:5052/health/ready`

For host-based development, start only `postgres` and `redis`, apply migrations from the API project, then run API and web in separate long-running terminals.

## Verification ladder

Use the narrowest applicable command first.

```bash
npm --prefix apps/web run lint
npm --prefix apps/web run typecheck
npm --prefix apps/web run build
dotnet build apps/api/Vanta.slnx --configuration Release
dotnet test tests/unit/Vanta.Api.UnitTests.csproj --configuration Release --filter "FullyQualifiedName!~ConcurrencyTests&FullyQualifiedName!~KnowledgeSemanticSearchTests"
```

Real-Postgres checks require a healthy pgvector database and applied migrations:

```bash
dotnet ef database update --project apps/api/src/Vanta.Api/Vanta.Api.csproj --startup-project apps/api/src/Vanta.Api/Vanta.Api.csproj
dotnet test tests/unit/Vanta.Api.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ConcurrencyTests|FullyQualifiedName~KnowledgeSemanticSearchTests"
```

For E2E, install only the required browser and run against a healthy stack:

```bash
npm --prefix apps/web exec playwright install chromium
npm --prefix apps/web run test:e2e
```

## MCP and external providers

No MCP server is required to build or run VANTA. MCP extends the coding agent; it must not replace application runtime adapters.

Runtime integrations remain behind the existing interfaces:

- Meta WhatsApp Cloud API through the WhatsApp adapters.
- Gemini, Groq, or OpenRouter through the AI provider abstraction.
- Gemini embeddings through the embedding provider abstraction.
- Future payment processors through `IPaymentProvider`.

Treat real credentials as optional local configuration and never route them through chat, source files, logs, tests, or MCP configuration. Add an MCP server only for a separately approved developer workflow with a concrete need and least-privilege access.

## Module priority

Before choosing work, consult `docs/ROADMAP.md`, `docs/VANTA_TECHNICAL_HANDOFF.md`, and `docs/PRODUCTION_READINESS.md`. Current high-value gaps include a real payment provider, proven backup/restore, deterministic E2E, object storage, role authorization, asynchronous knowledge ingestion, and later campaigns/abandoned-cart automation. Provider choice, paid infrastructure, and production credentials require human approval.

## Completion contract

Every implementation reports:

- behavior changed;
- focused and broader checks run;
- tenant/security implications;
- assumptions;
- remaining risks or blocked approvals.
