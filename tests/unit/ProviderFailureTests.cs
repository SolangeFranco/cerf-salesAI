using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;
using Xunit;

namespace Vanta.Api.UnitTests;

// Priority: provider failures (payment provider outage, AI provider outage). Verifies that an
// external provider throwing never leaks internals to the client and never leaves partial state,
// and that the AI provider's own fallback still produces a safe customer-facing reply.
public sealed class ProviderFailureTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Payment_provider_outage_returns_a_safe_error_and_leaves_the_order_unchanged()
    {
        await using var throwingFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IPaymentProvider>(new ThrowingPaymentProvider())));
        var client = throwingFactory.CreateClient();
        var auth = await RegisterAsync(client, "provider-outage");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var order = await CreateOrderAsync(client, auth.AccessToken, product.Id);
        await UpdateStatusAsync(client, auth.AccessToken, order.Id, OrderStatus.Confirmed);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{order.Id}/payments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        request.Headers.Add("Idempotency-Key", $"outage-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ThrowingPaymentProvider", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);

        var detail = await GetOrderAsync(client, auth.AccessToken, order.Id);
        Assert.Equal(OrderStatus.Confirmed, detail.Status);
        Assert.DoesNotContain(detail.StatusHistory, history => history.ToStatus == OrderStatus.AwaitingPayment);
    }

    [Fact]
    public async Task Ai_provider_exception_falls_back_to_a_deterministic_customer_reply()
    {
        var provider = new SmartAgentProvider(
            new HttpClient(new AlwaysThrowsHandler()),
            Options.Create(new AiProviderOptions { Provider = "gemini", ApiKey = "test-key", Model = "gemini-1.5-flash" }),
            NullLogger<SmartAgentProvider>.Instance);

        var response = await provider.GenerateResponseAsync("Hola, buscaba una camisa", [], CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string slug)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"Vanta {slug}", $"{slug}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Producto Outage", sku = $"OUT-{Guid.NewGuid():N}", price = 30000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, string token, Guid productId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", $"outage-order-{Guid.NewGuid():N}");
        request.Content = JsonContent.Create(new { items = new[] { new { productId, quantity = 1 } } });
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

    private static async Task<OrderDetailResponse> GetOrderAsync(HttpClient client, string token, Guid orderId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderDetailResponse>())!;
    }

    private sealed class ThrowingPaymentProvider : IPaymentProvider
    {
        public string Name => "mock";

        public Task<PaymentIntent> CreatePaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated payment provider outage.");

        public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated payment provider outage.");

        public Task<PaymentRefundResult> RefundPaymentAsync(string providerReference, decimal amount, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated payment provider outage.");

        public PaymentWebhookValidationResult ValidateWebhook(PaymentWebhookSignal signal) => PaymentWebhookValidationResult.Invalid;
    }

    private sealed class AlwaysThrowsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Simulated AI provider outage.");
    }
}
