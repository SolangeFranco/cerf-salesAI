using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;

namespace Vanta.Api.Modules.WhatsApp;

public enum ConversationOperationResult
{
    Success,
    NotFound,
    InvalidTransition,
    AssigneeNotInTenant,
}

/// <summary>
/// Owns Conversation operational state: enforces <see cref="ConversationStateMachine"/> rules,
/// applies every transition atomically (protecting assignment/pause/resume/close against
/// concurrent callers — e.g. two merchants clicking "take control" at once, or a human handoff
/// racing an inbound webhook) and writes a <see cref="ConversationActivity"/> audit row per change.
/// </summary>
public sealed class ConversationService(VantaDbContext db, CommerceEventRecorder commerceEvents)
{
    public async Task<ConversationOperationResult> AssignAsync(
        Guid tenantId, Guid conversationId, Guid? assigneeUserId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return ConversationOperationResult.NotFound;
        if (conversation.Status == ConversationStatus.Closed) return ConversationOperationResult.InvalidTransition;

        if (assigneeUserId is not null)
        {
            var belongsToTenant = await db.Users.AnyAsync(
                user => user.Id == assigneeUserId && user.TenantId == tenantId, cancellationToken);
            if (!belongsToTenant) return ConversationOperationResult.AssigneeNotInTenant;
        }

        var fromStatus = conversation.Status;
        // Assigning a human always means a human is now handling the conversation; unassigning
        // only clears ownership and leaves the current AI/human/paused state untouched.
        var toStatus = assigneeUserId is not null ? ConversationStatus.HumanActive : fromStatus;
        if (toStatus != fromStatus && !ConversationStateMachine.CanTransition(fromStatus, toStatus))
            return ConversationOperationResult.InvalidTransition;

        var now = DateTimeOffset.UtcNow;
        var updated = db.Database.IsRelational()
            ? await db.Conversations
                .Where(current => current.Id == conversationId && current.TenantId == tenantId && current.Status == fromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.Status, toStatus)
                    .SetProperty(current => current.AssignedUserId, assigneeUserId)
                    .SetProperty(current => current.AssignedAt, assigneeUserId is not null ? now : (DateTimeOffset?)null)
                    .SetProperty(current => current.LastActivityAt, now), cancellationToken)
            : ApplyInMemory(conversation, fromStatus, toStatus, now, assigneeUserId);
        if (updated != 1) return ConversationOperationResult.InvalidTransition;

        conversation.Status = toStatus;
        conversation.AssignedUserId = assigneeUserId;
        conversation.AssignedAt = assigneeUserId is not null ? now : null;
        conversation.LastActivityAt = now;
        db.ConversationActivities.Add(new ConversationActivity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId,
            Type = assigneeUserId is not null ? ConversationActivityType.Assigned : ConversationActivityType.Unassigned,
            FromStatus = fromStatus, ToStatus = toStatus, ActorUserId = actorUserId, AssignedUserId = assigneeUserId, CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return ConversationOperationResult.Success;
    }

    public Task<ConversationOperationResult> TakeControlAsync(Guid tenantId, Guid conversationId, Guid actorUserId, CancellationToken cancellationToken) =>
        AssignAsync(tenantId, conversationId, actorUserId, actorUserId, cancellationToken);

    public Task<ConversationOperationResult> PauseAsync(Guid tenantId, Guid conversationId, Guid actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(tenantId, conversationId, ConversationStatus.Paused, ConversationActivityType.Paused, actorUserId, clearAssignment: false, cancellationToken);

    public Task<ConversationOperationResult> ResumeAsync(Guid tenantId, Guid conversationId, Guid actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(tenantId, conversationId, ConversationStatus.AiActive, ConversationActivityType.HandoffToAi, actorUserId, clearAssignment: true, cancellationToken);

    public async Task PauseAllAsync(Guid tenantId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var conversationIds = await db.Conversations.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.Status == ConversationStatus.AiActive)
            .Select(current => current.Id)
            .ToListAsync(cancellationToken);

        foreach (var conversationId in conversationIds)
        {
            await TransitionAsync(tenantId, conversationId, ConversationStatus.Paused, ConversationActivityType.GlobalPaused, actorUserId, clearAssignment: false, cancellationToken);
        }
    }

    public async Task ResumeGlobalPauseAsync(Guid tenantId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var pausedConversationIds = await db.Conversations.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.Status == ConversationStatus.Paused)
            .Select(current => current.Id)
            .ToListAsync(cancellationToken);
        if (pausedConversationIds.Count == 0) return;

        var activities = await db.ConversationActivities.AsNoTracking()
            .Where(activity => activity.TenantId == tenantId && pausedConversationIds.Contains(activity.ConversationId))
            .OrderByDescending(activity => activity.CreatedAt)
            .ThenByDescending(activity => activity.Id)
            .Select(activity => new { activity.ConversationId, activity.Type })
            .ToListAsync(cancellationToken);
        var lastActivityByConversation = activities
            .GroupBy(activity => activity.ConversationId)
            .ToDictionary(group => group.Key, group => group.First().Type);

        foreach (var conversationId in pausedConversationIds)
        {
            if (lastActivityByConversation.GetValueOrDefault(conversationId) != ConversationActivityType.GlobalPaused) continue;
            await TransitionAsync(tenantId, conversationId, ConversationStatus.AiActive, ConversationActivityType.GlobalResumed, actorUserId, clearAssignment: true, cancellationToken);
        }
    }

    public Task<ConversationOperationResult> CloseAsync(Guid tenantId, Guid conversationId, Guid actorUserId, CancellationToken cancellationToken) =>
        TransitionAsync(tenantId, conversationId, ConversationStatus.Closed, ConversationActivityType.Closed, actorUserId, clearAssignment: false, cancellationToken);

    // The AI itself (not a merchant) requests human intervention — the handoff_to_human tool.
    // No specific human is chosen yet, so this pauses the AI rather than assigning HUMAN_ACTIVE;
    // a merchant then takes control explicitly. actorUserId is null: the system/AI performed it.
    public async Task<ConversationOperationResult> RequestHandoffAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var result = await TransitionAsync(tenantId, conversationId, ConversationStatus.Paused, ConversationActivityType.Paused, actorUserId: null, clearAssignment: false, cancellationToken);
        if (result == ConversationOperationResult.Success)
        {
            commerceEvents.Record(tenantId, CommerceEventType.HumanHandoff, conversationId: conversationId);
            await db.SaveChangesAsync(cancellationToken);
        }
        return result;
    }

    // Invoked by the inbound webhook pipeline; a customer writing again reopens a closed
    // conversation to the AI. No-op (not an error) when the conversation isn't closed.
    public Task<ConversationOperationResult> ReopenIfClosedAsync(Guid tenantId, Guid conversationId, bool aiResponsesEnabled, CancellationToken cancellationToken) =>
        TransitionAsync(tenantId, conversationId,
            aiResponsesEnabled ? ConversationStatus.AiActive : ConversationStatus.Paused,
            aiResponsesEnabled ? ConversationActivityType.Reopened : ConversationActivityType.GlobalPaused,
            actorUserId: null, clearAssignment: true, cancellationToken,
            requiredFromStatus: ConversationStatus.Closed, treatInvalidAsNoOp: true);

    // Atomically claims the right to answer a given inbound message: only succeeds once per
    // message and only while the AI is in control, preventing duplicate AI responses when
    // RespondAsync is triggered concurrently (webhook + manual "respond" click, retried webhook, etc.).
    public async Task<bool> TryClaimAiResponseAsync(Guid tenantId, Guid conversationId, Guid inboundMessageId, CancellationToken cancellationToken)
    {
        var updated = db.Database.IsRelational()
            ? await db.Conversations
                .Where(current => current.Id == conversationId && current.TenantId == tenantId
                    && current.Status == ConversationStatus.AiActive
                    && current.LastRespondedToMessageId != inboundMessageId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.LastRespondedToMessageId, inboundMessageId), cancellationToken)
            : await ClaimInMemoryAsync(tenantId, conversationId, inboundMessageId, cancellationToken);
        return updated == 1;
    }

    private async Task<int> ClaimInMemoryAsync(Guid tenantId, Guid conversationId, Guid inboundMessageId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null || conversation.Status != ConversationStatus.AiActive || conversation.LastRespondedToMessageId == inboundMessageId)
            return 0;
        conversation.LastRespondedToMessageId = inboundMessageId;
        await db.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private async Task<ConversationOperationResult> TransitionAsync(
        Guid tenantId, Guid conversationId, string toStatus, string activityType, Guid? actorUserId, bool clearAssignment,
        CancellationToken cancellationToken, string? requiredFromStatus = null, bool treatInvalidAsNoOp = false)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return ConversationOperationResult.NotFound;

        var fromStatus = conversation.Status;
        if (requiredFromStatus is not null && fromStatus != requiredFromStatus)
            return treatInvalidAsNoOp ? ConversationOperationResult.Success : ConversationOperationResult.InvalidTransition;
        if (!ConversationStateMachine.CanTransition(fromStatus, toStatus))
            return treatInvalidAsNoOp ? ConversationOperationResult.Success : ConversationOperationResult.InvalidTransition;

        var now = DateTimeOffset.UtcNow;
        var updated = db.Database.IsRelational()
            ? await db.Conversations
                .Where(current => current.Id == conversationId && current.TenantId == tenantId && current.Status == fromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.Status, toStatus)
                    .SetProperty(current => current.AssignedUserId, current => clearAssignment ? null : current.AssignedUserId)
                    .SetProperty(current => current.AssignedAt, current => clearAssignment ? (DateTimeOffset?)null : current.AssignedAt)
                    .SetProperty(current => current.LastActivityAt, now), cancellationToken)
            : ApplyInMemory(conversation, fromStatus, toStatus, now, clearAssignment ? null : conversation.AssignedUserId);
        if (updated != 1) return treatInvalidAsNoOp ? ConversationOperationResult.Success : ConversationOperationResult.InvalidTransition;

        conversation.Status = toStatus;
        conversation.LastActivityAt = now;
        if (clearAssignment)
        {
            conversation.AssignedUserId = null;
            conversation.AssignedAt = null;
        }
        db.ConversationActivities.Add(new ConversationActivity
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId,
            Type = activityType, FromStatus = fromStatus, ToStatus = toStatus, ActorUserId = actorUserId,
            AssignedUserId = conversation.AssignedUserId, CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return ConversationOperationResult.Success;
    }

    private static int ApplyInMemory(Conversation conversation, string expectedFrom, string toStatus, DateTimeOffset now, Guid? assignedUserId)
    {
        if (conversation.Status != expectedFrom) return 0;
        conversation.Status = toStatus;
        conversation.AssignedUserId = assignedUserId;
        conversation.LastActivityAt = now;
        return 1;
    }
}
