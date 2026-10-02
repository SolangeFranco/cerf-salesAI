namespace Vanta.Api.Modules.WhatsApp;

/// <summary>
/// Audit trail for everything that changes a conversation's operational state: AI/human
/// handoff, pause/resume, assignment and closing. Mirrors OrderStatusHistory/PaymentStatusHistory.
/// </summary>
public sealed class ConversationActivity
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConversationId { get; init; }
    public required string Type { get; init; }
    public string? FromStatus { get; init; }
    public string? ToStatus { get; init; }
    // Null means the system/AI performed the action (e.g. auto-reopen on a new inbound message).
    public Guid? ActorUserId { get; init; }
    public Guid? AssignedUserId { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
