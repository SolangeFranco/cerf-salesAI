namespace Vanta.Api.Modules.Catalog;

public sealed class ProductImage
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProductId { get; init; }
    public required byte[] Data { get; set; }
    public required string MediaType { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}