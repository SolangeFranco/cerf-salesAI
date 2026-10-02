using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class InventoryTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Order_reserves_available_stock_and_rejects_insufficient_stock()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Inventario Reserva", $"inventory-reserve-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var variant = await CreateVariantAsync(client, auth.AccessToken, product.Id, 5);

        var first = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, 3);
        var second = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, 3);
        var current = await GetVariantAsync(client, auth.AccessToken, product.Id);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(5, current.StockOnHand);
        Assert.Equal(3, current.ReservedStock);
        Assert.Equal(2, current.AvailableStock);
    }

    [Fact]
    public async Task Cancelling_order_releases_reserved_stock()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Inventario Cancel", $"inventory-cancel-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var variant = await CreateVariantAsync(client, auth.AccessToken, product.Id, 5);
        var orderResponse = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, 2);
        var order = (await orderResponse.Content.ReadFromJsonAsync<OrderResponse>())!;

        var cancel = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Cancelled);
        var current = await GetVariantAsync(client, auth.AccessToken, product.Id);

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(0, current.ReservedStock);
        Assert.Equal(5, current.AvailableStock);
    }

    [Fact]
    public async Task Delivered_order_commits_reserved_stock()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Inventario Fulfill", $"inventory-fulfill-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var variant = await CreateVariantAsync(client, auth.AccessToken, product.Id, 5);
        var orderResponse = await CreateOrderAsync(client, auth.AccessToken, product.Id, variant.Id, 2);
        var order = (await orderResponse.Content.ReadFromJsonAsync<OrderResponse>())!;
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);
        var payment = await CreatePaymentAsync(client, auth.AccessToken, order.Id);
        await ConfirmPaymentAsync(client, auth.AccessToken, payment.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Preparing);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Shipped);
        var delivered = await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Delivered);
        var current = await GetVariantAsync(client, auth.AccessToken, product.Id);

        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        Assert.Equal(3, current.StockQuantity);
        Assert.Equal(0, current.ReservedStock);
        Assert.Equal(3, current.AvailableStock);
    }

    [Fact]
    public async Task Tenant_cannot_modify_inventory_of_another_tenant_and_negative_stock_is_rejected()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync(client, "Inventario Tenant A", $"inventory-a-{Guid.NewGuid():N}@vanta.local");
        var tenantB = await RegisterAsync(client, "Inventario Tenant B", $"inventory-b-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, tenantA.AccessToken);
        var variant = await CreateVariantAsync(client, tenantA.AccessToken, product.Id, 5);

        var crossTenant = await UpdateStockAsync(client, tenantB.AccessToken, product.Id, variant.Id, 4);
        var negative = await UpdateStockAsync(client, tenantA.AccessToken, product.Id, variant.Id, -1);

        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
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
        request.Content = JsonContent.Create(new { name = "Producto Inventario", sku = $"INV-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<VariantResponse> CreateVariantAsync(HttpClient client, string token, Guid productId, int stock)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Talle M", sku = $"INV-VAR-{Guid.NewGuid():N}", stockQuantity = stock });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VariantResponse>())!;
    }

    private static async Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, string token, Guid productId, Guid variantId, int quantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"inventory-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, variantId, quantity } } });
        var response = await client.SendAsync(request);
        return response;
    }

    private static async Task<HttpResponseMessage> UpdateStatusAsync(HttpClient client, string token, Guid orderId, string status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/orders/{orderId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { status });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> UpdateStockAsync(HttpClient client, string token, Guid productId, Guid variantId, int stock)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{productId}/variants/{variantId}/stock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { stockQuantity = stock });
        return await client.SendAsync(request);
    }

    private static async Task<VariantResponse> GetVariantAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<VariantResponse>>())!.Single();
    }

    private static async Task<PaymentResponse> CreatePaymentAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"inventory-payment-{Guid.NewGuid():N}");
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

    private sealed record PaymentResponse(Guid Id, Guid OrderId, string Provider, string ProviderReference, string Status, decimal Amount, DateTimeOffset CreatedAt);
    private sealed record VariantResponse(Guid Id, Guid ProductId, string Name, string Sku, int StockQuantity, int StockOnHand, int ReservedStock, int AvailableStock, decimal? PriceOverride, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
