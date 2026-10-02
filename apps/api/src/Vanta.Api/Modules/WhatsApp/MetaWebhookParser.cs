using System.Text.Json;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record MetaInboundTextMessage(string MessageId, string CustomerExternalId, string Text, string? MediaId = null, string? MediaType = null, string? PhoneNumberId = null);
public sealed record MetaDeliveryStatus(string MessageId, string Status, string? Error = null);

public static class MetaWebhookParser
{
    private static readonly IReadOnlyDictionary<string, int> DeliveryStatusRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["pending"] = 0,
        ["sent"] = 1,
        ["delivered"] = 2,
        ["read"] = 3,
        ["failed"] = -1,
    };

    public static string NormalizeDeliveryStatus(string status)
    {
        if (status.Equals("delivered", StringComparison.OrdinalIgnoreCase)) return "Delivered";
        if (status.Equals("read", StringComparison.OrdinalIgnoreCase)) return "Read";
        if (status.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (status.Equals("sent", StringComparison.OrdinalIgnoreCase)) return "Sent";
        return status;
    }

    public static bool ShouldAdvanceDeliveryStatus(string? current, string next)
    {
        var currentRank = current is not null && DeliveryStatusRank.TryGetValue(current, out var currentValue) ? currentValue : -1;
        var nextRank = DeliveryStatusRank.TryGetValue(next, out var nextValue) ? nextValue : currentRank;
        return next.Equals("Failed", StringComparison.OrdinalIgnoreCase) || nextRank >= currentRank;
    }

    public static IReadOnlyList<MetaDeliveryStatus> ParseDeliveryStatuses(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var statuses = new List<MetaDeliveryStatus>();
        if (!document.RootElement.TryGetProperty("entry", out var entries)) return statuses;

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes)) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value) || !value.TryGetProperty("statuses", out var statusItems)) continue;
                foreach (var item in statusItems.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || !item.TryGetProperty("status", out var status)) continue;
                    string? error = null;
                    if (item.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
                        error = errors[0].ToString();
                    statuses.Add(new MetaDeliveryStatus(id.GetString() ?? string.Empty, status.GetString() ?? string.Empty, error));
                }
            }
        }

        return statuses;
    }

    public static MetaInboundTextMessage? ParseTextMessage(string rawBody)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("entry", out var entries) || entries.GetArrayLength() == 0) return null;
        var changes = entries[0].GetProperty("changes");
        if (changes.GetArrayLength() == 0) return null;
        var value = changes[0].GetProperty("value");
        var phoneNumberId = value.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("phone_number_id", out var phoneId)
            ? phoneId.GetString()
            : null;
        if (!value.TryGetProperty("messages", out var messages) || messages.GetArrayLength() == 0) return null;
        var message = messages[0];
        if (!message.TryGetProperty("id", out var messageId) || !message.TryGetProperty("from", out var sender)) return null;
        var type = message.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (type == "text" && message.TryGetProperty("text", out var text) && text.TryGetProperty("body", out var body))
            return new MetaInboundTextMessage(messageId.GetString() ?? string.Empty, sender.GetString() ?? string.Empty, body.GetString() ?? string.Empty, PhoneNumberId: phoneNumberId);
        if (type == "image" && message.TryGetProperty("image", out var image) && image.TryGetProperty("id", out var mediaId))
            return new MetaInboundTextMessage(messageId.GetString() ?? string.Empty, sender.GetString() ?? string.Empty, "", mediaId.GetString(), "image", phoneNumberId);
        return null;
    }
}
