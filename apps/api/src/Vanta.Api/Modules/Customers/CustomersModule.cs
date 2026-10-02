namespace Vanta.Api.Modules.Customers;

public static class CustomersModule
{
    public static IEndpointRouteBuilder MapCustomersModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/customers")
            .RequireAuthorization()
            .MapCustomerEndpoints();

        return endpoints;
    }
}
