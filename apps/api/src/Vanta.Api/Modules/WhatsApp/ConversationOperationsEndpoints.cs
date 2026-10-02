using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Orders;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record AssignConversationRequest(Guid? UserId);

public sealed record ConversationContextResponse(
    Guid Id,
    string CustomerExternalId,
    string Status,
    string? CurrentIntent,
    Guid? AssignedUserId,
    DateTimeOffset? AssignedAt,
    DateTimeOffset LastMessageAt,
    DateTimeOffset LastActivityAt,
    ConversationCartSummary? Cart,
    ConversationOrderSummary? LatestOrder,
    IReadOnlyList<ConversationActivityResponse> RecentActivity);

public sealed record ConversationCartSummary(Guid Id, decimal Total, IReadOnlyList<ConversationCartItemSummary> Items);
public sealed record ConversationCartItemSummary(string ProductName, int Quantity, decimal UnitPrice);
public sealed record ConversationOrderSummary(Guid Id, string Number, string Status, decimal Total, DateTimeOffset CreatedAt);
public sealed record ConversationActivityResponse(string Type, string? FromStatus, string? ToStatus, Guid? ActorUserId, Guid? AssignedUserId, DateTimeOffset CreatedAt);

/// <summary>
/// Merchant-facing conversation operations: taking control, assigning, pausing/resuming the AI,
/// closing, and viewing the full operational context. Separate from WhatsAppEndpoints.cs, which
/// owns the channel/webhook/message-thread routes.
/// </summary>
public static class ConversationOperationsEndpoints
{
    public static IEndpointRouteBuilder MapConversationOperations(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/conversations/{conversationId:guid}/take-control", TakeControlAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/assign", AssignAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/pause", PauseAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/resume", ResumeAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/close", CloseAsync).RequireAuthorization();
        endpoints.MapGet("/api/conversations/{conversationId:guid}/context", ContextAsync).RequireAuthorization();
        return endpoints;
    }

    private static bool TryGetTenantAndUser(ClaimsPrincipal user, out Guid tenantId, out Guid userId)
    {
        userId = Guid.Empty;
        return Guid.TryParse(user.FindFirst("tenant_id")?.Value, out tenantId)
            && Guid.TryParse(user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out userId);
    }

    private static IResult ToResult(ConversationOperationResult result) => result switch
    {
        ConversationOperationResult.Success => Results.Ok(new { success = true }),
        ConversationOperationResult.NotFound => Results.NotFound(),
        ConversationOperationResult.AssigneeNotInTenant => Results.BadRequest(new { error = "El usuario asignado no pertenece a este tenant." }),
        _ => Results.Conflict(new { error = "La conversación no admite esa transición desde su estado actual." }),
    };

    private static async Task<IResult> TakeControlAsync(Guid conversationId, ClaimsPrincipal user, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!TryGetTenantAndUser(user, out var tenantId, out var userId)) return Results.Unauthorized();
        return ToResult(await conversations.TakeControlAsync(tenantId, conversationId, userId, cancellationToken));
    }

    private static async Task<IResult> AssignAsync(Guid conversationId, AssignConversationRequest request, ClaimsPrincipal user, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!TryGetTenantAndUser(user, out var tenantId, out var userId)) return Results.Unauthorized();
        return ToResult(await conversations.AssignAsync(tenantId, conversationId, request.UserId, userId, cancellationToken));
    }

    private static async Task<IResult> PauseAsync(Guid conversationId, ClaimsPrincipal user, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!TryGetTenantAndUser(user, out var tenantId, out var userId)) return Results.Unauthorized();
        return ToResult(await conversations.PauseAsync(tenantId, conversationId, userId, cancellationToken));
    }

    private static async Task<IResult> ResumeAsync(Guid conversationId, ClaimsPrincipal user, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!TryGetTenantAndUser(user, out var tenantId, out var userId)) return Results.Unauthorized();
        return ToResult(await conversations.ResumeAsync(tenantId, conversationId, userId, cancellationToken));
    }

    private static async Task<IResult> CloseAsync(Guid conversationId, ClaimsPrincipal user, ConversationService conversations, CancellationToken cancellationToken)
    {
        if (!TryGetTenantAndUser(user, out var tenantId, out var userId)) return Results.Unauthorized();
        return ToResult(await conversations.CloseAsync(tenantId, conversationId, userId, cancellationToken));
    }

    private static async Task<IResult> ContextAsync(Guid conversationId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(
            current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return Results.NotFound();

        var cart = await db.Carts.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.ConversationId == conversationId)
            .Select(current => new ConversationCartSummary(
                current.Id,
                current.Items.Sum(item => item.Quantity * item.UnitPrice),
                current.Items.Select(item => new ConversationCartItemSummary(item.ProductName, item.Quantity, item.UnitPrice)).ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        var latestOrder = await db.Orders.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.ConversationId == conversationId)
            .OrderByDescending(current => current.CreatedAt)
            .Select(current => new ConversationOrderSummary(current.Id, current.Number, current.Status, current.Total, current.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        var recentActivity = await db.ConversationActivities.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.ConversationId == conversationId)
            .OrderByDescending(current => current.CreatedAt)
            .Take(20)
            .Select(current => new ConversationActivityResponse(current.Type, current.FromStatus, current.ToStatus, current.ActorUserId, current.AssignedUserId, current.CreatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new ConversationContextResponse(
            conversation.Id, conversation.CustomerExternalId, conversation.Status, conversation.CurrentIntent,
            conversation.AssignedUserId, conversation.AssignedAt, conversation.LastMessageAt, conversation.LastActivityAt,
            cart, latestOrder, recentActivity));
    }
}
