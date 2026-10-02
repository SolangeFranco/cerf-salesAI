using System.ComponentModel.DataAnnotations;

namespace Vanta.Api.Modules.Orders;

public sealed class OrderReservationOptions
{
    [Range(1, 1440)]
    public int ExpirationMinutes { get; set; } = 30;
}

public static class OrderReservationPolicy
{
    public static DateTimeOffset ExpiresAt(DateTimeOffset createdAt, OrderReservationOptions options) =>
        createdAt.AddMinutes(options.ExpirationMinutes);
}

public sealed class Order
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? CustomerId { get; set; }
    public Guid? ConversationId { get; set; }
    public required string Number { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReservationExpiresAt { get; set; }
    public List<OrderItem> Items { get; init; } = [];
    public List<OrderStatusHistory> StatusHistory { get; init; } = [];
}

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public Guid OrderId { get; init; }
    public Guid ProductId { get; init; }
    public Guid? VariantId { get; init; }
    public required string ProductName { get; init; }
    public required string Sku { get; init; }
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}
