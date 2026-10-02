using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Xunit;

namespace Vanta.Api.UnitTests;

public class OrderLifecycleTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Paid_order_can_progress_to_delivered_and_notifies_conversation()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Lifecycle Demo", $"lifecycle-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var accountId = await ConnectAsync(client, auth.AccessToken);
        var conversationId = await ReceiveMessageAsync(client, accountId);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id, conversationId);

        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order.Id);
        await ConfirmPaymentAsync(client, auth.AccessToken, payment.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Preparing);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Shipped);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Delivered);

        var detail = await GetOrderAsync(client, auth.AccessToken, order.Id);
        Assert.Equal(OrderStatus.Delivered, detail.Status);
        Assert.Equal(
            new[] { OrderStatus.New, OrderStatus.Confirmed, OrderStatus.AwaitingPayment, OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Shipped, OrderStatus.Delivered },
            detail.StatusHistory.Select(history => history.ToStatus).ToArray());

        var messages = await GetMessagesAsync(client, auth.AccessToken, conversationId);
        Assert.Contains(messages, message => message.Text.Contains("en preparación", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(messages, message => message.Text.Contains("enviado", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(messages, message => message.Text.Contains("entregado", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string tenantName, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(tenantName, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Producto Lifecycle", sku = $"LIFE-{Guid.NewGuid():N}", price = 15000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<string> ConnectAsync(HttpClient client, string token)
    {
        var accountId = $"lifecycle-account-{Guid.NewGuid():N}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId = accountId, displayName = "Lifecycle Demo" });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
        return accountId;
    }

    private static async Task<Guid> ReceiveMessageAsync(HttpClient client, string accountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { eventId = Guid.NewGuid().ToString(), accountExternalId = accountId, customerExternalId = "595000000002", messageId = Guid.NewGuid().ToString(), text = "Hola" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MockWebhookResponse>())!.ConversationId;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"lifecycle-order-{Guid.NewGuid():N}");
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
        request.Headers.Add("Idempotency-Key", $"lifecycle-payment-{Guid.NewGuid():N}");
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

    private static async Task<OrderDetailResponse> GetOrderAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
    }

    private static async Task<List<MessageResponse>> GetMessagesAsync(HttpClient client, string token, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<MessageResponse>>())!;
    }

    private sealed record MockWebhookResponse(bool Processed, bool Duplicate, Guid ConversationId);
    private sealed record MessageResponse(Guid Id, string Direction, string Text, string? DeliveryStatus, int DeliveryAttempts, string? DeliveryError, DateTimeOffset CreatedAt);
}
