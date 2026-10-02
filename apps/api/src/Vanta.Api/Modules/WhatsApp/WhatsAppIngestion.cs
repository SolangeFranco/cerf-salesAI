namespace Vanta.Api.Modules.WhatsApp;

// Framework-agnostic view of an inbound webhook call: raw body plus headers, so provider
// adapters can validate signatures without depending on ASP.NET request internals.
public sealed record WhatsAppWebhookSignal(string RawBody, IReadOnlyDictionary<string, string> Headers);

// Canonical, provider-agnostic events the ingestion pipeline understands. VANTA's domain only
// ever sees these two shapes, never a Meta/Twilio/other-specific payload.
public abstract record WhatsAppInboundEvent(string EventId, string ChannelExternalId);

public sealed record WhatsAppInboundMessageEvent(
    string EventId,
    string ChannelExternalId,
    string CustomerExternalId,
    string MessageExternalId,
    string Text,
    string? MediaId,
    string? MediaType,
    string? InlineImageBase64 = null,
    string? InlineImageMediaType = null,
    string? InlineMediaBase64 = null,
    string? InlineMediaMimeType = null,
    string? InlineMediaFileName = null,
    string? InlineMediaKind = null,
    bool MediaUnavailable = false,
    string? QuotedMessageExternalId = null,
    string? QuotedText = null,
    string? CustomerName = null,
    string? CustomerImageUrl = null,
    DateTimeOffset? MessageCreatedAt = null,
    bool IsGroup = false,
    bool IsChannel = false,
    bool IsFromMe = false,
    bool IsHistorical = false) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed record WhatsAppDeliveryStatusEvent(
    string EventId,
    string ChannelExternalId,
    string MessageExternalId,
    string Status,
    string? Error) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed record WhatsAppContactUpdateEvent(
    string EventId,
    string ChannelExternalId,
    string CustomerExternalId,
    string CustomerName,
    string? CustomerImageUrl = null) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed record WhatsAppChatUpdateEvent(
    string EventId,
    string ChannelExternalId,
    string CustomerExternalId,
    string? CustomerName,
    string? CustomerImageUrl,
    DateTimeOffset? LastActivityAt,
    bool IsGroup,
    bool IsChannel) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed record WhatsAppReactionEvent(
    string EventId,
    string ChannelExternalId,
    string MessageExternalId,
    string Emoji,
    string SenderExternalId) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed record WhatsAppIdentityMappingEvent(
    string EventId,
    string ChannelExternalId,
    string SourceExternalId,
    string CustomerExternalId,
    string? CustomerName,
    string? CustomerImageUrl) : WhatsAppInboundEvent(EventId, ChannelExternalId);

public sealed class WhatsAppMalformedPayloadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// Isolates VANTA's domain from any specific WhatsApp processor (Meta Cloud API, Twilio, ...).
/// Ingestion responsibilities only: signature validation and normalization into canonical events.
/// Outbound delivery is a separate concern owned by <see cref="IWhatsAppMessageSender"/>.
/// </summary>
public interface IWhatsAppProvider
{
    string Name { get; }

    // Validates the signature/authenticity of an inbound webhook call. Must not throw.
    bool ValidateWebhook(WhatsAppWebhookSignal signal);

    // Parses and normalizes a validated payload. Returns an empty list for a recognized-but-
    // unsupported event (e.g. an unsupported message type); throws WhatsAppMalformedPayloadException
    // for a structurally invalid payload (e.g. not valid JSON, missing required fields).
    IReadOnlyList<WhatsAppInboundEvent> ParseWebhook(WhatsAppWebhookSignal signal);
}
