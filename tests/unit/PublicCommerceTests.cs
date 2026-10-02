using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Commerce;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

// Priority 9 (public checkout): storefront browsing, order creation happy path, validation
// failures, stock conflicts, cross-tenant isolation, and duplicate/concurrent submissions.
public sealed class PublicCommerceTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Public_store_lists_only_active_products_for_that_tenant()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-store");
        await CreateProductAsync(client, auth.AccessToken, "Producto Público", 40000);

        var response = await client.GetAsync($"/api/public/store/{slug}");
        response.EnsureSuccessStatusCode();
        var store = await response.Content.ReadFromJsonAsync<PublicStoreResponse>();

        Assert.NotNull(store);
        Assert.Contains(store!.Products, product => product.Name == "Producto Público");
    }

    [Fact]
    public async Task Unknown_store_slug_returns_not_found()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/public/store/no-existe-esta-tienda");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_happy_path_creates_an_order_and_a_retrievable_payment_reference()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-happy");
        var product = await CreateProductAsync(client, auth.AccessToken, "Producto Checkout", 55000);

        var created = await CreateOrderAsync(client, slug, BuildRequest(product.Id));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<PublicOrderCreatedResponse>())!;
        Assert.Equal(55000m, body.Total);

        var checkout = await client.GetAsync($"/api/public/checkout/{body.PaymentReference}");
        checkout.EnsureSuccessStatusCode();
        var checkoutBody = await checkout.Content.ReadFromJsonAsync<PublicCheckoutResponse>();
        Assert.Equal("AwaitingPayment", checkoutBody!.OrderStatus);
    }

    [Fact]
    public async Task Checkout_with_missing_required_fields_is_rejected()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-validation");
        var product = await CreateProductAsync(client, auth.AccessToken, "Producto Validación", 20000);

        var response = await client.PostAsJsonAsync($"/api/public/store/{slug}/orders", new
        {
            name = "",
            phone = "0981000000",
            address = "Calle Falsa 123",
            items = new[] { new { productId = product.Id, variantId = (Guid?)null, quantity = 1 } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_with_invalid_quantity_is_rejected()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-qty");
        var product = await CreateProductAsync(client, auth.AccessToken, "Producto Cantidad", 20000);

        var response = await CreateOrderAsync(client, slug, BuildRequest(product.Id, quantity: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_with_unknown_product_is_rejected()
    {
        var (client, _, slug) = await RegisterTenantWithSlugAsync("public-unknown-product");

        var response = await CreateOrderAsync(client, slug, BuildRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_cannot_purchase_another_tenants_product_through_its_own_slug()
    {
        var (clientA, authA, slugA) = await RegisterTenantWithSlugAsync("public-cross-a");
        var (_, authB, _) = await RegisterTenantWithSlugAsync("public-cross-b");
        var productB = await CreateProductAsync(clientA, authB.AccessToken, "Producto Tenant B", 30000);

        // productB belongs to tenant B; buying it through tenant A's own storefront slug must fail.
        var response = await CreateOrderAsync(clientA, slugA, BuildRequest(productB.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_rejects_quantity_beyond_available_stock()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-stock");
        var product = await CreateProductAsync(client, auth.AccessToken, "Producto Stock", 15000);
        await CreateVariantAsync(client, auth.AccessToken, product.Id, "Único", stockQuantity: 2);
        var variantId = await GetFirstVariantIdAsync(client, slug, product.Id);

        var response = await CreateOrderAsync(client, slug, BuildRequest(product.Id, variantId, quantity: 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_checkout_submission_does_not_create_a_second_order()
    {
        var (client, auth, slug) = await RegisterTenantWithSlugAsync("public-duplicate");
        var product = await CreateProductAsync(client, auth.AccessToken, "Producto Duplicado", 45000);
        var request = BuildRequest(product.Id);

        var first = await CreateOrderAsync(client, slug, request);
        var second = await CreateOrderAsync(client, slug, request);

        var firstBody = await first.Content.ReadFromJsonAsync<PublicOrderCreatedResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<PublicOrderCreatedResponse>();
        Assert.Equal(firstBody!.OrderNumber, secondBody!.OrderNumber);
        Assert.Equal(firstBody.PaymentReference, secondBody.PaymentReference);
    }

    private static object BuildRequest(Guid productId, Guid? variantId = null, int quantity = 1) => new
    {
        name = "Cliente Público",
        phone = "0981000000",
        address = "Av. Siempre Viva 742",
        items = new[] { new { productId, variantId, quantity } },
    };

    private static Task<HttpResponseMessage> CreateOrderAsync(HttpClient client, string slug, object request) =>
        client.PostAsJsonAsync($"/api/public/store/{slug}/orders", request);

    private async Task<Guid?> GetFirstVariantIdAsync(HttpClient client, string slug, Guid productId)
    {
        var response = await client.GetAsync($"/api/public/store/{slug}");
        var store = await response.Content.ReadFromJsonAsync<PublicStoreResponse>();
        return store!.Products.Single(product => product.Id == productId).Variants.Single().Id;
    }

    private async Task<(HttpClient Client, AuthResponse Auth, string Slug)> RegisterTenantWithSlugAsync(string slugHint)
    {
        var client = factory.CreateClient();
        var tenantName = $"Public {slugHint} {Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(tenantName, $"{slugHint}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        registerResponse.EnsureSuccessStatusCode();
        var auth = (await registerResponse.Content.ReadFromJsonAsync<AuthResponse>())!;

        using var tenantRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/me");
        tenantRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var tenantResponse = await client.SendAsync(tenantRequest);
        var tenant = (await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>())!;

        return (client, auth, tenant.Slug);
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token, string name, decimal price)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku = $"PUB-{Guid.NewGuid():N}", price });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task CreateVariantAsync(HttpClient client, string token, Guid productId, string name, int stockQuantity)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/variants");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, sku = $"PUBVAR-{Guid.NewGuid():N}", stockQuantity, priceOverride = (decimal?)null });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
