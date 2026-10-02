namespace Vanta.Api.Modules.WhatsApp;

/// <summary>
/// Transport-level idempotency/audit record. Guards against the same webhook delivery being
/// processed twice (provider retries, at-least-once delivery), independent of the business-level
/// dedup already enforced on ConversationMessage.ExternalMessageId.
/// </summary>
public sealed class WhatsAppWebhookEvent
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Provider { get; init; }
    public required string EventId { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}
