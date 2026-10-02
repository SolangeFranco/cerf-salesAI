namespace Vanta.Api.Modules.Orders;

public static class OrdersModule
{
    public static IEndpointRouteBuilder MapOrdersModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/orders")
            .RequireAuthorization()
            .MapOrderEndpoints();

        return endpoints;
    }
}
