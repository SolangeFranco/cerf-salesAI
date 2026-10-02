using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Jobs;
using Vanta.Api.Modules.Observability;

namespace Vanta.Api.Modules.WhatsApp;

public sealed class WhatsAppDeliveryService(
    VantaDbContext db,
    IWhatsAppMessageSender sender,
    ILogger<WhatsAppDeliveryService> logger)
{
    public async Task<int> EnqueuePendingJobsAsync(BackgroundJobService jobs, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await db.ConversationMessages
            .Where(message => message.Direction == "Outbound" && message.DeliveryStatus == "Pending" &&
                (message.NextDeliveryAttemptAt == null || message.NextDeliveryAttemptAt <= now))
            .OrderBy(message => message.CreatedAt)
            .Take(20)
            .Select(message => new { message.Id, message.TenantId })
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            await jobs.EnqueueAsync(message.TenantId, BackgroundJobType.WhatsAppDelivery,
                $"whatsapp-delivery:{message.Id:N}", new { messageId = message.Id }, cancellationToken: cancellationToken);
        }

        return pending.Count;
    }

    public async Task ProcessMessageAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.ConversationMessages.FirstOrDefaultAsync(current => current.Id == messageId, cancellationToken);
        if (message is null || message.DeliveryStatus == "Sent") return;
        if (message.DeliveryStatus != "Pending") return;

        var conversation = await db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(current => current.Id == message.ConversationId && current.TenantId == message.TenantId, cancellationToken);
        if (conversation is null)
        {
            message.DeliveryStatus = "Failed";
            message.DeliveryError = "Conversation not found for outbound message.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var channelExternalId = await db.WhatsAppChannelAccounts.AsNoTracking()
                .Where(current => current.Id == conversation.ChannelAccountId && current.TenantId == message.TenantId)
                .Select(current => current.ExternalAccountId)
                .FirstOrDefaultAsync(cancellationToken);
            var sendResult = await sender.SendAsync(new WhatsAppOutboundMessage(
                conversation.CustomerExternalId, message.Text, message.ImageData, message.ImageMediaType, channelExternalId), cancellationToken);
            if (!string.IsNullOrWhiteSpace(sendResult.ProviderMessageId)) message.ExternalMessageId = sendResult.ProviderMessageId;
            message.DeliveryStatus = "Sent";
            message.DeliveryError = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            VantaTelemetry.Count(VantaTelemetry.WhatsAppFailures, "delivery_failed");
            message.DeliveryAttempts++;
            message.DeliveryError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
            message.DeliveryStatus = message.DeliveryAttempts >= 5 ? "Failed" : "Pending";
            message.NextDeliveryAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, Math.Min(message.DeliveryAttempts, 5)));
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<int> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await db.ConversationMessages
            .Where(message => message.Direction == "Outbound" && message.DeliveryStatus == "Pending" &&
                (message.NextDeliveryAttemptAt == null || message.NextDeliveryAttemptAt <= now))
            .OrderBy(message => message.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in pending)
        {
            var conversation = await db.Conversations.AsNoTracking()
                .FirstOrDefaultAsync(current => current.Id == message.ConversationId && current.TenantId == message.TenantId, cancellationToken);
            if (conversation is null)
            {
                message.DeliveryStatus = "Failed";
                message.DeliveryError = "Conversation not found for outbound message.";
                continue;
            }

            try
            {
                var channelExternalId = await db.WhatsAppChannelAccounts.AsNoTracking()
                    .Where(current => current.Id == conversation.ChannelAccountId && current.TenantId == message.TenantId)
                    .Select(current => current.ExternalAccountId)
                    .FirstOrDefaultAsync(cancellationToken);
                var sendResult = await sender.SendAsync(new WhatsAppOutboundMessage(
                    conversation.CustomerExternalId,
                    message.Text,
                    message.ImageData,
                    message.ImageMediaType, channelExternalId), cancellationToken);
                if (!string.IsNullOrWhiteSpace(sendResult.ProviderMessageId))
                    message.ExternalMessageId = sendResult.ProviderMessageId;
                message.DeliveryStatus = "Sent";
                message.DeliveryError = null;
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
            {
                message.DeliveryAttempts++;
                message.DeliveryError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
                message.DeliveryStatus = message.DeliveryAttempts >= 5 ? "Failed" : "Pending";
                message.NextDeliveryAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, Math.Min(message.DeliveryAttempts, 5)));
                logger.LogWarning(exception, "WhatsApp message {MessageId} delivery attempt {Attempt} failed", message.Id, message.DeliveryAttempts);
            }
        }

        if (pending.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }
}
