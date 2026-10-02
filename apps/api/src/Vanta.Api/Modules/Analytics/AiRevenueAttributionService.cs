using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;

namespace Vanta.Api.Modules.Analytics;

public sealed record AiRevenueAttributionResponse(
    decimal TotalRevenue,
    int PaidOrders,
    decimal AiAttributedRevenue,
    int AiCreatedOrders,
    int AiAssistedOrders,
    decimal AbandonedCartRecoveredRevenue,
    int AbandonedCartRecoveredOrders,
    bool HasAttributionData);

public sealed record AttributedOrder(
    Guid OrderId,
    decimal Revenue,
    string AttributionType,
    bool AbandonedCartRecovered);

/// <summary>
/// Reconstructs attribution from persisted orders, payments and commerce events.
/// It never estimates revenue and deduplicates every classification by OrderId.
/// </summary>
public sealed class AiRevenueAttributionService(VantaDbContext db)
{
    private static readonly HashSet<string> FinalRevenueStatuses =
    [
        OrderStatus.Paid,
        OrderStatus.Preparing,
        OrderStatus.Shipped,
        OrderStatus.Delivered,
    ];

    private static readonly HashSet<string> AssistingEventTypes =
    [
        CommerceEventType.ProductViewed,
        CommerceEventType.ProductRecommended,
        CommerceEventType.ProductAddedToCart,
        CommerceEventType.CartCreated,
        CommerceEventType.CheckoutStarted,
        CommerceEventType.HumanHandoff,
    ];

    public async Task<AiRevenueAttributionResponse> GetSummaryAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(order => order.TenantId == tenantId)
            .Select(order => new OrderSnapshot(order.Id, order.ConversationId, order.Total, order.Status, order.CreatedAt))
            .ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.Status == PaymentStatus.Paid)
            .Select(payment => payment.OrderId)
            .ToHashSetAsync(cancellationToken);
        var events = await db.CommerceEvents.AsNoTracking()
            .Where(current => current.TenantId == tenantId)
            .OrderBy(current => current.CreatedAt)
            .Select(current => new EventSnapshot(current.Id, current.Type, current.CreatedAt, current.ConversationId, current.OrderId, current.Metadata))
            .ToListAsync(cancellationToken);

        var attributedOrders = BuildAttribution(orders, payments, events);
        var revenueOrders = orders.Where(order => payments.Contains(order.Id) && FinalRevenueStatuses.Contains(order.Status)).ToList();

        return new AiRevenueAttributionResponse(
            revenueOrders.Sum(order => order.Total),
            revenueOrders.Count,
            attributedOrders.Sum(order => order.Revenue),
            attributedOrders.Count(order => order.AttributionType == AttributionTypes.AiCreated),
            attributedOrders.Count(order => order.AttributionType == AttributionTypes.AiAssisted),
            attributedOrders.Where(order => order.AbandonedCartRecovered).Sum(order => order.Revenue),
            attributedOrders.Count(order => order.AbandonedCartRecovered),
            attributedOrders.Count > 0);
    }

    public async Task<IReadOnlyList<AttributedOrder>> GetAttributedOrdersAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(order => order.TenantId == tenantId)
            .Select(order => new OrderSnapshot(order.Id, order.ConversationId, order.Total, order.Status, order.CreatedAt))
            .ToListAsync(cancellationToken);
        var payments = await db.Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.Status == PaymentStatus.Paid)
            .Select(payment => payment.OrderId)
            .ToHashSetAsync(cancellationToken);
        var events = await db.CommerceEvents.AsNoTracking()
            .Where(current => current.TenantId == tenantId)
            .OrderBy(current => current.CreatedAt)
            .Select(current => new EventSnapshot(current.Id, current.Type, current.CreatedAt, current.ConversationId, current.OrderId, current.Metadata))
            .ToListAsync(cancellationToken);

        return BuildAttribution(orders, payments, events);
    }

    private static IReadOnlyList<AttributedOrder> BuildAttribution(
        IReadOnlyList<OrderSnapshot> orders,
        IReadOnlySet<Guid> paidOrderIds,
        IReadOnlyList<EventSnapshot> events)
    {
        var result = new List<AttributedOrder>();
        foreach (var order in orders)
        {
            if (!paidOrderIds.Contains(order.Id) || !FinalRevenueStatuses.Contains(order.Status)) continue;

            var orderEvents = events.Where(current => current.OrderId == order.Id).ToList();
            var aiCreated = orderEvents.Any(current => current.Type == CommerceEventType.OrderCreated && MetadataSourceIs(current.Metadata, AttributionSources.AiTool));
            var assisted = !aiCreated && order.ConversationId.HasValue && events.Any(current =>
                current.ConversationId == order.ConversationId &&
                current.CreatedAt <= order.CreatedAt &&
                AssistingEventTypes.Contains(current.Type));
            if (!aiCreated && !assisted) continue;

            result.Add(new AttributedOrder(
                order.Id,
                order.Total,
                aiCreated ? AttributionTypes.AiCreated : AttributionTypes.AiAssisted,
                orderEvents.Any(current => current.Type == CommerceEventType.AbandonedCartRecovered)));
        }

        return result;
    }

    private static bool MetadataSourceIs(string? metadata, string source)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return false;
        try
        {
            using var document = JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty("source", out var value) && value.GetString() == source;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record OrderSnapshot(Guid Id, Guid? ConversationId, decimal Total, string Status, DateTimeOffset CreatedAt);
    private sealed record EventSnapshot(Guid Id, string Type, DateTimeOffset CreatedAt, Guid? ConversationId, Guid? OrderId, string? Metadata);
}

public static class AttributionTypes
{
    public const string AiCreated = "ai_created";
    public const string AiAssisted = "ai_assisted";
}

public static class AttributionSources
{
    public const string AiTool = "ai_tool";
    public const string MerchantApi = "merchant_api";
    public const string PublicStore = "public_store";
}