namespace Vanta.Api.Modules.Payments;

public static class PaymentsModule
{
    public static IEndpointRouteBuilder MapPaymentsModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api")
            .RequireAuthorization()
            .MapPaymentEndpoints();
        return endpoints;
    }
}
