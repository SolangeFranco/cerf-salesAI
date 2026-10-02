namespace Vanta.Api.Modules.WhatsApp;

public sealed record AgentShippingToolResult(decimal CostGs, int EstimatedDays, string Zone);

// A single, explicit, versionable shipping policy. The agent must never invent a shipping cost
// or delivery time itself — it only ever reports what this calculator returns.
public static class ShippingCalculator
{
    private static readonly string[] MetroKeywords = ["asuncion", "asunción", "gran asuncion", "gran asunción", "central", "lambare", "fernando de la mora", "san lorenzo", "luque", "capiata", "ñemby", "lambaré", "ñemby"];

    public static AgentShippingToolResult Calculate(string address, decimal metropolitanCostGs, decimal interiorCostGs, decimal? distanceKm, decimal baseCostGs, decimal includedDistanceKm, decimal extraCostPerKmGs)
    {
        var normalized = (address ?? string.Empty).Trim().ToLowerInvariant();
        var isMetro = MetroKeywords.Any(normalized.Contains);
        var configuredCost = isMetro ? metropolitanCostGs : interiorCostGs;
        var distanceSurcharge = distanceKm is > 0 && distanceKm > includedDistanceKm
            ? decimal.Ceiling(distanceKm.Value - includedDistanceKm) * extraCostPerKmGs
            : 0m;
        var cost = distanceKm is > 0 ? baseCostGs + distanceSurcharge : configuredCost;
        return new AgentShippingToolResult(cost, isMetro ? 2 : 5, isMetro ? "Área Metropolitana" : "Interior");
    }
}
