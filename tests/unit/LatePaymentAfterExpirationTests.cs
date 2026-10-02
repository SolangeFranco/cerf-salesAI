using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vanta.Api.Data;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class LatePaymentAfterExpirationTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Paid_webhook_after_expiration_records_exception_without_reopening_order()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var providerReference = $"mock-late-{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<VantaDbContext>();
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "Late payment tenant", Slug = $"late-payment-{Guid.NewGuid():N}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.Orders.Add(new Order
            {
                Id = orderId, TenantId = tenantId, Number = $"ORD-{Guid.NewGuid():N}",
                IdempotencyKey = $"late-order-{Guid.NewGuid():N}", Status = OrderStatus.Cancelled,
                Total = 1000, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
                ReservationExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            });
            db.Payments.Add(new Payment
            {
                Id = paymentId, TenantId = tenantId, OrderId = orderId, Provider = "mock",
                ProviderReference = providerReference, PublicReference = PaymentReferenceGenerator.Generate(),
                IdempotencyKey = $"late-payment-{Guid.NewGuid():N}", Status = PaymentStatus.Expired,
                Amount = 1000, CreatedAt = DateTimeOffset.UtcNow.AddHours(-1), UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new
        {
            eventId = $"late-event-{Guid.NewGuid():N}",
            providerReference,
            status = PaymentStatus.Paid,
        });

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<VantaDbContext>();
        var payment = await verify.Payments.AsNoTracking().SingleAsync(current => current.Id == paymentId);
        var order = await verify.Orders.AsNoTracking().SingleAsync(current => current.Id == orderId);
        Assert.Equal(PaymentStatus.PaidAfterExpiration, payment.Status);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Single(await verify.PaymentStatusHistory.Where(history => history.PaymentId == paymentId && history.ToStatus == PaymentStatus.PaidAfterExpiration).ToListAsync());
    }
}