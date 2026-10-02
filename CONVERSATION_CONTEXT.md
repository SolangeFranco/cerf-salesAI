# VANTA — Contexto de decisiones

**Última sincronización:** 27 de septiembre de 2026.
**Propósito:** ofrecer contexto breve a herramientas de desarrollo. Este archivo no sustituye requisitos aprobados ni el código. Ante discrepancias prevalecen `AGENTS.md`, [PROJECT.md](PROJECT.md), [docs/DECISIONS.md](docs/DECISIONS.md), tests y runtime.

## Dirección de producto

VANTA busca automatizar ventas conversacionales por WhatsApp para merchants paraguayos. Es una hipótesis de producto en validación, no un negocio con PMF demostrado. El nombre es provisional; vertical y precios no están aprobados como definitivos.

## Alcance técnico presente

El repositorio contiene un modular monolith .NET 10, Next.js 16, PostgreSQL/pgvector y Docker Compose local. Incluye tenant-scoped catalog/inventory, conversaciones y handoff, providers WhatsApp, tools deterministas de IA, Knowledge, órdenes/carritos/checkout, eventos de comercio y jobs persistidos.

La selección de acciones del agente es determinista; el proveedor LLM puede formular respuestas cuando se configura, pero no recibe acceso directo a datos. Campañas, billing por suscripción, object storage y sincronización de catálogo de Meta no son capacidades productivas implementadas.

## Decisión vigente de pagos

- `mock` sólo para Development/Testing.
- `manual-transfer` es el provider disponible en producción: guarda instrucciones bancarias por tenant, mantiene el pago pendiente y requiere confirmación autenticada del merchant después de verificar el depósito.
- El cliente puede ver los datos bancarios en el checkout opaco y recibir instrucciones por WhatsApp.
- No hay procesador automático, webhook bancario, conciliación automática ni reembolso de transferencia dentro de VANTA.
- No marcar pedidos pagados por redirect ni por comprobante aportado por el cliente.

## Despliegue

No se desplegó staging ni producción según la evidencia del repositorio en este corte. Railway está en evaluación para compute; Neon y otros proveedores aparecen en propuestas, no como contratos provisionados. No crear recursos pagos ni configurar credenciales reales sin aprobación explícita.

No aceptar datos reales hasta completar restore drill, E2E deterministas, credenciales y observabilidad productivas, y aprobar el procedimiento operativo de verificación/conciliación manual. Ver [docs/DEPLOYMENT_PRODUCTION.md](docs/DEPLOYMENT_PRODUCTION.md), [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md) y [docs/PRODUCTION_READINESS.md](docs/PRODUCTION_READINESS.md).

## Reglas de continuidad

1. Trabajar en slices pequeños y probar la hipótesis con el test más cercano.
2. Preservar cambios del worktree; nunca limpiar volúmenes o editar secretos como atajo.
3. No ampliar el producto por una mención histórica del chat: confirmar roadmap y ADR vigentes.
4. Documentar decisiones nuevas, configuración operativa y evidencia con fecha.
5. La memoria conversacional no es fuente para credenciales, requisitos legales ni precios actuales.
