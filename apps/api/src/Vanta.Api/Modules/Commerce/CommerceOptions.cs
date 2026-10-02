namespace Vanta.Api.Modules.Commerce;

public sealed class CommerceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string CatalogPath { get; set; } = "/store/{slug}";
    public string CheckoutPath { get; set; } = "/checkout/{paymentReference}";
}

public static class CommerceUrlBuilder
{
    public static string CatalogUrl(CommerceOptions options, string slug) =>
        Combine(options.BaseUrl, options.CatalogPath.Replace("{slug}", Uri.EscapeDataString(slug), StringComparison.Ordinal));

    public static string CheckoutUrl(CommerceOptions options, string paymentReference) =>
        Combine(options.BaseUrl, options.CheckoutPath.Replace("{paymentReference}", Uri.EscapeDataString(paymentReference), StringComparison.Ordinal));

    private static string Combine(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}