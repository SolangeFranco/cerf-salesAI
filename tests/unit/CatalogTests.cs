using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Catalog;
using Xunit;

namespace Vanta.Api.UnitTests;

public class CatalogTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Product_list_is_scoped_to_the_authenticated_tenant()
    {
        var client = factory.CreateClient();
        var tenantAToken = await RegisterAsync(client, "Catalogo A", "catalog-a@vanta.local");
        var tenantBToken = await RegisterAsync(client, "Catalogo B", "catalog-b@vanta.local");

        await CreateProductAsync(client, tenantAToken.AccessToken, "Camisa Azul", "CAM-AZUL", 125000);

        var tenantBProducts = await ListProductsAsync(client, tenantBToken.AccessToken);
        var tenantAProducts = await ListProductsAsync(client, tenantAToken.AccessToken);

        Assert.Empty(tenantBProducts);
        Assert.Single(tenantAProducts);
        Assert.Equal("CAM-AZUL", tenantAProducts[0].Sku);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected_within_the_same_tenant()
    {
        var client = factory.CreateClient();
        var token = await RegisterAsync(client, "Catalogo C", "catalog-c@vanta.local");

        var first = await CreateProductAsync(client, token.AccessToken, "Producto Uno", "SKU-1", 1000);
        var secondResponse = await CreateProductResponseAsync(client, token.AccessToken, "Producto Dos", "SKU-1", 2000);

        Assert.Equal(System.Net.HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task Variant_stock_is_created_and_isolated_by_tenant()
    {
        var client = factory.CreateClient();
        var tenantAToken = await RegisterAsync(client, "Variantes A", "variants-a@vanta.local");
        var tenantBToken = await RegisterAsync(client, "Variantes B", "variants-b@vanta.local");
        var productResponse = await CreateProductAsync(client, tenantAToken.AccessToken, "Remera", "REM-BASE", 90000);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var variantResponse = await CreateVariantAsync(client, tenantAToken.AccessToken, product.Id, "Talle M / Negro", "REM-M-NEG", 7);
        var tenantAVariants = await ListVariantsAsync(client, tenantAToken.AccessToken, product.Id);
        var tenantBVariantsResponse = await ListVariantsResponseAsync(client, tenantBToken.AccessToken, product.Id);

        Assert.Equal(System.Net.HttpStatusCode.Created, variantResponse.StatusCode);
        Assert.Single(tenantAVariants);
        Assert.Equal(7, tenantAVariants[0].StockQuantity);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, tenantBVariantsResponse.StatusCode);
    }

    [Fact]
    public async Task Product_owner_can_edit_product_and_update_variant_stock()
    {
        var client = factory.CreateClient();
        var token = await RegisterAsync(client, "Catalogo Edicion", "catalog-edit@vanta.local");
        var productResponse = await CreateProductAsync(client, token.AccessToken, "Producto Original", "EDIT-BASE", 1000);
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
        var variantResponse = await CreateVariantAsync(client, token.AccessToken, product.Id, "Talle unico", "EDIT-V1", 2);
        var variant = (await variantResponse.Content.ReadFromJsonAsync<VariantResponse>())!;

        using var updateProduct = new HttpRequestMessage(HttpMethod.Put, $"/api/products/{product.Id}");
        updateProduct.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        updateProduct.Content = JsonContent.Create(new { name = "Producto Actualizado", description = "Descripcion nueva", price = 1500, isActive = true });
        var productUpdateResponse = await client.SendAsync(updateProduct);

        using var updateStock = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{product.Id}/variants/{variant.Id}/stock");
        updateStock.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        updateStock.Content = JsonContent.Create(new { stockQuantity = 9 });
        var stockUpdateResponse = await client.SendAsync(updateStock);

        Assert.Equal(System.Net.HttpStatusCode.OK, productUpdateResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, stockUpdateResponse.StatusCode);
        var updatedStock = (await stockUpdateResponse.Content.ReadFromJsonAsync<VariantResponse>())!;
        Assert.Equal(9, updatedStock.StockQuantity);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string tenantName, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(tenantName, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<HttpResponseMessage> CreateProductAsync(
        HttpClient client, string token, string name, string sku, decimal price)
    {
        var response = await CreateProductResponseAsync(client, token, name, sku, price);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<HttpResponseMessage> CreateProductResponseAsync(
        HttpClient client, string token, string name, string sku, decimal price)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku, price });
        return await client.SendAsync(request);
    }

    private static async Task<List<ProductResponse>> ListProductsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ProductResponse>>())!;
    }

    private static async Task<HttpResponseMessage> CreateVariantAsync(
        HttpClient client, string token, Guid productId, string name, string sku, int stockQuantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku, stockQuantity });
        return await client.SendAsync(request);
    }

    private static async Task<List<VariantResponse>> ListVariantsAsync(HttpClient client, string token, Guid productId)
    {
        var response = await ListVariantsResponseAsync(client, token, productId);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<VariantResponse>>())!;
    }

    private static async Task<HttpResponseMessage> ListVariantsResponseAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }
}
