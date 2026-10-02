using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.Modules.Tenants;

public sealed record TenantResponse(Guid Id, string Name, string Slug, string? LogoUrl, decimal DeliveryBaseCostGs, decimal DeliveryIncludedDistanceKm, decimal DeliveryExtraCostPerKmGs, decimal MetropolitanDeliveryCostGs, decimal InteriorDeliveryCostGs, bool HasWhatsAppChannel, bool StoreSetupCompleted, bool AiResponsesEnabled, string? BankName, string? BankBeneficiaryName, string? BankAccountNumber, string? BankDocumentNumber, string? BankAlias);
public sealed record UpdateTenantSettingsRequest(
    [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200, MinimumLength = 2)] string Name,
    [property: System.ComponentModel.DataAnnotations.Url, System.ComponentModel.DataAnnotations.StringLength(2048)] string? LogoUrl,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999")] decimal DeliveryBaseCostGs,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0.01", "100000")] decimal DeliveryIncludedDistanceKm,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999")] decimal DeliveryExtraCostPerKmGs,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999")] decimal MetropolitanDeliveryCostGs,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "999999999")] decimal InteriorDeliveryCostGs,
    bool AiResponsesEnabled = false,
    string? BankName = null,
    string? BankBeneficiaryName = null,
    string? BankAccountNumber = null,
    string? BankDocumentNumber = null,
    string? BankAlias = null);
public sealed record DashboardSummaryResponse(
    int Products,
    int Orders,
    int Conversations,
    decimal TotalRevenue,
    int PaidOrders,
    decimal AiAttributedRevenue,
    int AiCreatedOrders,
    int AiAssistedOrders,
    decimal AbandonedCartRecoveredRevenue,
    int AbandonedCartRecoveredOrders,
    bool HasAttributionData);

public static class TenantEndpoints
{
    public static RouteGroupBuilder MapTenantEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", GetCurrentTenantAsync);
        group.MapPut("/settings", UpdateSettingsAsync);
        group.MapGet("/summary", GetDashboardSummaryAsync);
        return group;
    }

    private static async Task<IResult> GetCurrentTenantAsync(
        ClaimsPrincipal user,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppProviderOptions> whatsappOptions,
        CancellationToken cancellationToken)
    {
        var tenantIdClaim = user.FindFirstValue("tenant_id");
        if (tenantIdClaim is null || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var tenant = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Id, t.Name, t.Slug, t.LogoUrl, t.DeliveryBaseCostGs, t.DeliveryIncludedDistanceKm, t.DeliveryExtraCostPerKmGs, t.MetropolitanDeliveryCostGs, t.InteriorDeliveryCostGs, t.StoreSetupCompleted, t.AiResponsesEnabled, t.BankName, t.BankBeneficiaryName, t.BankAccountNumber, t.BankDocumentNumber, t.BankAlias })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null) return Results.NotFound();
        var hasWhatsAppChannel = await HasConnectedWhatsAppChannelAsync(tenantId, db, httpClientFactory, whatsappOptions.Value, cancellationToken);
        return Results.Ok(new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.LogoUrl, tenant.DeliveryBaseCostGs, tenant.DeliveryIncludedDistanceKm, tenant.DeliveryExtraCostPerKmGs, tenant.MetropolitanDeliveryCostGs, tenant.InteriorDeliveryCostGs, hasWhatsAppChannel, tenant.StoreSetupCompleted, tenant.AiResponsesEnabled, tenant.BankName, tenant.BankBeneficiaryName, tenant.BankAccountNumber, tenant.BankDocumentNumber, tenant.BankAlias));
    }

    private static async Task<bool> HasConnectedWhatsAppChannelAsync(
        Guid tenantId,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        WhatsAppProviderOptions options,
        CancellationToken cancellationToken)
    {
        if (await db.WhatsAppChannelAccounts.AnyAsync(channel => channel.TenantId == tenantId && channel.IsActive && channel.Provider == "meta", cancellationToken)) return true;
        var linkedChannels = await db.WhatsAppChannelAccounts.AsNoTracking()
            .Where(channel => channel.TenantId == tenantId && channel.IsActive && channel.Provider == "linked-device")
            .Select(channel => channel.ExternalAccountId)
            .ToListAsync(cancellationToken);
        foreach (var externalAccountId in linkedChannels)
        {
            if (!externalAccountId.StartsWith("linked:", StringComparison.Ordinal)) continue;
            var sessionId = externalAccountId["linked:".Length..];
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}");
            request.Headers.Add("X-Bridge-Key", options.LinkedDeviceBridgeKey);
            try
            {
                using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;
                var status = await response.Content.ReadFromJsonAsync<LinkedDeviceSessionStatus>(cancellationToken: cancellationToken);
                if (status?.Status == "connected") return true;
            }
            catch (HttpRequestException)
            {
                // A bridge outage must not invalidate the merchant JWT.
            }
        }
        return false;
    }

    private sealed record LinkedDeviceSessionStatus(string SessionId, string Status, string? Qr);

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateTenantSettingsRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        ConversationService conversations,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue("tenant_id"), out var tenantId)) return Results.Unauthorized();
        var tenant = await db.Tenants.FirstOrDefaultAsync(current => current.Id == tenantId, cancellationToken);
        if (tenant is null) return Results.NotFound();

        var aiPauseChanged = tenant.AiResponsesEnabled != request.AiResponsesEnabled;
        var actorUserId = Guid.TryParse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : (Guid?)null;
        await using var transaction = aiPauseChanged && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        tenant.Name = request.Name.Trim();
        tenant.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        tenant.StoreSetupCompleted = true;
        tenant.AiResponsesEnabled = request.AiResponsesEnabled;
        tenant.BankName = request.BankName?.Trim();
        tenant.BankBeneficiaryName = request.BankBeneficiaryName?.Trim();
        tenant.BankAccountNumber = request.BankAccountNumber?.Trim();
        tenant.BankDocumentNumber = request.BankDocumentNumber?.Trim();
        tenant.BankAlias = request.BankAlias?.Trim();
        tenant.DeliveryBaseCostGs = request.DeliveryBaseCostGs;
        tenant.DeliveryIncludedDistanceKm = request.DeliveryIncludedDistanceKm;
        tenant.DeliveryExtraCostPerKmGs = request.DeliveryExtraCostPerKmGs;
        tenant.MetropolitanDeliveryCostGs = request.MetropolitanDeliveryCostGs;
        tenant.InteriorDeliveryCostGs = request.InteriorDeliveryCostGs;
        await db.SaveChangesAsync(cancellationToken);

        if (aiPauseChanged)
        {
            if (request.AiResponsesEnabled)
                await conversations.ResumeGlobalPauseAsync(tenantId, actorUserId, cancellationToken);
            else
                await conversations.PauseAllAsync(tenantId, actorUserId, cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        var hasWhatsAppChannel = await db.WhatsAppChannelAccounts.AnyAsync(channel => channel.TenantId == tenant.Id && channel.IsActive && channel.Provider != "mock", cancellationToken);
        return Results.Ok(new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.LogoUrl, tenant.DeliveryBaseCostGs, tenant.DeliveryIncludedDistanceKm, tenant.DeliveryExtraCostPerKmGs, tenant.MetropolitanDeliveryCostGs, tenant.InteriorDeliveryCostGs, hasWhatsAppChannel, tenant.StoreSetupCompleted, tenant.AiResponsesEnabled, tenant.BankName, tenant.BankBeneficiaryName, tenant.BankAccountNumber, tenant.BankDocumentNumber, tenant.BankAlias));
    }

    private static async Task<Results<Ok<DashboardSummaryResponse>, UnauthorizedHttpResult>> GetDashboardSummaryAsync(
        ClaimsPrincipal user,
        VantaDbContext db,
        AiRevenueAttributionService attribution,
        CancellationToken cancellationToken)
    {
        var tenantIdClaim = user.FindFirstValue("tenant_id");
        if (tenantIdClaim is null || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            return TypedResults.Unauthorized();
        }

        var revenue = await attribution.GetSummaryAsync(tenantId, cancellationToken);
        var summary = new DashboardSummaryResponse(
            await db.Products.CountAsync(product => product.TenantId == tenantId && product.IsActive, cancellationToken),
            await db.Orders.CountAsync(order => order.TenantId == tenantId, cancellationToken),
            await db.Conversations.CountAsync(conversation => conversation.TenantId == tenantId, cancellationToken),
            revenue.TotalRevenue,
            revenue.PaidOrders,
            revenue.AiAttributedRevenue,
            revenue.AiCreatedOrders,
            revenue.AiAssistedOrders,
            revenue.AbandonedCartRecoveredRevenue,
            revenue.AbandonedCartRecoveredOrders,
            revenue.HasAttributionData);

        return TypedResults.Ok(summary);
    }
}
