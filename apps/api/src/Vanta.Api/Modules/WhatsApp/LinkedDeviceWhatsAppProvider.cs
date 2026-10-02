using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

public sealed class LinkedDeviceWhatsAppProvider(IOptions<WhatsAppProviderOptions> options) : IWhatsAppProvider
{
    public string Name => "linked-device";

    public bool ValidateWebhook(WhatsAppWebhookSignal signal)
    {
        var expected = options.Value.LinkedDeviceWebhookSecret;
        if (!string.IsNullOrWhiteSpace(expected) &&
            signal.Headers.TryGetValue("X-Linked-Device-Signature", out var actual) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual.Trim()), Encoding.UTF8.GetBytes(expected.Trim())))
            return true;

        var bridgeKey = options.Value.LinkedDeviceBridgeKey;
        if (!string.IsNullOrWhiteSpace(bridgeKey) &&
            signal.Headers.TryGetValue("X-Bridge-Key", out var providedBridgeKey) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedBridgeKey.Trim()), Encoding.UTF8.GetBytes(bridgeKey.Trim())))
            return true;

        return !string.IsNullOrWhiteSpace(bridgeKey) &&
            signal.Headers.TryGetValue("Authorization", out var authorization) &&
            authorization.StartsWith("Bridge ", StringComparison.OrdinalIgnoreCase) &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(authorization["Bridge ".Length..].Trim()), Encoding.UTF8.GetBytes(bridgeKey.Trim()));
    }

    public IReadOnlyList<WhatsAppInboundEvent> ParseWebhook(WhatsAppWebhookSignal signal)
    {
        try
        {
            using var document = JsonDocument.Parse(signal.RawBody);
            var root = document.RootElement;
            var eventType = ReadRequiredString(root, "type");
            var sessionId = ReadRequiredString(root, "sessionId");
            var eventId = ReadRequiredString(root, "eventId");

            if (eventType == "message")
            {
                return [new WhatsAppInboundMessageEvent(
                    eventId,
                    $"linked:{sessionId}",
                    ReadRequiredString(root, "from"),
                    ReadRequiredString(root, "messageId"),
                    ReadString(root, "text") ?? string.Empty,
                    null,
                    null,
                    InlineMediaBase64: ReadString(root, "mediaBase64"),
                    InlineMediaMimeType: ReadString(root, "mediaMimeType"),
                    InlineMediaFileName: ReadString(root, "mediaFileName"),
                    InlineMediaKind: ReadString(root, "mediaKind"),
                    MediaUnavailable: ReadBoolean(root, "mediaUnavailable"),
                    QuotedMessageExternalId: ReadString(root, "quotedMessageId"),
                    QuotedText: ReadString(root, "quotedText"),
                    CustomerName: ReadString(root, "contactName"),
                    CustomerImageUrl: ReadString(root, "contactImageUrl"),
                    MessageCreatedAt: ReadDateTimeOffset(root, "createdAt"),
                    IsGroup: ReadBoolean(root, "isGroup"),
                    IsChannel: ReadBoolean(root, "isChannel"),
                    IsFromMe: ReadBoolean(root, "fromMe"),
                    IsHistorical: ReadBoolean(root, "historical"))];
            }

            if (eventType == "status")
            {
                return [new WhatsAppDeliveryStatusEvent(
                    eventId,
                    $"linked:{sessionId}",
                    ReadRequiredString(root, "messageId"),
                    ReadRequiredString(root, "status"),
                    ReadString(root, "error"))];
            }

                    if (eventType == "contact")
                    {
                    return [new WhatsAppContactUpdateEvent(
                        eventId,
                        $"linked:{sessionId}",
                        ReadRequiredString(root, "contactId"),
                        ReadRequiredString(root, "contactName"),
                        ReadString(root, "contactImageUrl"))];
            }

            if (eventType == "chat")
            {
                return [new WhatsAppChatUpdateEvent(
                    eventId,
                    $"linked:{sessionId}",
                    ReadRequiredString(root, "contactId"),
                    ReadString(root, "contactName"),
                    ReadString(root, "contactImageUrl"),
                    ReadDateTimeOffset(root, "createdAt"),
                    ReadBoolean(root, "isGroup"),
                    ReadBoolean(root, "isChannel"))];
            }

            if (eventType == "reaction")
            {
                return [new WhatsAppReactionEvent(
                    eventId,
                    $"linked:{sessionId}",
                    ReadRequiredString(root, "messageId"),
                    ReadString(root, "emoji") ?? string.Empty,
                    ReadRequiredString(root, "sender"))];
            }

                    if (eventType == "identity")
                    {
                    return [new WhatsAppIdentityMappingEvent(
                        eventId,
                        $"linked:{sessionId}",
                        ReadRequiredString(root, "sourceId"),
                        ReadRequiredString(root, "contactId"),
                        ReadString(root, "contactName"),
                        ReadString(root, "contactImageUrl"))];
                    }

            return [];
        }
        catch (JsonException exception)
        {
            throw new WhatsAppMalformedPayloadException("Invalid linked-device webhook payload.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new WhatsAppMalformedPayloadException("Linked-device webhook is missing required fields.", exception);
        }
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        var value = ReadString(root, propertyName);
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(propertyName) : value;
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}

public sealed class LinkedDeviceWhatsAppSender(
    HttpClient httpClient,
    IOptions<WhatsAppProviderOptions> options)
{
    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ChannelExternalId) || !message.ChannelExternalId.StartsWith("linked:", StringComparison.Ordinal))
            throw new InvalidOperationException("El mensaje no tiene una sesión de dispositivo vinculado válida.");
        if (message.ImageData is not null)
            throw new InvalidOperationException("El bridge QR todavía solo admite mensajes de texto.");

        var sessionId = message.ChannelExternalId["linked:".Length..];
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}/messages")
        {
            Content = JsonContent.Create(new { to = message.RecipientExternalId, text = message.Text }),
        };
        request.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"El bridge QR rechazó el mensaje: {(int)response.StatusCode} {body}");

        using var document = JsonDocument.Parse(body);
        var messageId = document.RootElement.TryGetProperty("messageId", out var id) ? id.GetString() : null;
        return new WhatsAppSendResult(messageId ?? string.Empty);
    }
}

public sealed class RoutingWhatsAppMessageSender(
    MetaWhatsAppSender metaSender,
    MockWhatsAppSender mockSender,
    LinkedDeviceWhatsAppSender linkedDeviceSender,
    IOptions<WhatsAppProviderOptions> options)
    : IWhatsAppMessageSender
{
    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken) =>
        message.ChannelExternalId?.StartsWith("linked:", StringComparison.Ordinal) == true
            ? linkedDeviceSender.SendAsync(message, cancellationToken)
            : message.ChannelExternalId?.StartsWith("mock:", StringComparison.Ordinal) == true
                ? mockSender.SendAsync(message, cancellationToken)
                : string.Equals(options.Value.Provider, "mock", StringComparison.OrdinalIgnoreCase)
                    ? mockSender.SendAsync(message, cancellationToken)
                    : metaSender.SendAsync(message, cancellationToken);
}
