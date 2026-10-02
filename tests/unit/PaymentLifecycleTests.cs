using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class PaymentLifecycleTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Duplicate_payment_creation_with_same_idempotency_key_returns_the_same_payment()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Duplicate Creation", $"duplicate-creation-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);

        var first = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "dup-create-key-001");
        var second = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "dup-create-key-001");
        var firstBody = (await first.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var secondBody = (await second.Content.ReadFromJsonAsync<PaymentResponse>())!;

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody.Id, secondBody.Id);
        Assert.Equal(firstBody.PublicReference, secondBody.PublicReference);
    }

    [Fact]
    public async Task Public_references_are_opaque_and_unrelated_to_the_provider_reference()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Opaque Reference", $"opaque-reference-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);

        var response = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "opaque-reference-key-001");
        var payment = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;

        Assert.NotEqual(payment.ProviderReference, payment.PublicReference);
        Assert.DoesNotContain(order.Id.ToString("N"), payment.PublicReference, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(32, payment.PublicReference.Length);
    }

    [Fact]
    public async Task Server_to_server_verification_confirms_a_pending_payment()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Verify Confirm", $"verify-confirm-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "verify-key-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        var verifyResponse = await VerifyAsync(client, auth.AccessToken, payment.Id);
        var verified = await verifyResponse.Content.ReadFromJsonAsync<PaymentResponse>();
        var order2 = await GetOrderAsync(client, auth.AccessToken, order.Id);

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        Assert.Equal(PaymentStatus.Paid, verified!.Status);
        Assert.Equal(OrderStatus.Paid, order2.Status);
    }

    [Fact]
    public async Task Paid_order_can_be_refunded_and_order_transitions_to_refunded()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Refund Flow", $"refund-flow-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "refund-key-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;
        await ConfirmMockAsync(client, auth.AccessToken, payment.Id);

        var refundResponse = await RefundAsync(client, auth.AccessToken, payment.Id);
        var refunded = await refundResponse.Content.ReadFromJsonAsync<PaymentResponse>();
        var order2 = await GetOrderAsync(client, auth.AccessToken, order.Id);

        Assert.Equal(HttpStatusCode.OK, refundResponse.StatusCode);
        Assert.Equal(PaymentStatus.Refunded, refunded!.Status);
        Assert.Equal(OrderStatus.Refunded, order2.Status);
    }

    [Fact]
    public async Task Refunding_a_payment_that_was_never_paid_is_rejected()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Invalid Refund", $"invalid-refund-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "invalid-refund-key-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        var refundResponse = await RefundAsync(client, auth.AccessToken, payment.Id);

        Assert.Equal(HttpStatusCode.Conflict, refundResponse.StatusCode);
    }

    [Fact]
    public async Task Webhook_with_unsupported_status_is_rejected()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Invalid Webhook Status", $"invalid-webhook-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "invalid-status-key-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { providerReference = payment.ProviderReference, eventId = "invalid-status-event-001", status = "Refunded" });
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_with_invalid_signature_is_rejected()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "not-the-real-secret");
        request.Content = JsonContent.Create(new { providerReference = "mock-anything", eventId = "bad-signature-event-001", status = PaymentStatus.Paid });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_webhook_provider_is_rejected()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/bancard");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { providerReference = "anything", eventId = "unknown-provider-event-001", status = PaymentStatus.Paid });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Payment_endpoints_require_authentication()
    {
        var client = factory.CreateClient();
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        createRequest.Headers.Add("Idempotency-Key", "unauth-key-00000001");
        var createResponse = await client.SendAsync(createRequest);

        var verifyResponse = await client.PostAsync($"/api/payments/{paymentId}/verify", content: null);
        var refundResponse = await client.PostAsync($"/api/payments/{paymentId}/refund", content: null);
        var confirmResponse = await client.PostAsync($"/api/payments/{paymentId}/mock-confirm", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refundResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, confirmResponse.StatusCode);
    }

    [Fact]
    public async Task Tenant_cannot_verify_confirm_or_refund_another_tenants_payment()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync(client, "Payment Tenant A", $"payment-tenant-a-{Guid.NewGuid():N}@vanta.local");
        var tenantB = await RegisterAsync(client, "Payment Tenant B", $"payment-tenant-b-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, tenantA.AccessToken);
        var order = await CreateOrderAsync(client, tenantA.AccessToken, product.Id);
        await UpdateStatusAsync(client, tenantA.AccessToken, order.Id, OrderStatus.Confirmed);
        var paymentResponse = await CreatePaymentAsync(client, tenantA.AccessToken, order.Id, "cross-tenant-payment-key-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        var crossVerify = await VerifyAsync(client, tenantB.AccessToken, payment.Id);
        var crossRefund = await RefundAsync(client, tenantB.AccessToken, payment.Id);
        var crossConfirm = await ConfirmMockAsync(client, tenantB.AccessToken, payment.Id);
        var crossCreatePayment = await CreatePaymentAsync(client, tenantB.AccessToken, order.Id, "cross-tenant-create-key-001");

        Assert.Equal(HttpStatusCode.NotFound, crossVerify.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossRefund.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossCreatePayment.StatusCode);
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
        request.Content = JsonContent.Create(new { name = "Producto Pago Lifecycle", sku = $"PLC-{Guid.NewGuid():N}", price = 20000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"plc-order-{Guid.NewGuid():N}");
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
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<HttpResponseMessage> CreatePaymentAsync(HttpClient client, string token, Guid orderId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ConfirmMockAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/mock-confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> VerifyAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/verify");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RefundAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/refund");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
