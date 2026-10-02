using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Identity;
using Xunit;

namespace Vanta.Api.UnitTests;

public class CustomersTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Customers_are_scoped_to_their_tenant()
    {
        var client = factory.CreateClient();
        var tenantAToken = await RegisterAsync(client, "Clientes A", "customers-a@vanta.local");
        var tenantBToken = await RegisterAsync(client, "Clientes B", "customers-b@vanta.local");

        var createResponse = await CreateCustomerAsync(client, tenantAToken.AccessToken, "Ana Demo", "+595981111111");
        var customer = (await createResponse.Content.ReadFromJsonAsync<CustomerResponse>())!;
        var tenantBCustomers = await ListCustomersAsync(client, tenantBToken.AccessToken);
        var tenantACustomers = await ListCustomersAsync(client, tenantAToken.AccessToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Empty(tenantBCustomers);
        Assert.Single(tenantACustomers);
        Assert.Equal(customer.Id, tenantACustomers[0].Id);
    }

    [Fact]
    public async Task Duplicate_phone_is_rejected_within_a_tenant()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "Clientes C", "customers-c@vanta.local");
        await CreateCustomerAsync(client, auth.AccessToken, "Cliente Uno", "+595982222222");
        var duplicate = await CreateCustomerAsync(client, auth.AccessToken, "Cliente Dos", "+595982222222");

        Assert.Equal(System.Net.HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string tenantName, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(tenantName, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<HttpResponseMessage> CreateCustomerAsync(HttpClient client, string token, string name, string phone)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/customers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name, phone });
        return await client.SendAsync(request);
    }

    private static async Task<List<CustomerResponse>> ListCustomersAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/customers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<CustomerResponse>>())!;
    }
}
