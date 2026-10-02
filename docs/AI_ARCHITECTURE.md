# Arquitectura de IA

**Estado:** 27 de septiembre de 2026. El sistema usa selección determinista de tools y providers intercambiables; no hay function-calling autónomo del LLM.

## Principios de seguridad

- El provider LLM no recibe `VantaDbContext`, SQL ni una interfaz de persistencia.
- Sólo `AgentToolExecutor` invoca el catálogo cerrado de herramientas.
- Tenant y conversación derivan del contexto autorizado de aplicación, no de argumentos del modelo.
- Stock, precio, orden y pago proceden de servicios/datos persistidos; el modelo no determina esos estados.
- Mensajes, catálogo y Knowledge son contenido no confiable y se sanea antes de formar prompts.

## Flujo

```text
WhatsApp webhook
  → normalización / tenant / conversación
  → SalesAgentService (intención y selección deterministas)
  → AgentToolExecutor (allowlist y validación de argumentos)
  → AgentTools / servicios de dominio tenant-scoped
  → resultado tipado validado
  → provider AI formula respuesta
  → mensaje outbound persistido para delivery
```

Un provider externo puede redactar respuestas con contexto validado; no decide side effects ni accede a datos directamente. El provider mock permite ejecutar tests sin red. La configuración de producción valida un provider AI externo y su key; fallback y calidad real requieren prueba operativa.

## Herramientas actuales

| Tool | Acción y límite |
|---|---|
| `search_products` | Busca productos activos del tenant |
| `get_product` | Devuelve producto tenant-scoped |
| `check_stock` | Consulta cantidades reales/reservadas |
| `get_customer` | Accede al cliente de la conversación |
| `create_cart` | Crea/recupera carrito de esa conversación |
| `add_to_cart` | Valida cantidad y stock disponible |
| `calculate_shipping` | Calculadora fija de zona; no carrier externo |
| `create_order` | Crea orden con idempotencia y ownership |
| `create_payment` | Crea intención pendiente, nunca confirma |
| `get_order` | Consulta orden de esa conversación |
| `send_message` | Encola outbound para el hilo actual |
| `handoff_to_human` | Pausa IA y pide atención humana |
| `search_knowledge` | Busca chunks Ready del tenant y sanea el contenido |

Los argumentos no redirigen tenant ni conversación. La autorización y reglas de negocio permanecen en servicios API incluso si cambia el planificador.

## Contenido no confiable

`AgentContentSanitizer` limita longitud, quita controles y neutraliza patrones conocidos de inyección en mensajes, catálogo y Knowledge. El prompt separa datos de instrucciones. Tool selection determinista limita el impacto, pero sanitización no es garantía contra todas las técnicas.

## Knowledge

PostgreSQL + pgvector almacena documentos, chunks y embeddings tenant-scoped. `IEmbeddingProvider` aísla embeddings mock/Gemini; ranking semántico depende de Npgsql/Postgres, mientras tests InMemory usan fallback keyword. Ingesta es síncrona; no hay vector DB aparte.

## No implementado

- LLM seleccionando tools por function-calling.
- Evaluación de calidad con dataset real, budgets y SLO de costo/latencia.
- Ingesta asíncrona, HNSW/IVFFlat u optimización para gran volumen.
- AI ejecutando SQL o confirmando manual-transfer.