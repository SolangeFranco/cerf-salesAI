using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public class CartTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Cart_adds_items_without_reserving_stock_and_reuses_existing_line()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Carrito Demo", $"cart-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var variant = await CreateVariantAsync(client, auth.AccessToken, product.Id);
        await ConnectAsync(client, auth.AccessToken, "cart-account-" + Guid.NewGuid().ToString("N"));
        var accountId = (await ListChannelsAsync(client, auth.AccessToken)).Single().ExternalAccountId;
        var conversationId = await ReceiveMessageAsync(client, accountId);

        var first = await AddAsync(client, auth.AccessToken, conversationId, product.Id, variant.Id, 1);
        var second = await AddAsync(client, auth.AccessToken, conversationId, product.Id, variant.Id, 2);
        var cart = (await second.Content.ReadFromJsonAsync<CartResponse>())!;

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, second.StatusCode);
        Assert.Single(cart.Items);
        Assert.Equal(3, cart.Items[0].Quantity);
        Assert.Equal(36000, cart.Total);
        Assert.Equal(5, (await ListVariantsAsync(client, auth.AccessToken, product.Id))[0].StockQuantity);
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
        request.Content = JsonContent.Create(new { name = "Producto Carrito", sku = $"CART-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<VariantResponse> CreateVariantAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Talle M", sku = $"CART-VAR-{Guid.NewGuid():N}", stockQuantity = 5, priceOverride = 12000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VariantResponse>())!;
    }

    private static async Task ConnectAsync(HttpClient client, string token, string externalAccountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId, displayName = "Cart Demo" });
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task<List<ChannelResponse>> ListChannelsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/channels/whatsapp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ChannelResponse>>())!;
    }

    private static async Task<Guid> ReceiveMessageAsync(HttpClient client, string accountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { eventId = Guid.NewGuid().ToString(), accountExternalId = accountId, customerExternalId = "595000000001", messageId = Guid.NewGuid().ToString(), text = "Hola" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MockWebhookResponse>())!.ConversationId;
    }

    private static async Task<HttpResponseMessage> AddAsync(HttpClient client, string token, Guid conversationId, Guid productId, Guid variantId, int quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/cart/items");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { productId, variantId, quantity });
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

    private sealed record ChannelResponse(Guid Id, string Provider, string ExternalAccountId, string DisplayName);
    private sealed record MockWebhookResponse(bool Processed, bool Duplicate, Guid ConversationId);
}
