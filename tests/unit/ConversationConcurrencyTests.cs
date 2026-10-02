using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Tenants;
using Vanta.Api.Modules.WhatsApp;
using Xunit;

namespace Vanta.Api.UnitTests;

// Runs against real Postgres: EF Core's InMemory provider does not enforce the same atomicity as
// ExecuteUpdateAsync, so the claim-race guarantee can only be verified here (mirrors
// PaymentConcurrencyTests/InventoryConcurrencyTests).
public sealed class ConversationConcurrencyTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Two_concurrent_ai_response_claims_for_the_same_message_apply_exactly_once()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Conversation Concurrency {Guid.NewGuid():N}", Slug = $"conv-concurrency-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ChannelAccountId = Guid.NewGuid(),
            CustomerExternalId = $"customer-{Guid.NewGuid():N}", Status = ConversationStatus.AiActive,
            LastMessageAt = now, LastActivityAt = now,
        };
        setup.Tenants.Add(tenant);
        setup.Conversations.Add(conversation);
        await setup.SaveChangesAsync();

        var inboundMessageId = Guid.NewGuid();
        var first = TryClaimAsync(tenant.Id, conversation.Id, inboundMessageId);
        var second = TryClaimAsync(tenant.Id, conversation.Id, inboundMessageId);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result));

        await using var verify = CreateContext();
        var persisted = await verify.Conversations.SingleAsync(current => current.Id == conversation.Id);
        Assert.Equal(inboundMessageId, persisted.LastRespondedToMessageId);
    }

    [Fact]
    public async Task Two_concurrent_pause_attempts_from_ai_active_apply_exactly_once()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Conversation Pause {Guid.NewGuid():N}", Slug = $"conv-pause-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(), TenantId = tenant.Id, ChannelAccountId = Guid.NewGuid(),
            CustomerExternalId = $"customer-{Guid.NewGuid():N}", Status = ConversationStatus.AiActive,
            LastMessageAt = now, LastActivityAt = now,
        };
        setup.Tenants.Add(tenant);
        setup.Conversations.Add(conversation);
        await setup.SaveChangesAsync();

        var actorUserId = Guid.NewGuid();
        var first = TryPauseAsync(tenant.Id, conversation.Id, actorUserId);
        var second = TryPauseAsync(tenant.Id, conversation.Id, actorUserId);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result == ConversationOperationResult.Success));

        await using var verify = CreateContext();
        var persisted = await verify.Conversations.SingleAsync(current => current.Id == conversation.Id);
        Assert.Equal(ConversationStatus.Paused, persisted.Status);
        var activityCount = await verify.ConversationActivities.CountAsync(activity => activity.ConversationId == conversation.Id);
        Assert.Equal(1, activityCount);
    }

    private static async Task<bool> TryClaimAsync(Guid tenantId, Guid conversationId, Guid inboundMessageId)
    {
        await using var context = CreateContext();
        var service = new ConversationService(context, new CommerceEventRecorder(context));
        return await service.TryClaimAiResponseAsync(tenantId, conversationId, inboundMessageId, CancellationToken.None);
    }

    private static async Task<ConversationOperationResult> TryPauseAsync(Guid tenantId, Guid conversationId, Guid actorUserId)
    {
        await using var context = CreateContext();
        var service = new ConversationService(context, new CommerceEventRecorder(context));
        return await service.PauseAsync(tenantId, conversationId, actorUserId, CancellationToken.None);
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}
