namespace Vanta.Api.Modules.Catalog;

public sealed class ProductCategory
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}