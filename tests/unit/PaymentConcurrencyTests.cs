using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class PaymentConcurrencyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Two_concurrent_confirmations_apply_the_transition_exactly_once()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Payment Concurrency {Guid.NewGuid():N}", Slug = $"payment-concurrency-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var now = DateTimeOffset.UtcNow;
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant.Id, Number = $"PAY-{Guid.NewGuid():N}", IdempotencyKey = $"PAY-KEY-{Guid.NewGuid():N}", Status = OrderStatus.AwaitingPayment, Total = 1000, CreatedAt = now };
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, Provider = "mock",
            ProviderReference = $"mock-{Guid.NewGuid():N}", PublicReference = PaymentReferenceGenerator.Generate(),
            IdempotencyKey = $"pay-idem-{Guid.NewGuid():N}", Status = PaymentStatus.Pending, Amount = 1000,
            CreatedAt = now, UpdatedAt = now,
        };
        setup.Tenants.Add(tenant);
        setup.Orders.Add(order);
        setup.Payments.Add(payment);
        await setup.SaveChangesAsync();

        var first = TryConfirmAsync(payment.Id, tenant.Id);
        var second = TryConfirmAsync(payment.Id, tenant.Id);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result));

        await using var verify = CreateContext();
        var persisted = await verify.Payments.SingleAsync(current => current.Id == payment.Id);
        Assert.Equal(PaymentStatus.Paid, persisted.Status);
        var historyCount = await verify.PaymentStatusHistory.CountAsync(history => history.PaymentId == payment.Id);
        Assert.Equal(1, historyCount);
    }

    private static async Task<bool> TryConfirmAsync(Guid paymentId, Guid tenantId)
    {
        await using var context = CreateContext();
        var payment = await context.Payments.FirstAsync(current => current.Id == paymentId && current.TenantId == tenantId);
        var service = new PaymentService(context, new CommerceEventRecorder(context));
        var applied = await service.TryTransitionAsync(payment, PaymentStatus.Paid, PaymentTransitionSource.Webhook, CancellationToken.None);
        if (applied) await context.SaveChangesAsync();
        return applied;
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}
