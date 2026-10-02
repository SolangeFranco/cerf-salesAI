using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Jobs;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class OrderExpirationConcurrencyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Two_workers_release_an_expired_reservation_exactly_once()
    {
        var (tenantId, orderId, paymentId, variantId) = await SeedAsync();

        await Task.WhenAll(ExpireAsync(tenantId, orderId), ExpireAsync(tenantId, orderId));

        await using var verify = CreateContext();
        var order = await verify.Orders.SingleAsync(current => current.Id == orderId);
        var payment = await verify.Payments.SingleAsync(current => current.Id == paymentId);
        var variant = await verify.ProductVariants.SingleAsync(current => current.Id == variantId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Expired, payment.Status);
        Assert.Equal(0, variant.ReservedStock);
        Assert.Equal(1, await verify.InventoryMovements.CountAsync(current => current.OrderId == orderId && current.Type == InventoryMovementType.Release));
        Assert.Equal(1, await verify.OrderStatusHistory.CountAsync(current => current.OrderId == orderId && current.ToStatus == OrderStatus.Cancelled));
        Assert.Equal(1, await verify.PaymentStatusHistory.CountAsync(current => current.PaymentId == paymentId && current.ToStatus == PaymentStatus.Expired));
    }

    [Fact]
    public async Task Payment_confirmation_and_expiration_never_leave_a_mixed_state()
    {
        var (tenantId, orderId, paymentId, variantId) = await SeedAsync();

        await Task.WhenAll(ExpireAsync(tenantId, orderId), ConfirmAsync(tenantId, orderId, paymentId));

        await using var verify = CreateContext();
        var order = await verify.Orders.SingleAsync(current => current.Id == orderId);
        var payment = await verify.Payments.SingleAsync(current => current.Id == paymentId);
        var variant = await verify.ProductVariants.SingleAsync(current => current.Id == variantId);
        if (payment.Status == PaymentStatus.Paid)
        {
            Assert.Equal(OrderStatus.Paid, order.Status);
            Assert.Equal(2, variant.ReservedStock);
        }
        else
        {
            Assert.Equal(PaymentStatus.Expired, payment.Status);
            Assert.Equal(OrderStatus.Cancelled, order.Status);
            Assert.Equal(0, variant.ReservedStock);
        }
    }

    private static async Task ExpireAsync(Guid tenantId, Guid orderId)
    {
        await using var db = CreateContext();
        var service = new OrderExpirationService(db, new PaymentService(db, new CommerceEventRecorder(db)), new InventoryService(db));
        await service.ExpireAsync(new BackgroundJob
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Type = BackgroundJobType.OrderExpiration,
            IdempotencyKey = $"order-expiration-{orderId:N}", Payload = JsonSerializer.Serialize(new { orderId }),
            Status = BackgroundJobStatus.Running, MaxAttempts = 5, AvailableAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        }, CancellationToken.None);
    }

    private static async Task ConfirmAsync(Guid tenantId, Guid orderId, Guid paymentId)
    {
        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var payment = await db.Payments.FirstAsync(current => current.Id == paymentId && current.TenantId == tenantId);
        var payments = new PaymentService(db, new CommerceEventRecorder(db));
        if (!await payments.TryTransitionAsync(payment, PaymentStatus.Paid, PaymentTransitionSource.Webhook, CancellationToken.None))
        {
            await transaction.RollbackAsync();
            return;
        }

        var changed = await db.Orders
            .Where(current => current.Id == orderId && current.TenantId == tenantId && current.Status == OrderStatus.AwaitingPayment)
            .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.Status, OrderStatus.Paid));
        if (changed != 1)
        {
            await transaction.RollbackAsync();
            return;
        }

        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = orderId,
            FromStatus = OrderStatus.AwaitingPayment, ToStatus = OrderStatus.Paid, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task<(Guid TenantId, Guid OrderId, Guid PaymentId, Guid VariantId)> SeedAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Expiration concurrency", Slug = $"expiration-concurrency-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Expiration product", ProductNumber = $"PRD-{Guid.NewGuid():N}",
            Sku = $"EXP-{Guid.NewGuid():N}", Price = 1000, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, Name = "Default", Sku = $"EXP-VAR-{Guid.NewGuid():N}",
            StockQuantity = 5, ReservedStock = 2, IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var item = new OrderItem
        {
            Id = Guid.NewGuid(), ProductId = product.Id, VariantId = variant.Id, ProductName = product.Name,
            Sku = variant.Sku, Quantity = 2, UnitPrice = 1000, LineTotal = 2000,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Number = $"ORD-{Guid.NewGuid():N}", IdempotencyKey = $"expiration-{Guid.NewGuid():N}",
            Status = OrderStatus.AwaitingPayment, Total = 2000, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1), Items = [item],
        };
        var reservation = new StockReservation
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, OrderItemId = item.Id, ProductVariantId = variant.Id,
            Quantity = 2, Status = InventoryReservationStatus.Reserved, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, Provider = "mock", ProviderReference = $"mock-{order.Id:N}",
            PublicReference = PaymentReferenceGenerator.Generate(), IdempotencyKey = $"payment-{order.Id:N}", Status = PaymentStatus.Pending,
            Amount = order.Total, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        db.AddRange(tenant, product, variant, order, reservation, payment);
        await db.SaveChangesAsync();
        return (tenant.Id, order.Id, payment.Id, variant.Id);
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}