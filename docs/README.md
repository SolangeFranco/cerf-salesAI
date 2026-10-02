# Documentación VANTA

**Última sincronización con el repositorio:** 27 de septiembre de 2026. Este índice distingue documentación normativa, estado comprobado e hipótesis. El código y los tests definen el comportamiento implementado; una propuesta cloud o una decisión histórica no significa que el servicio esté desplegado.

## Fuentes canónicas

| Tema | Documento | Autoridad |
|---|---|---|
| Producto, límites y objetivo | [PRODUCT.md](PRODUCT.md), [PROJECT.md](../PROJECT.md) | Intención de producto y alcance de validación |
| Arquitectura y contratos API | [ARCHITECTURE.md](ARCHITECTURE.md), [API.md](API.md) | Diseño actual; comprobar cambios de runtime contra el código |
| Decisiones técnicas | [DECISIONS.md](DECISIONS.md) | Registro ADR; decisiones históricas no equivalen a capacidades actuales |
| IA y atribución | [AI_ARCHITECTURE.md](AI_ARCHITECTURE.md), [AI_REVENUE_ATTRIBUTION.md](AI_REVENUE_ATTRIBUTION.md) | Límites de tools, seguridad y reglas de métricas |
| Jobs y observabilidad | [BACKGROUND_JOBS.md](BACKGROUND_JOBS.md), [OBSERVABILITY.md](OBSERVABILITY.md) | Implementación y brechas operativas |
| Seguridad | [SECURITY.md](SECURITY.md) | Invariantes de seguridad; el checklist determina su verificación operativa |
| Release/readiness | [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md), [PRODUCTION_READINESS.md](PRODUCTION_READINESS.md) | Estado y gates de salida; no implica autorización para desplegar |
| Handoff técnico | [VANTA_TECHNICAL_HANDOFF.md](VANTA_TECHNICAL_HANDOFF.md) | Snapshot técnico fechado y límites conocidos |
| CI/CD | [CI_CD.md](CI_CD.md) | Workflows reales; hoy compilan/publican artefactos, no despliegan producción |
| Despliegue | [DEPLOYMENT_DEVELOPMENT.md](DEPLOYMENT_DEVELOPMENT.md), [DEPLOYMENT_PRODUCTION.md](DEPLOYMENT_PRODUCTION.md) | Propuestas no implementadas; verificar precios antes de contratar |
| Roadmap | [ROADMAP.md](ROADMAP.md) | Prioridades futuras, no inventario de rutas/features |

## Documentos de negocio

- [BUSINESS.md](BUSINESS.md): modelo e hipótesis de ingresos.
- [MARKET.md](MARKET.md): snapshot de investigación de mercado, con datos por revalidar.
- [COMPETITORS.md](COMPETITORS.md): tesis competitiva preliminar.
- [GTM.md](GTM.md): estrategia de validación founder-led.
- [PRICING.md](PRICING.md): opciones de precio aún no aprobadas.

## Auditoría e historial

- [ENGINEERING_AUDIT.md](ENGINEERING_AUDIT.md): auditoría histórica; no usar como estado actual.
- [CONVERSATION_CONTEXT.md](../CONVERSATION_CONTEXT.md): decisiones resumidas; los documentos canónicos anteriores prevalecen sobre el historial conversacional.

## Convenciones

- Cada afirmación de operación debe indicar fecha o evidencia reproducible.
- `Implemented` describe código; `validated` exige un test/check ejecutado; `deployed` requiere evidencia del entorno real.
- No registrar secretos, identificadores privados ni datos de clientes.
- Hipótesis de mercado, precios y cloud deben llevar fecha y no tratarse como decisiones aprobadas.
- Los archivos de agentes, skills y prompts bajo `.github/`, además de `AGENTS.md` y `CLAUDE.md`, son configuración de trabajo; no son documentos de estado del producto.