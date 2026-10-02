using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class AiRevenueAttributionTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Direct_ai_conversion_is_attributed_once()
    {
        var auth = await RegisterAsync("attr-direct");
        var orderId = await SeedOrderAsync(auth.TenantId, OrderStatus.Paid, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "ai_tool"));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(100000m, result.AiAttributedRevenue);
        Assert.Equal(1, result.AiCreatedOrders);
        Assert.Equal(0, result.AiAssistedOrders);
        Assert.Equal(orderId, Assert.Single(await GetAttributedOrdersAsync(auth.TenantId)).OrderId);
    }

    [Fact]
    public async Task Human_assisted_conversion_is_attributed_when_ai_helped_before_order()
    {
        var auth = await RegisterAsync("attr-assisted");
        var conversationId = Guid.NewGuid();
        await SeedOrderAsync(auth.TenantId, OrderStatus.Paid, PaymentStatus.Paid,
            (CommerceEventType.ProductRecommended, conversationId, null),
            (CommerceEventType.HumanHandoff, conversationId, null),
            (CommerceEventType.OrderCreated, conversationId, "merchant_api"));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(100000m, result.AiAttributedRevenue);
        Assert.Equal(0, result.AiCreatedOrders);
        Assert.Equal(1, result.AiAssistedOrders);
    }

    [Fact]
    public async Task Normal_order_without_ai_events_is_not_attributed()
    {
        var auth = await RegisterAsync("attr-normal");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Paid, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "merchant_api"));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(100000m, result.TotalRevenue);
        Assert.Equal(1, result.PaidOrders);
        Assert.Equal(0m, result.AiAttributedRevenue);
        Assert.False(result.HasAttributionData);
    }

    [Fact]
    public async Task Abandoned_cart_recovery_is_counted_only_with_real_recovery_event()
    {
        var auth = await RegisterAsync("attr-recovered");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Delivered, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "ai_tool"),
            (CommerceEventType.AbandonedCartRecovered, null, null));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(100000m, result.AbandonedCartRecoveredRevenue);
        Assert.Equal(1, result.AbandonedCartRecoveredOrders);
    }

    [Fact]
    public async Task Duplicate_events_do_not_double_count_an_order()
    {
        var auth = await RegisterAsync("attr-duplicates");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Paid, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "ai_tool"),
            (CommerceEventType.OrderCreated, null, "ai_tool"),
            (CommerceEventType.AbandonedCartRecovered, null, null),
            (CommerceEventType.AbandonedCartRecovered, null, null));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(100000m, result.AiAttributedRevenue);
        Assert.Equal(1, result.AiCreatedOrders);
        Assert.Equal(100000m, result.AbandonedCartRecoveredRevenue);
        Assert.Equal(1, result.AbandonedCartRecoveredOrders);
    }

    [Fact]
    public async Task Cancelled_order_is_excluded_even_when_ai_created()
    {
        var auth = await RegisterAsync("attr-cancelled");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Cancelled, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "ai_tool"));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(0m, result.TotalRevenue);
        Assert.Equal(0, result.PaidOrders);
        Assert.Equal(0m, result.AiAttributedRevenue);
    }

    [Fact]
    public async Task Refunded_order_is_excluded_even_when_it_was_ai_created()
    {
        var auth = await RegisterAsync("attr-refunded");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Refunded, PaymentStatus.Refunded,
            (CommerceEventType.OrderCreated, null, "ai_tool"));

        var result = await GetSummaryAsync(auth.TenantId);

        Assert.Equal(0m, result.TotalRevenue);
        Assert.Equal(0, result.PaidOrders);
        Assert.Equal(0m, result.AiAttributedRevenue);
    }

    [Fact]
    public async Task Analytics_and_dashboard_summary_return_the_persisted_attribution()
    {
        var auth = await RegisterAsync("attr-api");
        await SeedOrderAsync(auth.TenantId, OrderStatus.Delivered, PaymentStatus.Paid,
            (CommerceEventType.OrderCreated, null, "ai_tool"));

        using var client = factory.CreateClient();
        var analytics = await GetAuthorizedAsync<AiRevenueAttributionResponse>(client, auth.AccessToken, "/api/analytics/revenue-attribution");
        var dashboard = await GetAuthorizedAsync<DashboardSummaryResponse>(client, auth.AccessToken, "/api/tenants/summary");

        Assert.Equal(100000m, analytics.AiAttributedRevenue);
        Assert.Equal(analytics.AiAttributedRevenue, dashboard.AiAttributedRevenue);
        Assert.True(dashboard.HasAttributionData);
    }

    private async Task<AiRevenueAttributionResponse> GetSummaryAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRevenueAttributionService>();
        return await service.GetSummaryAsync(tenantId, CancellationToken.None);
    }

    private static async Task<T> GetAuthorizedAsync<T>(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<IReadOnlyList<AttributedOrder>> GetAttributedOrdersAsync(Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AiRevenueAttributionService>();
        return await service.GetAttributedOrdersAsync(tenantId, CancellationToken.None);
    }

    private async Task<Guid> SeedOrderAsync(
        Guid tenantId,
        string orderStatus,
        string paymentStatus,
        params (string Type, Guid? ConversationId, string? Source)[] eventDefinitions)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VantaDbContext>();
        var orderId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(2);
        var conversationId = eventDefinitions.Select(definition => definition.ConversationId).FirstOrDefault(id => id.HasValue);
        db.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            ConversationId = conversationId,
            Number = $"ORD-ATTR-{orderId:N}",
            IdempotencyKey = $"attr-{orderId:N}",
            Status = orderStatus,
            Total = 100000m,
            CreatedAt = createdAt,
        });
        db.Payments.Add(new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = "mock",
            ProviderReference = $"provider-{orderId:N}",
            PublicReference = $"public-{orderId:N}",
            IdempotencyKey = $"payment-{orderId:N}",
            Status = paymentStatus,
            Amount = 100000m,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        });

        var eventIndex = 0;
        foreach (var definition in eventDefinitions)
        {
            var metadata = definition.Source is null ? null : $"{{\"source\":\"{definition.Source}\"}}";
            db.CommerceEvents.Add(new CommerceEvent
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Type = definition.Type,
                CreatedAt = createdAt.AddMinutes(-2 + eventIndex++),
                ConversationId = definition.ConversationId,
                OrderId = orderId,
                Metadata = metadata,
            });
        }

        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<AuthResponse> RegisterAsync(string slug)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"Vanta {slug}", $"{slug}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private sealed record DashboardSummaryResponse(
        int Products,
        int Orders,
        int Conversations,
        decimal TotalRevenue,
        int PaidOrders,
        decimal AiAttributedRevenue,
        int AiCreatedOrders,
        int AiAssistedOrders,
        decimal AbandonedCartRecoveredRevenue,
        int AbandonedCartRecoveredOrders,
        bool HasAttributionData);
}
