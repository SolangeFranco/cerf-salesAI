using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public class OrdersTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Order_uses_variant_price_reserves_stock_and_is_idempotent()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Ordenes Demo", "orders-demo@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var variant = await CreateVariantAsync(client, auth.AccessToken, product.Id);
        const string idempotencyKey = "order-test-001";

        var first = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, idempotencyKey);
        var retry = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, idempotencyKey);

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, retry.StatusCode);
        var firstOrder = (await first.Content.ReadFromJsonAsync<OrderResponse>())!;
        var retryOrder = (await retry.Content.ReadFromJsonAsync<OrderResponse>())!;
        Assert.Equal(firstOrder.Id, retryOrder.Id);
        Assert.Equal(24000, firstOrder.Total);

        var variants = await ListVariantsAsync(client, auth.AccessToken, product.Id);
        Assert.Equal(3, variants[0].StockQuantity);
    }

    [Fact]
    public async Task Order_requires_idempotency_key()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Ordenes Validacion", "orders-validation@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Content = JsonContent.Create(new { items = new[] { new { productId = product.Id, quantity = 1 } } });
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Order_status_only_allows_valid_transitions()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Estados Demo", "orders-status@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var createResponse = await CreateOrderAsync(client, auth.AccessToken, product.Id, null, "status-test-001");
        var order = (await createResponse.Content.ReadFromJsonAsync<OrderResponse>())!;

        var confirmed = await UpdateStatusAsync(client, auth.AccessToken, order.Id, "Confirmed");
        var invalidDelivered = await UpdateStatusAsync(client, auth.AccessToken, order.Id, "Delivered");

        Assert.Equal(System.Net.HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, invalidDelivered.StatusCode);
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
        request.Content = JsonContent.Create(new { name = "Producto Orden", sku = $"ORD-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<VariantResponse> CreateVariantAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Variante Orden", sku = $"VAR-{Guid.NewGuid():N}", stockQuantity = 5, priceOverride = 12000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VariantResponse>())!;
    }

    private static async Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, string token, Guid productId, Guid? variantId, string idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Content = JsonContent.Create(new { items = new[] { new { productId, variantId, quantity = 2 } } });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateStatusAsync(HttpClient client, string token, Guid orderId, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status });
        return await client.SendAsync(request);
    }

    private static async Task<List<VariantResponse>> ListVariantsAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<VariantResponse>>())!;
    }
}
