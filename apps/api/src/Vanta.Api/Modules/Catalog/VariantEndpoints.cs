using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Catalog;

public sealed record CreateVariantRequest(
    [property: Required, StringLength(200, MinimumLength = 1)] string Name,
    [property: Required, StringLength(100, MinimumLength = 1)] string Sku,
    [property: Range(0, int.MaxValue)] int StockQuantity,
    [property: Range(typeof(decimal), "0", "999999999")] decimal? PriceOverride);

public sealed record UpdateVariantStockRequest(
    [property: Range(0, int.MaxValue)] int StockQuantity);

public sealed record VariantResponse(
    Guid Id,
    Guid ProductId,
    string Name,
    string Sku,
    int StockQuantity,
    int StockOnHand,
    int ReservedStock,
    int AvailableStock,
    decimal? PriceOverride,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class VariantEndpoints
{
    public static RouteGroupBuilder MapVariantEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{productId:guid}/variants", ListAsync);
        group.MapPost("/{productId:guid}/variants", CreateAsync);
        group.MapPatch("/{productId:guid}/variants/{variantId:guid}/stock", UpdateStockAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        Guid productId,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var ownsProduct = await db.Products.AnyAsync(
            product => product.Id == productId && product.TenantId == tenantId,
            cancellationToken);
        if (!ownsProduct)
        {
            return Results.NotFound();
        }

        var variants = await db.ProductVariants
            .AsNoTracking()
            .Where(variant => variant.ProductId == productId && variant.TenantId == tenantId && variant.IsActive)
            .OrderBy(variant => variant.Name)
            .Select(variant => ToResponse(variant))
            .ToListAsync(cancellationToken);

        return Results.Ok(variants);
    }

    private static async Task<IResult> CreateAsync(
        Guid productId,
        CreateVariantRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var ownsProduct = await db.Products.AnyAsync(
            product => product.Id == productId && product.TenantId == tenantId && product.IsActive,
            cancellationToken);
        if (!ownsProduct)
        {
            return Results.NotFound();
        }

        var normalizedSku = request.Sku.Trim().ToUpperInvariant();
        var skuExists = await db.ProductVariants.AnyAsync(
            variant => variant.TenantId == tenantId && variant.Sku == normalizedSku,
            cancellationToken);
        if (skuExists)
        {
            return Results.Conflict(new { error = "Ya existe una variante con ese SKU." });
        }

        var now = DateTimeOffset.UtcNow;
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProductId = productId,
            Name = request.Name.Trim(),
            Sku = normalizedSku,
            StockQuantity = request.StockQuantity,
            PriceOverride = request.PriceOverride,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/products/{productId}/variants/{variant.Id}", ToResponse(variant));
    }

    private static async Task<IResult> UpdateStockAsync(
        Guid productId,
        Guid variantId,
        UpdateVariantStockRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(
            current => current.Id == variantId
                && current.ProductId == productId
                && current.TenantId == tenantId
                && current.IsActive,
            cancellationToken);
        if (variant is null)
        {
            return Results.NotFound();
        }

        if (request.StockQuantity < 0)
        {
            return Results.BadRequest(new { error = "El stock no puede ser negativo." });
        }

        if (request.StockQuantity < variant.ReservedStock)
        {
            return Results.Conflict(new { error = "El stock físico no puede ser menor que el stock reservado." });
        }
        variant.StockQuantity = request.StockQuantity;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(variant));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        return Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
    }

    private static VariantResponse ToResponse(ProductVariant variant) => new(
        variant.Id,
        variant.ProductId,
        variant.Name,
        variant.Sku,
        variant.StockQuantity - variant.ReservedStock,
        variant.StockQuantity,
        variant.ReservedStock,
        variant.StockQuantity - variant.ReservedStock,
        variant.PriceOverride,
        variant.IsActive,
        variant.CreatedAt,
        variant.UpdatedAt);
}
