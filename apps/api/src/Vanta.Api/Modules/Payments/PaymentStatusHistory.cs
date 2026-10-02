namespace Vanta.Api.Modules.Payments;

public sealed class PaymentStatusHistory
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid PaymentId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
