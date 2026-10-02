namespace Vanta.Api.Modules.WhatsApp;

public sealed class WhatsAppChannelAccount
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Provider { get; init; }
    public required string ExternalAccountId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
}

public sealed class Conversation
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ChannelAccountId { get; init; }
    public Guid? CustomerId { get; set; }
    public required string CustomerExternalId { get; set; }
    public string? DisplayName { get; set; }
    public bool IsGroup { get; set; }
    public bool IsChannel { get; set; }
    // One of ConversationStatus.AiActive / HumanActive / Paused / Closed — who is currently in control.
    public required string Status { get; set; }
    public string? CurrentIntent { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
    // Any conversation-affecting event (message, assignment, pause/resume, close), not just messages.
    public DateTimeOffset LastActivityAt { get; set; }
    // Guards against the AI producing two responses to the same inbound message under concurrent triggers.
    public Guid? LastRespondedToMessageId { get; set; }
}


public sealed class ConversationMessage
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ConversationId { get; init; }
    public required string ExternalMessageId { get; set; }
    public required string Direction { get; init; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public byte[]? ImageData { get; set; }
    public string? ImageMediaType { get; set; }
    public byte[]? MediaData { get; set; }
    public string? MediaMimeType { get; set; }
    public string? MediaFileName { get; set; }
    public string? MediaKind { get; set; }
    public bool MediaUnavailable { get; set; }
    public string? AudioTranscript { get; set; }
    public string? DeliveryStatus { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset? NextDeliveryAttemptAt { get; set; }
    public string? DeliveryError { get; set; }
    public string? ReactionsJson { get; set; }
    public string? QuotedExternalMessageId { get; set; }
    public string? QuotedText { get; set; }
    public bool IsDeleted { get; set; }
}
