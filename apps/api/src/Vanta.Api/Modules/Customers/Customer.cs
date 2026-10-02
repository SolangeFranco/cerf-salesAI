namespace Vanta.Api.Modules.Customers;

public sealed class Customer
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ProfileImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
