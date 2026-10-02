# AGENTS.md — VANTA Coding Agent Instructions

## Role

You are the implementation agent for VANTA.

The founder is Roman Veron. ChatGPT is the product/architecture copilot. You are responsible for turning approved product decisions into production-quality code.

Do not invent major business requirements.

## Source of Truth

Read these files before making substantial changes:

1. `PROJECT.md`
2. `docs/DECISIONS.md`
3. Relevant domain document under `docs/`
4. Existing code and tests

If code and documentation disagree:
- Do not silently change business intent.
- Prefer the latest explicit decision.
- Update `docs/DECISIONS.md` when a meaningful architecture/product decision changes.

## Operating Principles

### 1. Validation first

Do not build large speculative features.

The product is intentionally being validated with real Paraguayan merchants.

Prefer:
- thin vertical slices
- working demos
- real integrations behind adapters
- mocks when credentials are unavailable
- measurable outcomes

### 2. Modular monolith first

Use a modular monolith.

Do not introduce microservices unless there is a documented operational reason.

### 3. Security by default

Never:
- hard-code secrets
- commit credentials
- expose API keys
- trust client-side payment status
- skip webhook signature validation
- bypass tenant authorization
- execute arbitrary AI-generated SQL
- expose another tenant's data

### 4. AI must use controlled tools

The AI agent must interact with business data through explicit application tools.

Never give the LLM unrestricted database access.

Every tool should:
- validate input
- enforce tenant scope
- enforce authorization/business rules
- return the minimum required information
- produce auditable effects where applicable

### 5. Payments require correctness

Payment code must be:
- idempotent
- webhook-driven
- auditable
- state-machine based
- provider-independent

Never mark an order as paid because the browser redirected to a success URL.

### 6. WhatsApp must be provider-isolated

Keep provider-specific code behind interfaces/adapters.

Webhook processing must be:
- idempotent
- validated
- retry-safe
- observable

### 7. Multi-tenancy is mandatory

Every tenant-owned operation must be tenant-scoped.

Add tests that prove:
- tenant A cannot read tenant B
- tenant A cannot modify tenant B
- background jobs preserve tenant context

### 8. Quality

Backend:
- .NET 10 / C#
- ASP.NET Core
- EF Core
- PostgreSQL
- Clean, explicit domain boundaries
- Async I/O
- CancellationToken where appropriate
- Structured logging
- OpenTelemetry
- Validation
- ProblemDetails
- OpenAPI

Frontend:
- Next.js
- TypeScript
- Tailwind
- shadcn/ui
- TanStack Query
- React Hook Form
- Zod

### 9. Avoid overengineering

Do not create:
- generic repositories everywhere
- abstractions with only one implementation without a real reason
- speculative event buses
- unnecessary CQRS machinery
- microservices
- premature distributed infrastructure

Use abstractions when they isolate a volatile boundary such as:
- payments
- WhatsApp providers
- AI providers
- shipping providers

### 10. Testing

Use:
- unit tests for domain/business logic
- integration tests for DB/API boundaries
- end-to-end tests for critical user flows

Critical flows:
- merchant onboarding
- product creation
- stock check
- AI product recommendation
- cart creation
- order creation
- payment confirmation
- WhatsApp webhook
- tenant isolation

### 11. Database

Use PostgreSQL.

Prefer:
- explicit migrations
- indexes based on actual query patterns
- foreign keys
- constraints
- unique constraints
- timestamps
- audit fields where appropriate

Use pgvector initially for RAG unless a separate vector database becomes justified.

### 12. API

Design stable REST APIs.

Requirements:
- consistent resource naming
- validation
- pagination
- filtering
- sorting
- idempotency where needed
- ProblemDetails errors
- OpenAPI documentation

Do not leak internal domain models blindly through public APIs.

### 13. UX

The product should feel premium.

Avoid:
- clutter
- excessive cards
- confusing jargon
- unnecessary modals
- inconsistent spacing
- low-quality empty states
- fake metrics

Use:
- clear hierarchy
- responsive layouts
- accessible components
- meaningful loading states
- useful empty states
- subtle animation only where it improves comprehension

### 14. AI-generated revenue

Never fabricate this KPI.

Revenue attribution must have a defined data model and attribution rule.

If attribution is not implemented, do not display a misleading number.

### 15. Observability

Important operations should be observable.

At minimum:
- structured logs
- request traces
- errors
- latency
- webhook processing
- payment events
- AI tool calls
- background jobs

Do not log:
- access tokens
- API keys
- passwords
- payment secrets
- unnecessary personal data

### 16. Background jobs

Use reliable background processing when needed.

Jobs must be:
- retryable
- idempotent
- observable
- tenant-aware

Examples:
- abandoned-cart follow-up
- post-delivery follow-up
- campaign sending
- document embedding
- analytics aggregation

### 17. Documentation

When adding a significant feature:
- update relevant docs
- add API documentation
- add tests
- record meaningful architectural decisions

## What requires human approval

Stop and ask for confirmation before:
- production payment credentials
- production WhatsApp credentials
- paid cloud resources with meaningful cost
- destructive database migrations
- public production deployment
- legal/compliance commitments
- irreversible vendor lock-in
- major pricing changes
- changing core product positioning
- deleting significant data

For reversible technical decisions, proceed using the documented architecture.

## Delivery workflow

For each feature:

1. Read requirements.
2. Inspect existing code.
3. Identify affected module(s).
4. Make the smallest coherent implementation.
5. Add/update tests.
6. Run formatting/lint/type checks.
7. Run relevant tests.
8. Update docs/decisions.
9. Summarize:
   - what changed
   - tests run
   - assumptions
   - remaining risks

## First implementation priority

Do NOT immediately build the whole roadmap.

First create the foundation for a thin vertical slice:

`Merchant → Product → WhatsApp conversation → AI product lookup → Cart/Order → Dashboard`

Use mocks/adapters where real provider credentials are unavailable.

The first objective is a credible end-to-end demo that can be shown to a real merchant.
