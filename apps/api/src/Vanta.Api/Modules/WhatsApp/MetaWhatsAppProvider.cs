using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

/// <summary>
/// Ingestion adapter for the Meta WhatsApp Cloud API. Owns HMAC signature validation and
/// translation of Meta's payload shape into VANTA's canonical inbound events; no other part of
/// the domain parses or validates Meta payloads directly.
/// </summary>
public sealed class MetaWhatsAppProvider(IOptions<WhatsAppProviderOptions> options) : IWhatsAppProvider
{
    public string Name => "meta";

    public bool ValidateWebhook(WhatsAppWebhookSignal signal)
    {
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.AppSecret)) return true;

        if (!signal.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(configuration.AppSecret));
        var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signal.RawBody))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signatureHeader), Encoding.UTF8.GetBytes(expected));
    }

    public IReadOnlyList<WhatsAppInboundEvent> ParseWebhook(WhatsAppWebhookSignal signal)
    {
        try
        {
            var deliveryStatuses = MetaWebhookParser.ParseDeliveryStatuses(signal.RawBody);
            if (deliveryStatuses.Count > 0)
            {
                var channelExternalId = ExtractPhoneNumberId(signal.RawBody);
                return deliveryStatuses
                    .Select(status => (WhatsAppInboundEvent)new WhatsAppDeliveryStatusEvent(
                        EventId: $"{status.MessageId}:{status.Status}",
                        ChannelExternalId: channelExternalId ?? string.Empty,
                        MessageExternalId: status.MessageId,
                        Status: MetaWebhookParser.NormalizeDeliveryStatus(status.Status),
                        Error: status.Error))
                    .ToList();
            }

            var inbound = MetaWebhookParser.ParseTextMessage(signal.RawBody);
            if (inbound is null) return [];

            return
            [
                new WhatsAppInboundMessageEvent(
                    EventId: inbound.MessageId,
                    ChannelExternalId: inbound.PhoneNumberId ?? string.Empty,
                    CustomerExternalId: inbound.CustomerExternalId,
                    MessageExternalId: inbound.MessageId,
                    Text: inbound.Text,
                    MediaId: inbound.MediaId,
                    MediaType: inbound.MediaType),
            ];
        }
        catch (JsonException exception)
        {
            throw new WhatsAppMalformedPayloadException("Invalid Meta webhook payload.", exception);
        }
    }

    private static string? ExtractPhoneNumberId(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        if (!document.RootElement.TryGetProperty("entry", out var entries) || entries.GetArrayLength() == 0) return null;
        var changes = entries[0].GetProperty("changes");
        if (changes.GetArrayLength() == 0) return null;
        var value = changes[0].GetProperty("value");
        return value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var phoneId)
            ? phoneId.GetString()
            : null;
    }
}
