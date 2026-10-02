namespace Vanta.Api.Modules.Catalog;

public sealed class Product
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    public string AvailableSizes { get; set; } = "[]";
    public string Gender { get; set; } = "Unisex";
    public ICollection<ProductImage> Images { get; } = new List<ProductImage>();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string ProductNumber { get; set; }
    public required string Sku { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public bool IsActive { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageMediaType { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
