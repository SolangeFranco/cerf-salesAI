using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Identity;
using Xunit;

namespace Vanta.Api.UnitTests;

// Priority 1 (authentication): registration validation, duplicate tenants, wrong-credential
// login, and rejection of malformed/tampered/missing-scheme JWTs on protected endpoints.
public sealed class AuthenticationTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Register_with_weak_password_is_rejected_with_validation_problem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Weak Password Co", $"weak-{Guid.NewGuid():N}@vanta.local", "short1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Register_with_invalid_email_is_rejected_with_validation_problem()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Invalid Email Co", "not-an-email", "Passw0rd!123"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Registering_the_same_tenant_name_twice_is_rejected_as_conflict()
    {
        var client = factory.CreateClient();
        var tenantName = $"Duplicate Tenant {Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(tenantName, $"first-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(tenantName, $"second-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var client = factory.CreateClient();
        var email = $"wrong-pass-{Guid.NewGuid():N}@vanta.local";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Wrong Password Co", email, "Passw0rd!123"));

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "IncorrectPassword!1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_nonexistent_email_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"nobody-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_bearer_token_is_rejected()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-jwt");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_token_signature_is_rejected()
    {
        var client = factory.CreateClient();
        var email = $"tamper-{Guid.NewGuid():N}@vanta.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Tamper Co", email, "Passw0rd!123"));
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();

        var segments = auth!.AccessToken.Split('.');
        var tamperedSignature = new string(segments[2].Reverse().ToArray());
        var tamperedToken = $"{segments[0]}.{segments[1]}.{tamperedSignature}";

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_bearer_scheme_is_rejected()
    {
        var client = factory.CreateClient();
        var email = $"noscheme-{Guid.NewGuid():N}@vanta.local";
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("No Scheme Co", email, "Passw0rd!123"));
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/me");
        request.Headers.TryAddWithoutValidation("Authorization", auth!.AccessToken);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
