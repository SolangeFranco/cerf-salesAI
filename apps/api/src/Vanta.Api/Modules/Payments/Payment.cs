namespace Vanta.Api.Modules.Payments;

public static class PaymentStatus
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Expired = "Expired";
    public const string PaidAfterExpiration = "PaidAfterExpiration";
    public const string Refunded = "Refunded";
}

public sealed class Payment
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public required string Provider { get; init; }
    public required string ProviderReference { get; init; }
    // Opaque identifier safe to expose in public checkout URLs/messages; unrelated to ProviderReference.
    public required string PublicReference { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string Status { get; set; }
    public decimal Amount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
