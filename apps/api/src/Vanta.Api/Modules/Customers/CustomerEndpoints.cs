using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Customers;

public sealed record CreateCustomerRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: Required, StringLength(40, MinimumLength = 6)] string Phone,
    [property: EmailAddress, StringLength(200)] string? Email,
    [property: StringLength(500)] string? Address);

public sealed record CustomerResponse(Guid Id, string Name, string Phone, string? Email, string? Address, DateTimeOffset CreatedAt);

public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var customers = await db.Customers
            .AsNoTracking()
            .Where(customer => customer.TenantId == tenantId)
            .OrderBy(customer => customer.Name)
            .Select(customer => ToResponse(customer))
            .ToListAsync(cancellationToken);

        return Results.Ok(customers);
    }

    private static async Task<IResult> CreateAsync(
        CreateCustomerRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();

        var phone = request.Phone.Trim();
        var exists = await db.Customers.AnyAsync(
            customer => customer.TenantId == tenantId && customer.Phone == phone,
            cancellationToken);
        if (exists) return Results.Conflict(new { error = "Ya existe un cliente con ese teléfono." });

        var now = DateTimeOffset.UtcNow;
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Phone = phone,
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/customers/{customer.Id}", ToResponse(customer));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.Name, customer.Phone, customer.Email, customer.Address, customer.CreatedAt);
}
