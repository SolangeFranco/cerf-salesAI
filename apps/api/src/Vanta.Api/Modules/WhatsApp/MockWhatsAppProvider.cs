using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

/// <summary>
/// Ingestion adapter used for local development/tests. Mirrors the real shape of a provider
/// adapter (validate + parse) without depending on any external service.
/// </summary>
public sealed class MockWhatsAppProvider(IOptions<WhatsAppProviderOptions> options) : IWhatsAppProvider
{
    public string Name => "mock";

    public bool ValidateWebhook(WhatsAppWebhookSignal signal) =>
        !string.IsNullOrWhiteSpace(options.Value.MockWebhookSecret) &&
        signal.Headers.TryGetValue("X-Webhook-Signature", out var signature) &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(signature),
            System.Text.Encoding.UTF8.GetBytes(options.Value.MockWebhookSecret));

    public IReadOnlyList<WhatsAppInboundEvent> ParseWebhook(WhatsAppWebhookSignal signal)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(signal.RawBody);
        }
        catch (JsonException exception)
        {
            throw new WhatsAppMalformedPayloadException("Invalid mock webhook payload.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            var eventId = ReadString(root, "eventId");
            var accountExternalId = ReadString(root, "accountExternalId");
            var customerExternalId = ReadString(root, "customerExternalId");
            var messageId = ReadString(root, "messageId");
            var text = ReadString(root, "text") ?? string.Empty;
            var imageBase64 = ReadString(root, "imageBase64");
            var imageMediaType = ReadString(root, "imageMediaType");

            if (string.IsNullOrWhiteSpace(eventId) || eventId.Length < 8
                || string.IsNullOrWhiteSpace(accountExternalId)
                || string.IsNullOrWhiteSpace(customerExternalId)
                || string.IsNullOrWhiteSpace(messageId) || messageId.Length < 8)
            {
                throw new WhatsAppMalformedPayloadException("Missing or invalid required fields in mock webhook payload.");
            }

            return
            [
                new WhatsAppInboundMessageEvent(
                    EventId: eventId,
                    ChannelExternalId: accountExternalId,
                    CustomerExternalId: customerExternalId,
                    MessageExternalId: messageId,
                    Text: text,
                    MediaId: null,
                    MediaType: null,
                    InlineImageBase64: imageBase64,
                    InlineImageMediaType: imageMediaType),
            ];
        }
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
