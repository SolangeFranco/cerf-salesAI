using System.Text.Json;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Analytics;

/// <summary>
/// Records commerce events into the SAME <see cref="VantaDbContext"/> (and therefore the same
/// transaction) as the business operation that triggers them. There is no queue, bus, or outbox:
/// <see cref="Record"/> only adds to the change tracker — the caller's own existing
/// SaveChangesAsync persists it. This keeps the event and the fact it describes atomic, and keeps
/// the whole system a plain part of the modular monolith rather than a distributed component.
/// </summary>
public sealed class CommerceEventRecorder(VantaDbContext db)
{
    private const int MaxMetadataLength = 1000;

    public void Record(
        Guid tenantId,
        string type,
        Guid? conversationId = null,
        Guid? customerId = null,
        Guid? productId = null,
        Guid? cartId = null,
        Guid? orderId = null,
        Guid? paymentId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        db.CommerceEvents.Add(new CommerceEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            CreatedAt = DateTimeOffset.UtcNow,
            ConversationId = conversationId,
            CustomerId = customerId,
            ProductId = productId,
            CartId = cartId,
            OrderId = orderId,
            PaymentId = paymentId,
            Metadata = SerializeMetadata(metadata),
        });
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;
        var json = JsonSerializer.Serialize(metadata);
        return json.Length > MaxMetadataLength ? json[..MaxMetadataLength] : json;
    }
}
