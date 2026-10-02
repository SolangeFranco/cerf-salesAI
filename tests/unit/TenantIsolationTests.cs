using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

public class TenantIsolationTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Each_tenant_only_sees_its_own_data()
    {
        var client = factory.CreateClient();

        var tenantAToken = await RegisterAsync(client, "Moda Asuncion", "duena-a@modaasuncion.com");
        var tenantBToken = await RegisterAsync(client, "Zapatos del Sur", "duena-b@zapatosdelsur.com");

        var tenantA = await GetCurrentTenantAsync(client, tenantAToken.AccessToken);
        var tenantB = await GetCurrentTenantAsync(client, tenantBToken.AccessToken);

        Assert.Equal(tenantAToken.TenantId, tenantA.Id);
        Assert.Equal(tenantBToken.TenantId, tenantB.Id);
        Assert.NotEqual(tenantA.Id, tenantB.Id);
        Assert.NotEqual(tenantA.Slug, tenantB.Slug);
    }

    [Fact]
    public async Task Login_returns_token_for_existing_user()
    {
        var client = factory.CreateClient();
        const string email = "duena-c@ferreteriacentral.com";
        const string password = "Passw0rd!123";

        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Ferreteria Central", email, password));

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

        loginResponse.EnsureSuccessStatusCode();
        var body = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string tenantName, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest(tenantName, email, "Passw0rd!123"));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        return body!;
    }

    private static async Task<TenantResponse> GetCurrentTenantAsync(HttpClient client, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenants/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TenantResponse>();
        Assert.NotNull(body);
        return body!;
    }
}
