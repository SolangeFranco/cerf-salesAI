namespace Vanta.Api.Modules.Catalog;

public sealed class ProductVariant
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProductId { get; init; }
    public required string Name { get; set; }
    public required string Sku { get; set; }
    public int StockQuantity { get; set; }
    public int ReservedStock { get; set; }
    public decimal? PriceOverride { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
