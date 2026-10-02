using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Catalog;

public sealed record CreateProductRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: StringLength(200)] string? Description,
    [property: StringLength(100, MinimumLength = 1)] string? Sku,
    [property: Range(typeof(decimal), "0", "999999999")] decimal Price,
    [property: Range(typeof(decimal), "0", "100")] decimal DiscountPercent,
    Guid? CategoryId,
    IReadOnlyList<string>? AvailableSizes,
    string? Gender);

public sealed record UpdateProductRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: StringLength(200)] string? Description,
    [property: Range(typeof(decimal), "0", "999999999")] decimal Price,
    [property: Range(typeof(decimal), "0", "100")] decimal DiscountPercent,
    bool IsActive,
    Guid? CategoryId,
    IReadOnlyList<string>? AvailableSizes,
    string? Gender);

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string ProductNumber,
    string Sku,
    decimal Price,
    decimal DiscountPercent,
    decimal DiscountedPrice,
    bool IsActive,
    string? ImageBase64,
    string? ImageMediaType,
    Guid? CategoryId,
    string? CategoryName,
    IReadOnlyList<string> AvailableSizes,
    string Gender,
    IReadOnlyList<ProductImageResponse> Images,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductImageResponse(Guid Id, string? ImageBase64, string MediaType, int SortOrder);
public sealed record UpdateProductStockRequest(
    [property: Range(0, int.MaxValue)] int StockQuantity,
    IReadOnlyList<string>? Sizes);


public static class ProductEndpoints
{
    public static RouteGroupBuilder MapProductEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", ListAsync);
        group.MapGet("/{productId:guid}", GetByIdAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{productId:guid}", UpdateAsync);
        group.MapPut("/{productId:guid}/stock", UpdateStockAsync);
        group.MapPost("/{productId:guid}/image", UploadImageAsync).DisableAntiforgery();
        group.MapPost("/{productId:guid}/images", UploadImagesAsync).DisableAntiforgery();
        group.MapDelete("/{productId:guid}/images/{imageId:guid}", DeleteImageAsync);
        return group;
    }

    private static async Task<IResult> GetByIdAsync(
        Guid productId,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var product = await db.Products
            .AsNoTracking()
            .Include(current => current.Category)
            .Include(current => current.Images)
            .FirstOrDefaultAsync(
                current => current.Id == productId && current.TenantId == tenantId,
                cancellationToken);
        if (product is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(ToResponse(product));
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var products = await db.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.Images)
            .Where(product => product.TenantId == tenantId && product.IsActive)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(products.Select(ToResponse));
    }

    private static async Task<IResult> CreateAsync(
        CreateProductRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        if (request.CategoryId.HasValue && !await db.ProductCategories.AnyAsync(
                category => category.Id == request.CategoryId.Value && category.TenantId == tenantId,
                cancellationToken))
        {
            return Results.BadRequest(new { error = "La categoría seleccionada no es válida." });
        }

        var normalizedSku = string.IsNullOrWhiteSpace(request.Sku)
            ? $"SKU-{Guid.NewGuid():N}"[..20].ToUpperInvariant()
            : request.Sku.Trim().ToUpperInvariant();
        var productNumber = await GenerateProductNumberAsync(db, tenantId, cancellationToken);
        var skuExists = await db.Products.AnyAsync(
            product => product.TenantId == tenantId && product.Sku == normalizedSku,
            cancellationToken);
        if (skuExists)
        {
            return Results.Conflict(new { error = "Ya existe un producto con ese SKU." });
        }

        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            ProductNumber = productNumber,
            Sku = normalizedSku,
            Price = request.Price,
            DiscountPercent = request.DiscountPercent,
            CategoryId = request.CategoryId,
            AvailableSizes = JsonSerializer.Serialize(NormalizeSizes(request.AvailableSizes)),
            Gender = NormalizeGender(request.Gender),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/products/{product.Id}", ToResponse(product));
    }

    private static async Task<IResult> UpdateAsync(
        Guid productId,
        UpdateProductRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        if (request.CategoryId.HasValue && !await db.ProductCategories.AnyAsync(
                category => category.Id == request.CategoryId.Value && category.TenantId == tenantId,
                cancellationToken))
        {
            return Results.BadRequest(new { error = "La categoría seleccionada no es válida." });
        }

        var product = await db.Products.FirstOrDefaultAsync(
            current => current.Id == productId && current.TenantId == tenantId,
            cancellationToken);
        if (product is null)
        {
            return Results.NotFound();
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Price = request.Price;
        product.DiscountPercent = request.DiscountPercent;
        product.CategoryId = request.CategoryId;
        product.AvailableSizes = JsonSerializer.Serialize(NormalizeSizes(request.AvailableSizes));
        product.Gender = NormalizeGender(request.Gender);
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(product));
    }

    private static async Task<IResult> UpdateStockAsync(
        Guid productId,
        UpdateProductStockRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var product = await db.Products.FirstOrDefaultAsync(current => current.Id == productId && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (product is null) return Results.NotFound();

        var sizes = NormalizeSizes(request.Sizes);
        var variants = await db.ProductVariants.Where(current => current.ProductId == productId && current.TenantId == tenantId && current.IsActive).ToListAsync(cancellationToken);
        var targetSizes = sizes.Count > 0 ? sizes : new[] { "UNICO" };
        foreach (var staleVariant in variants.Where(current => !targetSizes.Contains(current.Name.ToUpperInvariant()) && current.ReservedStock == 0))
        {
            staleVariant.IsActive = false;
            staleVariant.UpdatedAt = DateTimeOffset.UtcNow;
        }
        var baseQuantity = request.StockQuantity / targetSizes.Count;
        var remainder = request.StockQuantity % targetSizes.Count;
        for (var index = 0; index < targetSizes.Count; index++)
        {
            var size = targetSizes[index];
            var quantity = baseQuantity + (index < remainder ? 1 : 0);
            var variant = variants.FirstOrDefault(current => current.Name.ToUpper() == size);
            if (variant is null)
            {
                variant = new ProductVariant
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ProductId = productId,
                    Name = size, Sku = $"{product.Sku}-{size}", StockQuantity = quantity,
                    IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductVariants.Add(variant);
            }
            else
            {
                if (quantity < variant.ReservedStock)
                    return Results.Conflict(new { error = $"El stock no puede ser menor que el reservado para {size}." });
                variant.StockQuantity = quantity;
                variant.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { stockQuantity = request.StockQuantity, sizes = targetSizes });
    }

    private static async Task<IResult> UploadImageAsync(
        Guid productId,
        IFormFile file,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var product = await db.Products.FirstOrDefaultAsync(
            p => p.Id == productId && p.TenantId == tenantId,
            cancellationToken);
        if (product is null)
        {
            return Results.NotFound();
        }

        const long maxFileSize = 5 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            return Results.BadRequest(new { error = "La imagen no puede exceder 5 MB." });
        }

        var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedContentTypes.Contains(file.ContentType))
        {
            return Results.BadRequest(new { error = "Solo se permiten imágenes JPEG, PNG o WebP." });
        }

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, cancellationToken);
        product.ImageData = memoryStream.ToArray();
        product.ImageMediaType = file.ContentType;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(product));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        return Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
    }

    private static async Task<IResult> UploadImagesAsync(
        Guid productId,
        ClaimsPrincipal user,
        VantaDbContext db,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var product = await db.Products.FirstOrDefaultAsync(current => current.Id == productId && current.TenantId == tenantId, cancellationToken);
        if (product is null) return Results.NotFound();

        var files = request.Form.Files;
        if (files.Count == 0) return Results.BadRequest(new { error = "Seleccioná al menos una imagen." });
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        var nextOrder = await db.ProductImages.Where(image => image.ProductId == productId && image.TenantId == tenantId).Select(image => (int?)image.SortOrder).MaxAsync(cancellationToken) ?? -1;
        foreach (var file in files)
        {
            if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { error = "Cada imagen no puede exceder 5 MB." });
            if (!allowedTypes.Contains(file.ContentType)) return Results.BadRequest(new { error = "Sólo se permiten imágenes JPEG, PNG o WebP." });
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            db.ProductImages.Add(new ProductImage { Id = Guid.NewGuid(), TenantId = tenantId, ProductId = productId, Data = stream.ToArray(), MediaType = file.ContentType, SortOrder = ++nextOrder, CreatedAt = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { uploaded = files.Count });
    }

    private static async Task<IResult> DeleteImageAsync(
        Guid productId,
        Guid imageId,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        await db.ProductImages
            .Where(current => current.Id == imageId && current.ProductId == productId && current.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IReadOnlyList<string> NormalizeSizes(IReadOnlyList<string>? sizes) =>
        (sizes ?? Array.Empty<string>()).Select(size => size.Trim().ToUpperInvariant()).Where(size => size.Length > 0).Distinct().ToArray();

    private static string NormalizeGender(string? gender) =>
        gender is "Hombre" or "Mujer" or "Unisex" ? gender : "Unisex";

    private static async Task<string> GenerateProductNumberAsync(VantaDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var next = await db.Database.SqlQueryRaw<long>("SELECT nextval('product_sku_sequence') AS \"Value\"").SingleAsync(cancellationToken);
            return $"PRD-{next:D6}";
        }

        var nextNumber = await db.Products.CountAsync(product => product.TenantId == tenantId, cancellationToken) + 1;
        var candidate = $"PRD-{nextNumber:D6}";
        while (await db.Products.AnyAsync(product => product.TenantId == tenantId && product.ProductNumber == candidate, cancellationToken))
        {
            nextNumber++;
            candidate = $"PRD-{nextNumber:D6}";
        }

        return candidate;
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.ProductNumber,
        product.Sku,
        product.Price,
        product.DiscountPercent,
        ProductPricing.EffectivePrice(product),
        product.IsActive,
        product.ImageData is not null
            ? Convert.ToBase64String(product.ImageData)
            : product.Images.OrderBy(image => image.SortOrder).Select(image => (string?)Convert.ToBase64String(image.Data)).FirstOrDefault(),
        product.ImageMediaType ?? product.Images.OrderBy(image => image.SortOrder).Select(image => image.MediaType).FirstOrDefault(),
        product.CategoryId,
        product.Category?.Name,
        DeserializeSizes(product.AvailableSizes),
        product.Gender,
        product.Images.OrderBy(image => image.SortOrder).Select(image => new ProductImageResponse(image.Id, Convert.ToBase64String(image.Data), image.MediaType, image.SortOrder)).ToArray(),
        product.CreatedAt,
        product.UpdatedAt);

    private static IReadOnlyList<string> DeserializeSizes(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(value) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }
}
