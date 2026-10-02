namespace Vanta.Api.Modules.Orders;

public sealed class Cart
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConversationId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<CartItem> Items { get; init; } = [];
}

public sealed class CartItem
{
    public Guid Id { get; init; }
    public Guid CartId { get; init; }
    public Guid ProductId { get; init; }
    public Guid? VariantId { get; init; }
    public required string ProductName { get; init; }
    public required string Sku { get; init; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; init; }
}
