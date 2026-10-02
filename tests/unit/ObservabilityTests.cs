using System.Net;
using System.Text.Json;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class ObservabilityTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Requests_return_request_and_safe_correlation_ids()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "merchant-flow-42");

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Request-Id", out var requestIds));
        Assert.True(Guid.TryParse(requestIds.Single(), out _));
        Assert.Equal("merchant-flow-42", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task Unsafe_correlation_header_is_replaced_and_not_reflected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "bad-" + new string('x', 130));

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        Assert.DoesNotContain("bad-", correlationId);
        Assert.DoesNotContain("\\r", correlationId);
        Assert.DoesNotContain("\\n", correlationId);
    }

    [Fact]
    public async Task Live_health_does_not_require_postgres_and_does_not_expose_dependency_details()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", JsonDocument.Parse(body).RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }
}
