using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class TenantAccessControlTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Tenant_cannot_read_or_modify_another_tenants_resources_or_use_its_ai_tools()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync(client, "Aislamiento A", $"isolation-a-{Guid.NewGuid():N}@vanta.local");
        var tenantB = await RegisterAsync(client, "Aislamiento B", $"isolation-b-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, tenantA.AccessToken);
        var variant = await CreateVariantAsync(client, tenantA.AccessToken, product.Id);
        var conversationId = await CreateConversationAsync(client, tenantA.AccessToken);
        var order = await CreateOrderAsync(client, tenantA.AccessToken, product.Id, conversationId);

        Assert.Empty(await ListProductsAsync(client, tenantB.AccessToken));
        Assert.Empty(await ListCustomersAsync(client, tenantB.AccessToken));
        Assert.Empty(await ListOrdersAsync(client, tenantB.AccessToken));
        Assert.Empty(await ListConversationsAsync(client, tenantB.AccessToken));
        Assert.Empty(await SearchProductsAsync(client, tenantB.AccessToken, product.Name));

        Assert.Equal(HttpStatusCode.NotFound, await UpdateProductAsync(client, tenantB.AccessToken, product.Id));
        Assert.Equal(HttpStatusCode.NotFound, await UpdateVariantStockAsync(client, tenantB.AccessToken, product.Id, variant.Id));
        Assert.Equal(HttpStatusCode.NotFound, await UploadProductImageAsync(client, tenantB.AccessToken, product.Id));
        Assert.Equal(HttpStatusCode.NotFound, await GetConversationMessagesAsync(client, tenantB.AccessToken, conversationId));
        Assert.Equal(HttpStatusCode.NotFound, await SendMessageAsync(client, tenantB.AccessToken, conversationId));
        Assert.Equal(HttpStatusCode.NotFound, await UpdateOrderStatusAsync(client, tenantB.AccessToken, order.Id));
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
        request.Content = JsonContent.Create(new { name = "Producto Privado A", sku = $"ISO-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<VariantResponse> CreateVariantAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Variante privada", sku = $"ISO-VAR-{Guid.NewGuid():N}", stockQuantity = 4 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VariantResponse>())!;
    }

    private static async Task<Guid> CreateConversationAsync(HttpClient client, string token)
    {
        var accountId = $"isolation-account-{Guid.NewGuid():N}";
        using (var connect = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock"))
        {
            connect.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            connect.Content = JsonContent.Create(new { externalAccountId = accountId, displayName = "Isolation" });
            (await client.SendAsync(connect)).EnsureSuccessStatusCode();
        }

        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        webhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        webhook.Content = JsonContent.Create(new
        {
            eventId = $"isolation-event-{Guid.NewGuid():N}",
            accountExternalId = accountId,
            customerExternalId = $"isolation-customer-{Guid.NewGuid():N}",
            messageId = $"isolation-message-{Guid.NewGuid():N}",
            text = "Hola",
        });
        var response = await client.SendAsync(webhook);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MockWebhookResponse>();
        return body!.ConversationId;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"isolation-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } }, conversationId });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    private static async Task<List<ProductResponse>> ListProductsAsync(HttpClient client, string token) =>
        await GetAsync<List<ProductResponse>>(client, token, "/api/products");

    private static async Task<List<CustomerResponse>> ListCustomersAsync(HttpClient client, string token) =>
        await GetAsync<List<CustomerResponse>>(client, token, "/api/customers");

    private static async Task<List<OrderResponse>> ListOrdersAsync(HttpClient client, string token) =>
        await GetAsync<List<OrderResponse>>(client, token, "/api/orders");

    private static async Task<List<ConversationResponse>> ListConversationsAsync(HttpClient client, string token) =>
        await GetAsync<List<ConversationResponse>>(client, token, "/api/conversations");

    private static async Task<List<ProductSearchResponse>> SearchProductsAsync(HttpClient client, string token, string query) =>
        await GetAsync<List<ProductSearchResponse>>(client, token, $"/api/ai/tools/search-products?query={Uri.EscapeDataString(query)}");

    private static async Task<T> GetAsync<T>(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpStatusCode> UpdateProductAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{productId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Ataque", price = 1, isActive = true });
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> UpdateVariantStockAsync(HttpClient client, string token, Guid productId, Guid variantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{productId}/variants/{variantId}/stock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { stockQuantity = 999 });
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> UploadProductImageAsync(HttpClient client, string token, Guid productId)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "cross-tenant.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/image");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> GetConversationMessagesAsync(HttpClient client, string token, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> SendMessageAsync(HttpClient client, string token, Guid conversationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { text = "Acceso indebido", idempotencyKey = $"isolation-message-{Guid.NewGuid():N}" });
        return (await client.SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> UpdateOrderStatusAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status = OrderStatus.Confirmed });
        return (await client.SendAsync(request)).StatusCode;
    }

    private sealed record MockWebhookResponse(Guid ConversationId);
    private sealed record ConversationResponse(Guid Id, string CustomerExternalId, string Status, DateTimeOffset LastMessageAt);
    private sealed record ProductSearchResponse(Guid Id, string Name, string Sku, decimal Price);
}
