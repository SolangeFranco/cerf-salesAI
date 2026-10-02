namespace Vanta.Api.Modules.Payments;

public sealed class PaymentWebhookEvent
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Provider { get; init; }
    public required string EventId { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}
