using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public class OrderDetailsTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Order_detail_is_scoped_to_the_authenticated_tenant()
    {
        var client = factory.CreateClient();
        var tenantAToken = await RegisterAsync(client, "Detalle A", "detail-a@vanta.local");
        var tenantBToken = await RegisterAsync(client, "Detalle B", "detail-b@vanta.local");
        var product = await CreateProductAsync(client, tenantAToken.AccessToken);
        var order = await CreateOrderAsync(client, tenantAToken.AccessToken, product.Id);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantBToken.AccessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Order_detail_includes_status_history()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Historial Demo", "history@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);

        using var transition = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{order.Id}/status");
        transition.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        transition.Content = JsonContent.Create(new { status = "Confirmed" });
        (await client.SendAsync(transition)).EnsureSuccessStatusCode();

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{order.Id}");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var detailResponse = await client.SendAsync(detailRequest);
        detailResponse.EnsureSuccessStatusCode();
        var detail = await detailResponse.Content.ReadFromJsonAsync<OrderDetailResponse>();

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.StatusHistory.Count);
        Assert.Equal("Confirmed", detail.StatusHistory[^1].ToStatus);
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
        request.Content = JsonContent.Create(new { name = "Detalle", sku = $"DET-{Guid.NewGuid():N}", price = 1000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"detail-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
