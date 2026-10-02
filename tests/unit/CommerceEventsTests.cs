using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;
using Xunit;

namespace Vanta.Api.UnitTests;

// Covers the Commerce Events log: every event carries tenant/type/timestamp plus the relevant
// entity ids, only non-sensitive metadata is stored, and the timeline endpoint can reconstruct
// conversation -> recommendation -> cart -> order -> payment. No distributed bus is involved —
// events are plain rows written in the same VantaDbContext/transaction as the triggering operation.
public class CommerceEventsTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task New_conversation_emits_conversation_started()
    {
        var auth = await RegisterAsync("ce-conv-started");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-001");

        var events = await ListEventsAsync(auth.AccessToken, conversationId: conversationId);

        Assert.Contains(events, e => e.Type == CommerceEventType.ConversationStarted && e.ConversationId == conversationId);
    }

    [Fact]
    public async Task Cart_and_add_to_cart_tools_emit_matching_events()
    {
        var auth = await RegisterAsync("ce-cart");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-cart-001");
        var product = await CreateProductAsync(auth.AccessToken, "Producto Carrito", 50000);
        await CreateVariantAsync(auth.AccessToken, product.Id, "Único", 10);

        using var scope = factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();

        await tools.CreateCartAsync(auth.TenantId, conversationId, CancellationToken.None);

        var added = await tools.AddToCartAsync(auth.TenantId, conversationId, product.Id, 2, string.Empty, CancellationToken.None);
        Assert.NotNull(added);

        var events = await ListEventsAsync(auth.AccessToken, conversationId: conversationId);

        Assert.Contains(events, e => e.Type == CommerceEventType.CartCreated && e.ConversationId == conversationId);
        var addedEvent = Assert.Single(events, e => e.Type == CommerceEventType.ProductAddedToCart);
        Assert.Equal(product.Id, addedEvent.ProductId);
        Assert.NotNull(addedEvent.CartId);
    }

    [Fact]
    public async Task Checkout_emits_checkout_started_order_created_and_payment_started_in_order_with_matching_ids()
    {
        var auth = await RegisterAsync("ce-checkout");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-checkout-001");
        var product = await CreateProductAsync(auth.AccessToken, "Producto Checkout", 75000);
        await CreateVariantAsync(auth.AccessToken, product.Id, "Único", 10);

        using var scope = factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();

        await tools.CreateCartAsync(auth.TenantId, conversationId, CancellationToken.None);
        await tools.AddToCartAsync(auth.TenantId, conversationId, product.Id, 1, string.Empty, CancellationToken.None);
        var order = await tools.CheckoutCartAsync(auth.TenantId, conversationId, $"ce-checkout-{Guid.NewGuid():N}", CancellationToken.None);
        Assert.NotNull(order);

        var events = await ListEventsAsync(auth.AccessToken, conversationId: conversationId);
        var checkoutStarted = Assert.Single(events, e => e.Type == CommerceEventType.CheckoutStarted);
        var orderCreated = Assert.Single(events, e => e.Type == CommerceEventType.OrderCreated);
        var paymentStarted = Assert.Single(events, e => e.Type == CommerceEventType.PaymentStarted);

        Assert.Equal(order!.Id, orderCreated.OrderId);
        Assert.Equal(order.Id, paymentStarted.OrderId);
        Assert.NotNull(paymentStarted.PaymentId);

        // Reconstruct chronological order: CheckoutStarted -> OrderCreated -> PaymentStarted.
        Assert.True(checkoutStarted.CreatedAt <= orderCreated.CreatedAt);
        Assert.True(orderCreated.CreatedAt <= paymentStarted.CreatedAt);
    }

    [Fact]
    public async Task Confirming_a_payment_emits_payment_confirmed()
    {
        var (client, auth, orderId, paymentId, conversationId) = await CreateOrderAwaitingPaymentAsync("ce-payconfirm");
        _ = conversationId;

        using var confirm = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/mock-confirm");
        confirm.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        (await client.SendAsync(confirm)).EnsureSuccessStatusCode();

        var events = await ListEventsAsync(auth.AccessToken, orderId: orderId);
        var confirmed = Assert.Single(events, e => e.Type == CommerceEventType.PaymentConfirmed);
        Assert.Equal(paymentId, confirmed.PaymentId);
        Assert.Equal(orderId, confirmed.OrderId);
    }

    [Fact]
    public async Task Order_cancelled_and_delivered_transitions_emit_matching_events()
    {
        var auth = await RegisterAsync("ce-order-status");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-status-001");
        var product = await CreateProductAsync(auth.AccessToken, "Producto Estado", 40000);

        var client = factory.CreateClient();
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id, conversationId);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Cancelled);

        var cancelledEvents = await ListEventsAsync(auth.AccessToken, orderId: order.Id);
        Assert.Contains(cancelledEvents, e => e.Type == CommerceEventType.OrderCancelled && e.OrderId == order.Id);

        // Separate order to verify the Delivered path independently of the cancelled one.
        var conversationId2 = await SendInboundAsync(accountExternalId, "customer-ce-status-002");
        var order2 = await CreateOrderAsync(client, auth.AccessToken, product.Id, conversationId2);
        await UpdateStatusAsync(client, auth.AccessToken, order2.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order2.Id);
        await ConfirmPaymentAsync(client, auth.AccessToken, payment.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order2.Id, OrderStatus.Preparing);
        await UpdateStatusAsync(client, auth.AccessToken, order2.Id, OrderStatus.Shipped);
        await UpdateStatusAsync(client, auth.AccessToken, order2.Id, OrderStatus.Delivered);

        var deliveredEvents = await ListEventsAsync(auth.AccessToken, orderId: order2.Id);
        Assert.Contains(deliveredEvents, e => e.Type == CommerceEventType.OrderDelivered && e.OrderId == order2.Id);
    }

    [Fact]
    public async Task Human_handoff_tool_emits_human_handoff_event()
    {
        var auth = await RegisterAsync("ce-handoff");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-handoff-001");

        using var scope = factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<AgentToolExecutor>();
        var result = await executor.ExecuteAsync(auth.TenantId, conversationId, AgentToolNames.HandoffToHuman, Args(new { }), CancellationToken.None);
        Assert.True(result.Success);

        var events = await ListEventsAsync(auth.AccessToken, conversationId: conversationId);
        Assert.Contains(events, e => e.Type == CommerceEventType.HumanHandoff && e.ConversationId == conversationId);
    }

    [Fact]
    public async Task Conversation_timeline_reconstructs_the_full_journey_in_chronological_order()
    {
        var auth = await RegisterAsync("ce-timeline");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-timeline-001");
        var product = await CreateProductAsync(auth.AccessToken, "Producto Timeline", 60000);
        await CreateVariantAsync(auth.AccessToken, product.Id, "Único", 10);

        using var scope = factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();
        await tools.CreateCartAsync(auth.TenantId, conversationId, CancellationToken.None);
        await tools.AddToCartAsync(auth.TenantId, conversationId, product.Id, 1, string.Empty, CancellationToken.None);
        await tools.CheckoutCartAsync(auth.TenantId, conversationId, $"ce-timeline-{Guid.NewGuid():N}", CancellationToken.None);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/analytics/conversations/{conversationId}/timeline");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var timeline = (await response.Content.ReadFromJsonAsync<List<CommerceEventResponse>>())!;

        Assert.True(timeline.Count >= 4);
        for (var i = 1; i < timeline.Count; i++)
        {
            Assert.True(timeline[i - 1].CreatedAt <= timeline[i].CreatedAt);
        }
        var types = timeline.Select(e => e.Type).ToList();
        Assert.Contains(CommerceEventType.ConversationStarted, types);
        Assert.Contains(CommerceEventType.CartCreated, types);
        Assert.Contains(CommerceEventType.ProductAddedToCart, types);
        Assert.Contains(CommerceEventType.CheckoutStarted, types);
        Assert.Contains(CommerceEventType.OrderCreated, types);
        Assert.Contains(CommerceEventType.PaymentStarted, types);
    }

    [Fact]
    public async Task Tenant_cannot_see_another_tenants_commerce_events_or_timeline()
    {
        var tenantA = await RegisterAsync("ce-isolation-a");
        var tenantB = await RegisterAsync("ce-isolation-b");
        var accountExternalId = await ConnectAsync(tenantA.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, "customer-ce-isolation-001");

        var eventsFromB = await ListEventsAsync(tenantB.AccessToken, conversationId: conversationId);
        Assert.Empty(eventsFromB);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/analytics/conversations/{conversationId}/timeline");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.AccessToken);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Metadata_never_contains_customer_pii()
    {
        var auth = await RegisterAsync("ce-metadata-safety");
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var customerExternalId = "595987654321";
        var conversationId = await SendInboundAsync(accountExternalId, customerExternalId);
        var product = await CreateProductAsync(auth.AccessToken, "Producto Metadata", 45000);
        await CreateVariantAsync(auth.AccessToken, product.Id, "Único", 10);

        using var scope = factory.Services.CreateScope();
        var tools = scope.ServiceProvider.GetRequiredService<IAgentTools>();
        await tools.CreateCartAsync(auth.TenantId, conversationId, CancellationToken.None);
        await tools.AddToCartAsync(auth.TenantId, conversationId, product.Id, 1, string.Empty, CancellationToken.None);
        await tools.CheckoutCartAsync(auth.TenantId, conversationId, $"ce-metadata-{Guid.NewGuid():N}", CancellationToken.None);

        var events = await ListEventsAsync(auth.AccessToken, conversationId: conversationId);

        foreach (var e in events)
        {
            if (e.Metadata is null) continue;
            Assert.DoesNotContain(customerExternalId, e.Metadata);
            Assert.True(e.Metadata.Length <= 1000);
        }
    }

    private async Task<(HttpClient Client, AuthResponse Auth, Guid OrderId, Guid PaymentId, Guid ConversationId)> CreateOrderAwaitingPaymentAsync(string slug)
    {
        var auth = await RegisterAsync(slug);
        var accountExternalId = await ConnectAsync(auth.AccessToken);
        var conversationId = await SendInboundAsync(accountExternalId, $"customer-{slug}");
        var product = await CreateProductAsync(auth.AccessToken, $"Producto {slug}", 55000);

        var client = factory.CreateClient();
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id, conversationId);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order.Id);
        return (client, auth, order.Id, payment.Id, conversationId);
    }

    private async Task<List<CommerceEventResponse>> ListEventsAsync(string token, Guid? conversationId = null, Guid? orderId = null)
    {
        using var client = factory.CreateClient();
        var query = new List<string>();
        if (conversationId.HasValue) query.Add($"conversationId={conversationId}");
        if (orderId.HasValue) query.Add($"orderId={orderId}");
        var path = "/api/analytics/commerce-events" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CommerceEventResponse>>())!;
    }

    private async Task<AuthResponse> RegisterAsync(string slug)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"Vanta {slug}", $"{slug}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> ConnectAsync(string token)
    {
        var accountExternalId = $"acc-{Guid.NewGuid():N}";
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId = accountExternalId, displayName = "Mock Channel" });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        return accountExternalId;
    }

    private async Task<Guid> SendInboundAsync(string accountExternalId, string customerExternalId)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new
        {
            eventId = $"event-{Guid.NewGuid():N}",
            accountExternalId,
            customerExternalId,
            messageId = $"msg-{Guid.NewGuid():N}",
            text = "Hola",
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MockWebhookResponse>();
        return body!.ConversationId;
    }

    private async Task<ProductResponse> CreateProductAsync(string token, string name, decimal price)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku = $"SKU-{Guid.NewGuid():N}", price });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private async Task CreateVariantAsync(string token, Guid productId, string name, int stockQuantity)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku = $"VAR-{Guid.NewGuid():N}", stockQuantity, priceOverride = (decimal?)null });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"ce-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } }, conversationId });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task UpdateStatusAsync(HttpClient client, string token, Guid orderId, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<PaymentResponse> CreatePaymentAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"ce-payment-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
    }

    private static async Task ConfirmPaymentAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/mock-confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed record MockWebhookResponse(bool Processed, bool Duplicate, Guid ConversationId);
}
