namespace Vanta.Api.Modules.Analytics;

// The initial, closed set of commerce lifecycle events. Anything analytics needs to reconstruct
// later (conversation -> recommendation -> cart -> order -> payment) is expressed as one of these.
public static class CommerceEventType
{
    public const string ProductViewed = "ProductViewed";
    public const string ConversationStarted = "ConversationStarted";
    public const string ProductRecommended = "ProductRecommended";
    public const string ProductAddedToCart = "ProductAddedToCart";
    public const string CartCreated = "CartCreated";
    public const string CheckoutStarted = "CheckoutStarted";
    public const string OrderCreated = "OrderCreated";
    public const string PaymentStarted = "PaymentStarted";
    public const string PaymentConfirmed = "PaymentConfirmed";
    public const string OrderCancelled = "OrderCancelled";
    public const string OrderDelivered = "OrderDelivered";
    public const string HumanHandoff = "HumanHandoff";
    public const string AbandonedCartRecovered = "AbandonedCartRecovered";

    public static readonly IReadOnlyList<string> All =
    [
        ProductViewed, ConversationStarted, ProductRecommended, ProductAddedToCart, CartCreated,
        CheckoutStarted, OrderCreated, PaymentStarted, PaymentConfirmed, OrderCancelled,
        OrderDelivered, HumanHandoff, AbandonedCartRecovered,
    ];
}

/// <summary>
/// A single point-in-time fact about a tenant's commerce activity. Every event carries the ids of
/// whichever entities were involved (conversation/customer/product/cart/order/payment); querying
/// on those ids alone reconstructs the full journey — no separate graph or join table is needed.
/// <see cref="Metadata"/> is a small JSON blob of non-sensitive fields only (counts, statuses,
/// amounts) — never customer PII (name/phone/address) or free-text message content.
/// </summary>
public sealed class CommerceEvent
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid? ConversationId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? ProductId { get; init; }
    public Guid? CartId { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? PaymentId { get; init; }
    public string? Metadata { get; init; }
}
