using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Catalog;

public sealed record CreateCategoryRequest([property: Required, StringLength(100, MinimumLength = 2)] string Name);
public sealed record CategoryResponse(Guid Id, string Name);

public static class CategoryEndpoints
{
    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/categories", ListAsync);
        group.MapPost("/categories", CreateAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var categories = await db.ProductCategories
            .AsNoTracking()
            .Where(category => category.TenantId == tenantId)
            .OrderBy(category => category.Name)
            .Select(category => new CategoryResponse(category.Id, category.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(categories);
    }

    private static async Task<IResult> CreateAsync(
        CreateCategoryRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var name = request.Name.Trim();
        var exists = await db.ProductCategories.AnyAsync(
            category => category.TenantId == tenantId && category.Name.ToLower() == name.ToLower(),
            cancellationToken);
        if (exists) return Results.Conflict(new { error = "Ya existe una categoría con ese nombre." });

        var category = new ProductCategory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/products/categories/{category.Id}", new CategoryResponse(category.Id, category.Name));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
}