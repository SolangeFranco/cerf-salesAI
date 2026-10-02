using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class OrderExpirationTests
{
    [Fact]
    public void Reservation_expiration_uses_the_configured_ttl()
    {
        var createdAt = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        var expiresAt = OrderReservationPolicy.ExpiresAt(createdAt, new OrderReservationOptions { ExpirationMinutes = 45 });

        Assert.Equal(createdAt.AddMinutes(45), expiresAt);
    }

    [Fact]
    public async Task Expired_order_is_enqueued_only_once()
    {
        await using var db = CreateDb();
        var (order, _, _) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Pending, DateTimeOffset.UtcNow.AddMinutes(-1));
        var expiration = CreateService(db);
        var jobs = new BackgroundJobService(db, NullLogger<BackgroundJobService>.Instance);

        await expiration.EnqueueExpiredAsync(jobs, CancellationToken.None);
        await expiration.EnqueueExpiredAsync(jobs, CancellationToken.None);

        var job = Assert.Single(await db.BackgroundJobs.ToListAsync());
        Assert.Equal(order.TenantId, job.TenantId);
        Assert.Equal(BackgroundJobType.OrderExpiration, job.Type);
        Assert.Equal($"order-expiration-{order.Id:N}", job.IdempotencyKey);
    }

    [Fact]
    public async Task Expired_order_cancels_payment_and_releases_inventory_once()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Pending, DateTimeOffset.UtcNow.AddMinutes(-1));
        var service = CreateService(db);
        var job = Job(order);

        await service.ExpireAsync(job, CancellationToken.None);
        await service.ExpireAsync(job, CancellationToken.None);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(PaymentStatus.Expired, payment.Status);
        Assert.Equal(0, variant.ReservedStock);
        Assert.Single(await db.InventoryMovements.Where(movement => movement.Type == InventoryMovementType.Release).ToListAsync());
        Assert.Single(await db.OrderStatusHistory.Where(history => history.ToStatus == OrderStatus.Cancelled).ToListAsync());
        Assert.Single(await db.PaymentStatusHistory.Where(history => history.ToStatus == PaymentStatus.Expired).ToListAsync());
    }

    [Fact]
    public async Task Future_reservation_is_not_expired()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Pending, DateTimeOffset.UtcNow.AddMinutes(10));

        await CreateService(db).ExpireAsync(Job(order), CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(2, variant.ReservedStock);
    }

    [Fact]
    public async Task Paid_order_is_never_expired()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.Paid, PaymentStatus.Paid, DateTimeOffset.UtcNow.AddMinutes(-1));

        await CreateService(db).ExpireAsync(Job(order), CancellationToken.None);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(2, variant.ReservedStock);
    }

    [Fact]
    public async Task Job_from_another_tenant_cannot_expire_the_order()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Pending, DateTimeOffset.UtcNow.AddMinutes(-1));
        var originalJob = Job(order);
        var job = new BackgroundJob
        {
            Id = originalJob.Id,
            TenantId = Guid.NewGuid(),
            Type = originalJob.Type,
            IdempotencyKey = originalJob.IdempotencyKey,
            Payload = originalJob.Payload,
            Status = originalJob.Status,
            MaxAttempts = originalJob.MaxAttempts,
            AvailableAt = originalJob.AvailableAt,
            CreatedAt = originalJob.CreatedAt,
        };

        await CreateService(db).ExpireAsync(job, CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(2, variant.ReservedStock);
        Assert.Empty(await db.InventoryMovements.Where(movement => movement.Type == InventoryMovementType.Release).ToListAsync());
    }

    [Fact]
    public async Task Confirmed_payment_wins_over_expiration_before_order_transition()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Paid, DateTimeOffset.UtcNow.AddMinutes(-1));

        await CreateService(db).ExpireAsync(Job(order), CancellationToken.None);

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(2, variant.ReservedStock);
        Assert.Empty(await db.InventoryMovements.Where(movement => movement.Type == InventoryMovementType.Release).ToListAsync());
    }

    [Fact]
    public async Task Late_payment_is_recorded_without_reopening_order_or_inventory()
    {
        await using var db = CreateDb();
        var (order, payment, variant) = await SeedOrderAsync(db, OrderStatus.AwaitingPayment, PaymentStatus.Pending, DateTimeOffset.UtcNow.AddMinutes(-1));
        await CreateService(db).ExpireAsync(Job(order), CancellationToken.None);
        var payments = new PaymentService(db, new CommerceEventRecorder(db));

        var applied = await payments.TryTransitionAsync(payment, PaymentStatus.PaidAfterExpiration, PaymentTransitionSource.Webhook, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.True(applied);
        Assert.Equal(PaymentStatus.PaidAfterExpiration, payment.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(0, variant.ReservedStock);
        Assert.Single(await db.PaymentStatusHistory.Where(history => history.ToStatus == PaymentStatus.PaidAfterExpiration).ToListAsync());
    }

    private static OrderExpirationService CreateService(VantaDbContext db) =>
        new(db, new PaymentService(db, new CommerceEventRecorder(db)), new InventoryService(db));

    private static BackgroundJob Job(Order order) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = order.TenantId,
        Type = BackgroundJobType.OrderExpiration,
        IdempotencyKey = $"order-expiration-{order.Id:N}",
        Payload = JsonSerializer.Serialize(new { orderId = order.Id }),
        Status = BackgroundJobStatus.Running,
        MaxAttempts = 5,
        AvailableAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<(Order Order, Payment Payment, ProductVariant Variant)> SeedOrderAsync(
        VantaDbContext db,
        string orderStatus,
        string paymentStatus,
        DateTimeOffset expiresAt)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Expiration tenant", Slug = $"expiration-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Expiration product", ProductNumber = "PRD-EXP-001",
            Sku = $"EXP-{Guid.NewGuid():N}", Price = 1000, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var variant = new ProductVariant
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ProductId = product.Id, Name = "Default",
            Sku = $"EXP-VAR-{Guid.NewGuid():N}", StockQuantity = 5, ReservedStock = 2, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        var item = new OrderItem
        {
            Id = Guid.NewGuid(), ProductId = product.Id, VariantId = variant.Id, ProductName = product.Name,
            Sku = variant.Sku, Quantity = 2, UnitPrice = 1000, LineTotal = 2000,
        };
        var order = new Order
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, Number = "ORD-EXP-001", IdempotencyKey = $"exp-{Guid.NewGuid():N}",
            Status = orderStatus, Total = 2000, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            ReservationExpiresAt = expiresAt, Items = [item],
        };
        var reservation = new StockReservation
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, OrderItemId = item.Id,
            ProductVariantId = variant.Id, Quantity = 2, Status = InventoryReservationStatus.Reserved,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, Provider = "mock",
            ProviderReference = $"mock-{order.Id:N}", PublicReference = PaymentReferenceGenerator.Generate(),
            IdempotencyKey = $"payment-{order.Id:N}", Status = paymentStatus, Amount = order.Total,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), UpdatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };

        db.AddRange(tenant, product, variant, order, reservation, payment);
        await db.SaveChangesAsync();
        return (order, payment, variant);
    }

    private static VantaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<VantaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VantaDbContext(options);
    }
}