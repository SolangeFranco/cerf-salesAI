namespace Vanta.Api.Modules.Orders;

public sealed class OrderStatusHistory
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
