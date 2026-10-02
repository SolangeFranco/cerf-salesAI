namespace Vanta.Api.Modules.Catalog;

public sealed class StockAlertRequest
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid ProductId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool IsActive { get; set; } = true;
}