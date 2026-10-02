# AI Revenue Attribution

## Objetivo

AI Revenue Attribution muestra únicamente ingresos respaldados por datos persistidos. No se estiman conversiones, no se asignan porcentajes por heurística y no se cuentan dos veces eventos duplicados.

La unidad contable es una orden (`OrderId`). Una orden puede contribuir una sola vez a cada métrica monetaria.

## Reglas

### AI-created order

Una orden es **AI-created** cuando se cumplen todas estas condiciones:

1. Existe una orden persistida del tenant.
2. Existe un evento `OrderCreated` persistido con el mismo `OrderId`.
3. El metadata del evento contiene `{"source":"ai_tool"}`.
4. La orden tiene un pago persistido en estado `Paid`.
5. El estado de la orden es cobrable: `Paid`, `Preparing`, `Shipped` o `Delivered`.

La creación por una herramienta de IA es una señal de autoría, no una señal de pago. Por eso la orden no entra en revenue hasta que el pago esté confirmado.

### AI-assisted conversion

Una orden es **AI-assisted** cuando:

1. Está asociada a una conversación mediante `ConversationId`.
2. No es AI-created.
3. Antes o en el instante de creación de la orden existe en esa conversación al menos un evento real de asistencia de IA: `ProductViewed`, `ProductRecommended`, `ProductAddedToCart`, `CartCreated`, `CheckoutStarted` o `HumanHandoff`.
4. Tiene pago `Paid` y estado cobrable.

`HumanHandoff` permite medir el caso en que la IA inició o enriqueció la oportunidad y una persona terminó la venta. Una orden normal sin estos eventos no es AI-assisted.

### AI-attributed revenue

**AI-attributed revenue** es la suma de `Order.Total` de la unión deduplicada de órdenes AI-created y AI-assisted que tienen pago `Paid` y no están canceladas ni reembolsadas.

No se suman pagos ni eventos individuales. Se suma una vez por `OrderId`.

### Abandoned cart recovered

Una orden es una **abandoned cart recovery** cuando:

1. Existe un evento real `AbandonedCartRecovered` con el mismo `OrderId`.
2. La orden está atribuida como AI-created o AI-assisted.
3. Tiene pago `Paid` y estado cobrable.

La métrica monetaria suma el total de esas órdenes, una sola vez por orden. La existencia del evento prueba la recuperación; no se infiere abandono mirando solamente fechas.

## Exclusiones

- `Cancelled` nunca genera revenue atribuido.
- `Refunded` nunca genera revenue atribuido, aunque originalmente haya sido pagada.
- Un pago `Pending`, `Failed`, `Expired` o `Refunded` no genera revenue.
- Una conversación sola, un `ConversationStarted` solo o una orden sin eventos de IA no genera atribución.
- Eventos duplicados no incrementan conteos ni revenue.
- Las órdenes del checkout público se registran con `source=public_store` y las órdenes de la API normal con `source=merchant_api`; ninguna es AI-created.

## Metodología reproducible

`AiRevenueAttributionService` ejecuta siempre los mismos pasos, dentro del tenant autenticado:

1. Lee órdenes, pagos y Commerce Events persistidos.
2. Conserva sólo pagos `Paid` y estados de orden cobrables.
3. Agrupa los eventos por `OrderId` y por `ConversationId`.
4. Identifica `ai_tool` desde el metadata de `OrderCreated`.
5. Para las órdenes restantes, busca eventos de asistencia anteriores o simultáneos a `Order.CreatedAt`.
6. Clasifica cada orden una sola vez como `ai_created` o `ai_assisted`.
7. Marca recuperación sólo por `AbandonedCartRecovered` real.
8. Suma `Order.Total` una sola vez por orden.

La consulta no depende de timestamps del navegador, redirecciones de checkout ni métricas calculadas en frontend.

## API y dashboard

- `GET /api/analytics/revenue-attribution` devuelve revenue total cobrado, órdenes pagadas, revenue atribuido, órdenes creadas por IA, órdenes asistidas y recuperaciones de carrito.
- `GET /api/tenants/summary` devuelve el mismo bloque de datos junto con productos, órdenes y conversaciones para el dashboard.
- Cuando no hay órdenes atribuidas, `hasAttributionData=false`; la interfaz muestra `Sin datos` y no inventa una tendencia.

## Limitaciones actuales

La atribución es determinista y de un solo touch: una orden se clasifica como AI-created o AI-assisted, nunca ambas. No pretende repartir crédito entre varios agentes, campañas o canales. Para agregar modelos multi-touch deberá añadirse una decisión explícita y tests antes de mostrar nuevas métricas.
