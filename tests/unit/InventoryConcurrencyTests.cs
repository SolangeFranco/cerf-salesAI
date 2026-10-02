using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class InventoryConcurrencyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Two_concurrent_orders_can_reserve_only_available_stock()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Concurrency {Guid.NewGuid():N}", Slug = $"concurrency-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Concurrent product", ProductNumber = "PRD-TEST-001", Sku = $"CON-{Guid.NewGuid():N}", Price = 1000, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var variant = new ProductVariant { Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, Name = "One unit", Sku = $"CON-VAR-{Guid.NewGuid():N}", StockQuantity = 1, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        setup.Tenants.Add(tenant);
        setup.Products.Add(product);
        setup.ProductVariants.Add(variant);
        await setup.SaveChangesAsync();

        var firstOrder = CreateOrder(tenant.Id, product.Id, variant.Id);
        var secondOrder = CreateOrder(tenant.Id, product.Id, variant.Id);
        var first = ReserveAsync(firstOrder);
        var second = ReserveAsync(secondOrder);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result));
        await setup.Entry(variant).ReloadAsync();
        Assert.Equal(1, variant.StockQuantity);
        Assert.Equal(1, variant.ReservedStock);
    }

    private static async Task<bool> ReserveAsync(Order order)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        var inventory = new InventoryService(context);
        try
        {
            await inventory.ReserveOrderAsync(order, CancellationToken.None);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        catch (InsufficientStockException)
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    private static Order CreateOrder(Guid tenantId, Guid productId, Guid variantId)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Number = $"CON-{Guid.NewGuid():N}",
            IdempotencyKey = $"CON-KEY-{Guid.NewGuid():N}", Status = OrderStatus.New, Total = 1000, CreatedAt = now,
        };
        order.Items.Add(new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = productId, VariantId = variantId, ProductName = "Concurrent product", Sku = "CON-VAR", Quantity = 1, UnitPrice = 1000, LineTotal = 1000 });
        return order;
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}
