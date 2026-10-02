using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Analytics;

public sealed record CommerceEventResponse(
    Guid Id, string Type, DateTimeOffset CreatedAt, Guid? ConversationId, Guid? CustomerId,
    Guid? ProductId, Guid? CartId, Guid? OrderId, Guid? PaymentId, string? Metadata);

/// <summary>
/// Read-only access to the commerce event log — this is what lets a merchant/analytics view
/// reconstruct a conversation's whole journey (conversation -> recommendation -> cart -> order ->
/// payment) after the fact, by filtering/ordering on the ids every event already carries.
/// </summary>
public static class CommerceEventEndpoints
{
    public static IEndpointRouteBuilder MapCommerceEventsModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/analytics").RequireAuthorization();
        group.MapGet("/commerce-events", ListAsync);
        group.MapGet("/conversations/{conversationId:guid}/timeline", GetConversationTimelineAsync);
        group.MapGet("/revenue-attribution", GetRevenueAttributionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRevenueAttributionAsync(
        ClaimsPrincipal user, AiRevenueAttributionService attribution, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        return Results.Ok(await attribution.GetSummaryAsync(tenantId, cancellationToken));
    }

    private static async Task<IResult> ListAsync(
        string? type, Guid? conversationId, Guid? orderId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var query = db.CommerceEvents.AsNoTracking().Where(current => current.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(current => current.Type == type);
        if (conversationId.HasValue) query = query.Where(current => current.ConversationId == conversationId);
        if (orderId.HasValue) query = query.Where(current => current.OrderId == orderId);

        var events = await query.OrderByDescending(current => current.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Results.Ok(events.Select(ToResponse));
    }

    // The reconstructed journey for one conversation, oldest first: e.g. ConversationStarted ->
    // ProductRecommended -> ProductAddedToCart -> CheckoutStarted -> OrderCreated ->
    // PaymentStarted -> PaymentConfirmed.
    private static async Task<IResult> GetConversationTimelineAsync(
        Guid conversationId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var ownsConversation = await db.Conversations.AnyAsync(
            current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (!ownsConversation) return Results.NotFound();

        var events = await db.CommerceEvents.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.ConversationId == conversationId)
            .OrderBy(current => current.CreatedAt)
            .ToListAsync(cancellationToken);
        return Results.Ok(events.Select(ToResponse));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);

    private static CommerceEventResponse ToResponse(CommerceEvent current) => new(
        current.Id, current.Type, current.CreatedAt, current.ConversationId, current.CustomerId,
        current.ProductId, current.CartId, current.OrderId, current.PaymentId, current.Metadata);
}
