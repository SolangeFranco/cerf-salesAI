using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class ApiHardeningTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Protected_endpoint_returns_problem_details_and_security_headers_without_authentication()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_json_returns_problem_details_without_internal_exception_data()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new StringContent("{not-json", System.Text.Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("JsonException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Oversized_request_is_rejected_before_endpoint_processing()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = new ByteArrayContent(new byte[16 * 1024 * 1024 + 1]),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}

public sealed class StagingEndpointHardeningTests
{
    [Fact]
    public async Task Staging_does_not_map_mock_whatsapp_endpoints()
    {
        await using var factory = new StagingWebApplicationFactory();
        using var client = factory.CreateClient();

        var webhookResponse = await client.PostAsync("/api/webhooks/whatsapp/mock", null);
        var channelResponse = await client.PostAsync("/api/channels/whatsapp/mock", null);

        Assert.Equal(HttpStatusCode.NotFound, webhookResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, channelResponse.StatusCode);
    }

    [Fact]
    public async Task Staging_demo_maps_mock_endpoints_but_keeps_them_protected()
    {
        await using var factory = new StagingWebApplicationFactory(enableMockEndpoints: true);
        using var client = factory.CreateClient();

        var webhookResponse = await client.PostAsync("/api/webhooks/whatsapp/mock", null);
        var channelResponse = await client.PostAsync("/api/channels/whatsapp/mock", null);

        Assert.Equal(HttpStatusCode.Unauthorized, webhookResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, channelResponse.StatusCode);
    }

    private sealed class StagingWebApplicationFactory(bool enableMockEndpoints = false) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("Payment:Provider", "manual-transfer");
            builder.UseSetting("Features:EnableMockEndpoints", enableMockEndpoints.ToString());
            builder.UseSetting("WhatsApp:MockWebhookSecret", "staging-test-secret");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Testing:UseInMemoryDatabase"] = "true",
                });
            });
        }
    }
}
