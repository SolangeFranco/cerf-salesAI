using Vanta.Api.Modules.WhatsApp;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class StoreSettingsTests
{
    [Fact]
    public void Shipping_uses_configured_metropolitan_cost()
    {
        var result = ShippingCalculator.Calculate("Asuncion", 22000m, 41000m, null, 0m, 10m, 0m);

        Assert.Equal(22000m, result.CostGs);
        Assert.Equal("Área Metropolitana", result.Zone);
    }

    [Fact]
    public void Shipping_uses_configured_interior_cost()
    {
        var result = ShippingCalculator.Calculate("Encarnacion", 22000m, 41000m, null, 0m, 10m, 0m);

        Assert.Equal(41000m, result.CostGs);
        Assert.Equal("Interior", result.Zone);
    }

    [Fact]
    public void Shipping_preserves_an_explicit_zero_cost_policy()
    {
        var result = ShippingCalculator.Calculate("Luque", 0m, 0m, null, 0m, 10m, 0m);

        Assert.Equal(0m, result.CostGs);
    }

    [Fact]
    public void Shipping_adds_extra_cost_for_each_started_kilometer_beyond_included_distance()
    {
        var result = ShippingCalculator.Calculate("Luque", 15000m, 35000m, 12.2m, 10000m, 10m, 2500m);

        Assert.Equal(17500m, result.CostGs);
    }
}
