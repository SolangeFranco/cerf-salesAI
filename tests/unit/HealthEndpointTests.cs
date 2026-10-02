using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Vanta.Api.UnitTests;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Health_endpoint_responds()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        // Postgres may not be reachable in this environment; we only assert the
        // endpoint is wired up, not that dependencies are healthy.
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
    }
}
