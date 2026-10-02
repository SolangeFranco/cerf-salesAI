namespace Vanta.Api.Modules.Tenants;

// Foundation entity for multi-tenancy. Business rules/authorization are added as real modules land.
public sealed class Tenant
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public string? LogoUrl { get; set; }
    public bool StoreSetupCompleted { get; set; }
    public bool AiResponsesEnabled { get; set; } = false;
    public string? BankName { get; set; }
    public string? BankBeneficiaryName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankDocumentNumber { get; set; }
    public string? BankAlias { get; set; }
    public decimal DeliveryBaseCostGs { get; set; }
    public decimal DeliveryIncludedDistanceKm { get; set; } = 10m;
    public decimal DeliveryExtraCostPerKmGs { get; set; }
    public decimal MetropolitanDeliveryCostGs { get; set; }
    public decimal InteriorDeliveryCostGs { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
