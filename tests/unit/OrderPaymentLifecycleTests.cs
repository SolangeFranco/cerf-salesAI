using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class OrderPaymentLifecycleTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Manual_status_endpoint_cannot_skip_the_payment_flow()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Lifecycle Skip", $"lifecycle-skip-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);

        var toPaid = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Paid);
        var toAwaitingPayment = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.AwaitingPayment);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var toPreparingBeforePayment = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Preparing);

        Assert.Equal(HttpStatusCode.Conflict, toPaid.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, toAwaitingPayment.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, toPreparingBeforePayment.StatusCode);
    }

    [Fact]
    public async Task Requesting_payment_moves_order_to_awaiting_payment_and_blocks_duplicate_confirmed_requirement()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Lifecycle Await", $"lifecycle-await-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);

        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "await-payment-001");
        var detail = await GetOrderAsync(client, auth.AccessToken, order.Id);

        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, detail.Status);
        Assert.Equal(OrderStatus.AwaitingPayment, detail.StatusHistory[^1].ToStatus);
    }

    [Fact]
    public async Task Failed_webhook_moves_order_to_payment_failed_and_retry_returns_to_awaiting_payment()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Lifecycle Failed", $"lifecycle-failed-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "failed-payment-001");
        var paymentBody = (await payment.Content.ReadFromJsonAsync<PaymentResponse>())!;

        var webhook = await SendWebhookAsync(client, paymentBody.ProviderReference, PaymentStatus.Failed, "failed-event-001");
        var afterFailure = await GetOrderAsync(client, auth.AccessToken, order.Id);

        var retryPayment = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "retry-payment-001");
        var afterRetry = await GetOrderAsync(client, auth.AccessToken, order.Id);

        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal(OrderStatus.PaymentFailed, afterFailure.Status);
        Assert.Equal(HttpStatusCode.Created, retryPayment.StatusCode);
        Assert.Equal(OrderStatus.AwaitingPayment, afterRetry.Status);
    }

    [Fact]
    public async Task Delivered_order_can_be_refunded_but_not_re_delivered()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Lifecycle Refund", $"lifecycle-refund-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "refund-payment-001");
        var paymentBody = (await payment.Content.ReadFromJsonAsync<PaymentResponse>())!;
        await ConfirmPaymentAsync(client, auth.AccessToken, paymentBody.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Preparing);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Shipped);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Delivered);

        var refunded = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Refunded);
        var reDelivered = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Delivered);

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reDelivered.StatusCode);
    }

    [Fact]
    public async Task Tenant_cannot_change_status_of_another_tenants_order_across_lifecycle_states()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync(client, "Lifecycle Tenant A", $"lifecycle-a-{Guid.NewGuid():N}@vanta.local");
        var tenantB = await RegisterAsync(client, "Lifecycle Tenant B", $"lifecycle-b-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, tenantA.AccessToken);
        var order = await CreateOrderAsync(client, tenantA.AccessToken, product.Id);

        var crossTenantConfirm = await UpdateStatusAsync(client, tenantB.AccessToken, order.Id, OrderStatus.Confirmed);
        var crossTenantPayment = await CreatePaymentAsync(client, tenantB.AccessToken, order.Id, "cross-tenant-payment-001");

        Assert.Equal(HttpStatusCode.NotFound, crossTenantConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantPayment.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Producto Lifecycle", sku = $"LC-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"lifecycle-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task<HttpResponseMessage> UpdateStatusAsync(HttpClient client, string token, Guid orderId, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> CreatePaymentAsync(HttpClient client, string token, Guid orderId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task ConfirmPaymentAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/mock-confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string providerReference, string status, string eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { providerReference, status, eventId });
        return await client.SendAsync(request);
    }

    private static async Task<OrderDetailResponse> GetOrderAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
    }
}
