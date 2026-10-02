namespace Vanta.Api.Modules.Catalog;

public static class CatalogModule
{
    public static IEndpointRouteBuilder MapCatalogModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/products")
            .RequireAuthorization()
            .MapCategoryEndpoints()
            .MapProductEndpoints()
            .MapVariantEndpoints();

        return endpoints;
    }
}
