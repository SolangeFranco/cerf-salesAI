using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

// Runs against real Postgres: the InMemory provider does not enforce unique indexes, so the
// public checkout's duplicate-submission guard ((TenantId, IdempotencyKey), see
// PublicCommerceEndpoints.BuildIdempotencyKey) can only be verified here.
public sealed class PublicCheckoutConcurrencyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Two_concurrent_identical_public_checkouts_persist_exactly_one_order()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Public Checkout Race {Guid.NewGuid():N}", Slug = $"public-race-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Producto Race", ProductNumber = "PRD-TEST-002", Sku = $"RACE-{Guid.NewGuid():N}", Price = 25000, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        setup.Tenants.Add(tenant);
        setup.Products.Add(product);
        await setup.SaveChangesAsync();

        var sharedIdempotencyKey = $"public-race-key-{Guid.NewGuid():N}";
        var first = SubmitAsync(tenant.Id, product.Id, sharedIdempotencyKey);
        var second = SubmitAsync(tenant.Id, product.Id, sharedIdempotencyKey);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(applied => applied));
        var persistedOrders = await setup.Orders.Where(order => order.TenantId == tenant.Id && order.IdempotencyKey == sharedIdempotencyKey).ToListAsync();
        Assert.Single(persistedOrders);
    }

    // Mirrors PublicCommerceEndpoints.CreateOrderAsync's write path and duplicate-key handling
    // closely enough to exercise the same unique-constraint race, without going through HTTP.
    private static async Task<bool> SubmitAsync(Guid tenantId, Guid productId, string idempotencyKey)
    {
        await using var context = CreateContext();
        var existing = await context.Orders.AsNoTracking().AnyAsync(order => order.TenantId == tenantId && order.IdempotencyKey == idempotencyKey);
        if (existing) return false;

        var now = DateTimeOffset.UtcNow;
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenantId, Number = $"RACE-{Guid.NewGuid():N}", IdempotencyKey = idempotencyKey, Status = OrderStatus.AwaitingPayment, Total = 25000, CreatedAt = now };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = productId, ProductName = "Producto Race", Sku = "RACE", Quantity = 1, UnitPrice = 25000, LineTotal = 25000 });
        context.Orders.Add(order);
        try
        {
            await context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}
