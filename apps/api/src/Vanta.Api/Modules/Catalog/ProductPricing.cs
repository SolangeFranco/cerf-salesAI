namespace Vanta.Api.Modules.Catalog;

public static class ProductPricing
{
    public static decimal ApplyDiscount(decimal price, decimal discountPercent) =>
        Math.Round(price * (1 - discountPercent / 100m), 2, MidpointRounding.AwayFromZero);

    public static decimal EffectivePrice(Product product, decimal? variantPriceOverride = null) =>
        ApplyDiscount(variantPriceOverride ?? product.Price, product.DiscountPercent);
}