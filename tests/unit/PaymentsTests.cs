using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public class PaymentsTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Mock_payment_is_idempotent_and_can_be_confirmed()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Pagos Demo", "payments-demo@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateOrderStatusAsync(client, auth.AccessToken, order.Id, "Confirmed");

        var first = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "payment-test-001");
        var retry = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "payment-test-001");
        var payment = (await first.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var confirm = await ConfirmPaymentAsync(client, auth.AccessToken, payment.Id);

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(payment.Id, (await retry.Content.ReadFromJsonAsync<PaymentResponse>())!.Id);
        Assert.Equal(System.Net.HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(PaymentStatus.Paid, (await confirm.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);

        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var orderDetail = await client.SendAsync(orderRequest);
        orderDetail.EnsureSuccessStatusCode();
        var detail = await orderDetail.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.Equal(OrderStatus.Paid, detail!.Status);
        Assert.Equal(OrderStatus.Paid, detail.StatusHistory[^1].ToStatus);
    }

    [Fact]
    public async Task Payment_requires_confirmed_order()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Pagos Validacion", "payments-validation@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);

        var response = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "payment-test-002");

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Manual_transfer_stays_pending_until_merchant_confirms_it()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Transferencia Manual", $"manual-transfer-{Guid.NewGuid():N}@vanta.local");
        await UpdateBankSettingsAsync(client, auth.AccessToken);
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateOrderStatusAsync(client, auth.AccessToken, order.Id, "Confirmed");

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/payments/manual-transfer");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        createRequest.Headers.Add("Idempotency-Key", $"manual-{Guid.NewGuid():N}");
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        var transfer = (await createResponse.Content.ReadFromJsonAsync<ManualTransferPaymentResponse>())!;

        Assert.Equal("manual-transfer", transfer.Payment.Provider);
        Assert.Equal(PaymentStatus.Pending, transfer.Payment.Status);
        Assert.Equal("Banco Demo", transfer.BankName);
        Assert.Equal("Cuenta Demo", transfer.AccountNumber);

        using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/payments/manual-transfer");
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        retryRequest.Headers.Add("Idempotency-Key", createRequest.Headers.GetValues("Idempotency-Key").Single());
        var retryResponse = await client.SendAsync(retryRequest);
        retryResponse.EnsureSuccessStatusCode();
        var retry = (await retryResponse.Content.ReadFromJsonAsync<ManualTransferPaymentResponse>())!;
        Assert.Equal(transfer.Payment.Id, retry.Payment.Id);
        Assert.Equal("Banco Demo", retry.BankName);

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{transfer.Payment.Id}/manual-confirm");
        confirmRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var confirmResponse = await client.SendAsync(confirmRequest);
        confirmResponse.EnsureSuccessStatusCode();
        Assert.Equal(PaymentStatus.Paid, (await confirmResponse.Content.ReadFromJsonAsync<PaymentResponse>())!.Status);
    }

    [Fact]
    public async Task Signed_mock_webhook_is_idempotent_and_marks_order_paid()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Webhook Demo", "payments-webhook@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateOrderStatusAsync(client, auth.AccessToken, order.Id, "Confirmed");
        var paymentResponse = await CreatePaymentAsync(client, auth.AccessToken, order.Id, "webhook-payment-001");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        var first = await SendWebhookAsync(client, payment.ProviderReference, "webhook-event-001");
        var duplicate = await SendWebhookAsync(client, payment.ProviderReference, "webhook-event-001");

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("duplicate", await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Payment_webhook_derives_tenant_from_payment_not_request_body()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync(client, "Webhook Tenant A", $"webhook-a-{Guid.NewGuid():N}@vanta.local");
        var tenantB = await RegisterAsync(client, "Webhook Tenant B", $"webhook-b-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, tenantA.AccessToken);
        var order = await CreateOrderAsync(client, tenantA.AccessToken, product.Id);
        await UpdateOrderStatusAsync(client, tenantA.AccessToken, order.Id, "Confirmed");
        var paymentResponse = await CreatePaymentAsync(client, tenantA.AccessToken, order.Id, $"webhook-cross-tenant-{Guid.NewGuid():N}");
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new
        {
            eventId = $"cross-tenant-event-{Guid.NewGuid():N}",
            providerReference = payment.ProviderReference,
            status = PaymentStatus.Paid,
            tenantId = tenantB.TenantId,
        });

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        using var orderRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantA.AccessToken);
        var orderDetail = await client.SendAsync(orderRequest);
        var detail = await orderDetail.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.Equal(OrderStatus.Paid, detail!.Status);

        using var crossTenantRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        crossTenantRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.AccessToken);
        var crossTenantResponse = await client.SendAsync(crossTenantRequest);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Public_ecommerce_paid_order_queues_whatsapp_receipt()
    {
        var client = factory.CreateClient();
        var tenantName = $"Recibo Ecommerce {Guid.NewGuid():N}";
        var auth = await RegisterAsync(client, tenantName, $"receipt-{Guid.NewGuid():N}@vanta.local");
        await ConnectAsync(client, auth.AccessToken, $"receipt-channel-{Guid.NewGuid():N}");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var slug = TenantSlugGenerator.FromName(tenantName);

        var publicOrder = await CreatePublicOrderAsync(client, slug, product.Id);
        var providerReference = await GetProviderReferenceAsync(client, auth.AccessToken, publicOrder.OrderNumber);
        var first = await SendWebhookAsync(client, providerReference, $"receipt-event-{Guid.NewGuid():N}");

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);

        var conversations = await ListConversationsAsync(client, auth.AccessToken);
        var conversation = Assert.Single(conversations, current => current.CustomerExternalId == "+595981555123");
        var messages = await ListMessagesAsync(client, auth.AccessToken, conversation.Id);
        Assert.Contains(messages, message => message.Direction == "Outbound" && message.Text.Contains("Recibo VANTA", StringComparison.OrdinalIgnoreCase) && message.Text.Contains(publicOrder.OrderNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task UpdateBankSettingsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/tenants/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "Transferencia Manual",
            deliveryBaseCostGs = 0,
            deliveryIncludedDistanceKm = 1,
            deliveryExtraCostPerKmGs = 0,
            metropolitanDeliveryCostGs = 0,
            interiorDeliveryCostGs = 0,
            bankName = "Banco Demo",
            bankBeneficiaryName = "Beneficiario Demo",
            bankAccountNumber = "Cuenta Demo",
            bankDocumentNumber = "1234567",
            bankAlias = "alias.demo",
        });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Producto Pago", sku = $"PAY-{Guid.NewGuid():N}", price = 15000 });
        var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"pay-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } });
        var response = await client.SendAsync(request); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task<HttpResponseMessage> UpdateOrderStatusAsync(HttpClient client, string token, Guid orderId, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status });
        var response = await client.SendAsync(request); response.EnsureSuccessStatusCode(); return response;
    }

    private static async Task<HttpResponseMessage> CreatePaymentAsync(HttpClient client, string token, Guid orderId, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> ConfirmPaymentAsync(HttpClient client, string token, Guid paymentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/payments/{paymentId}/mock-confirm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(HttpClient client, string providerReference, string eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/payments/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { providerReference, eventId, status = PaymentStatus.Paid });
        return await client.SendAsync(request);
    }

    private static async Task ConnectAsync(HttpClient client, string token, string externalAccountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId, displayName = "Receipt Channel" });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<PublicOrderCreatedResponse> CreatePublicOrderAsync(HttpClient client, string slug, Guid productId)
    {
        var response = await client.PostAsJsonAsync($"/api/public/store/{slug}/orders", new
        {
            name = "Cliente Recibo",
            phone = "+595981555123",
            email = "cliente.recibo@example.com",
            address = "Av. España 123, Asunción",
            items = new[] { new { productId, variantId = (Guid?)null, quantity = 1 } },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PublicOrderCreatedResponse>())!;
    }

    // The public checkout response only ever exposes the opaque PublicReference (by design, see
    // ADR on opaque payment references), so the webhook's real ProviderReference has to be read
    // back through the merchant-authenticated order detail endpoint instead.
    private static async Task<string> GetProviderReferenceAsync(HttpClient client, string token, string orderNumber)
    {
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var orders = (await listResponse.Content.ReadFromJsonAsync<List<OrderResponse>>())!;
        var orderId = orders.Single(order => order.Number == orderNumber).Id;

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();
        var detail = (await detailResponse.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
        return detail.Payment!.ProviderReference;
    }

    private static async Task<List<ConversationResponse>> ListConversationsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ConversationResponse>>())!;
    }

    private static async Task<List<ConversationMessageResponse>> ListMessagesAsync(HttpClient client, string token, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ConversationMessageResponse>>())!;
    }

    private sealed record PublicOrderCreatedResponse(string OrderNumber, string PaymentReference, string CheckoutUrl, decimal Total);
    private sealed record ConversationResponse(Guid Id, string CustomerExternalId, string Status, DateTimeOffset LastMessageAt);
    private sealed record ConversationMessageResponse(Guid Id, string Direction, string Text, string? ImageBase64, string? ImageMediaType, string? DeliveryStatus, int DeliveryAttempts, string? DeliveryError, DateTimeOffset CreatedAt);
}
