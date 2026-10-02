using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Catalog;

namespace Vanta.Api.Modules.Orders;

public sealed record AddCartItemRequest(
    [property: Required] Guid ProductId,
    Guid? VariantId,
    [property: Range(1, 100)] int Quantity);

public sealed record CartItemResponse(Guid Id, Guid ProductId, Guid? VariantId, string ProductName, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record CartResponse(Guid Id, Guid ConversationId, decimal Total, IReadOnlyList<CartItemResponse> Items);

public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/conversations/{conversationId:guid}/cart", GetAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/cart/items", AddAsync).RequireAuthorization();
        endpoints.MapDelete("/api/conversations/{conversationId:guid}/cart/items/{itemId:guid}", RemoveAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(Guid conversationId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        if (!await db.Conversations.AsNoTracking().AnyAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken)) return Results.NotFound();
        var cart = await db.Carts.AsNoTracking().Include(current => current.Items).FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        return Results.Ok(cart is null ? EmptyResponse(conversationId) : ToResponse(cart));
    }

    private static async Task<IResult> AddAsync(Guid conversationId, AddCartItemRequest request, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        if (!await db.Conversations.AnyAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken)) return Results.NotFound();
        var product = await db.Products.FirstOrDefaultAsync(current => current.Id == request.ProductId && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (product is null) return Results.NotFound(new { error = "El producto no existe o no está activo." });
        ProductVariant? variant = null;
        if (request.VariantId.HasValue)
        {
            variant = await db.ProductVariants.FirstOrDefaultAsync(current => current.Id == request.VariantId && current.ProductId == product.Id && current.TenantId == tenantId && current.IsActive, cancellationToken);
            if (variant is null) return Results.NotFound(new { error = "La variante no existe o no pertenece al producto." });
        }
        var hasVariants = await db.ProductVariants.AnyAsync(current => current.ProductId == product.Id && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (hasVariants && variant is null) return Results.BadRequest(new { error = "Debe seleccionar una variante antes de agregar el producto." });
        var cart = await db.Carts.Include(current => current.Items).FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (cart is null)
        {
            cart = new Cart { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, CreatedAt = now, UpdatedAt = now };
            db.Carts.Add(cart);
        }
        var existing = cart.Items.FirstOrDefault(item => item.ProductId == product.Id && item.VariantId == variant?.Id);
        var requestedQuantity = (existing?.Quantity ?? 0) + request.Quantity;
        if (variant is not null && variant.StockQuantity < requestedQuantity) return Results.Conflict(new { error = $"Stock insuficiente para la variante {variant.Sku}." });
        if (existing is not null) existing.Quantity = requestedQuantity;
        else cart.Items.Add(new CartItem { Id = Guid.NewGuid(), ProductId = product.Id, VariantId = variant?.Id, ProductName = product.Name, Sku = variant?.Sku ?? product.Sku, Quantity = request.Quantity, UnitPrice = ProductPricing.EffectivePrice(product, variant?.PriceOverride) });
        cart.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(cart));
    }

    private static async Task<IResult> RemoveAsync(Guid conversationId, Guid itemId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var cart = await db.Carts.Include(current => current.Items).FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        if (cart is null) return Results.NotFound();
        var item = cart.Items.FirstOrDefault(current => current.Id == itemId);
        if (item is null) return Results.NotFound();
        db.CartItems.Remove(item);
        cart.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(cart));
    }

    private static CartResponse EmptyResponse(Guid conversationId) => new(Guid.Empty, conversationId, 0, []);
    private static CartResponse ToResponse(Cart cart) => new(cart.Id, cart.ConversationId, cart.Items.Sum(item => item.UnitPrice * item.Quantity), cart.Items.Select(item => new CartItemResponse(item.Id, item.ProductId, item.VariantId, item.ProductName, item.Sku, item.Quantity, item.UnitPrice, item.UnitPrice * item.Quantity)).ToList());
    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) => Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
}
