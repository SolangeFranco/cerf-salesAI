using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Observability;

namespace Vanta.Api.Modules.WhatsApp;

public enum WhatsAppEventOutcome
{
    Processed,
    Duplicate,
    ChannelNotFound,
    MediaRejected,
    MediaDownloadFailed,
}

public sealed record WhatsAppEventResult(WhatsAppInboundEvent Event, WhatsAppEventOutcome Outcome, Guid? ConversationId = null, string? Error = null);

public sealed record WhatsAppWebhookProcessResult(bool Validated, IReadOnlyList<WhatsAppEventResult> Events);

/// <summary>
/// Owns the WhatsApp inbound webhook pipeline shared by every provider:
/// validate -> identify provider (caller-supplied) -> normalize event -> idempotency check ->
/// persist -> process. Endpoints only translate HTTP in/out; all business behavior lives here so
/// mock and real providers are handled identically.
/// </summary>
public sealed class WhatsAppWebhookProcessor(
    VantaDbContext db,
    IMetaMediaClient mediaClient,
    IAudioTranscriptionProvider audioTranscription,
    SalesAgentService agent,
    ConversationService conversations,
    CommerceEventRecorder commerceEvents,
    ILogger<WhatsAppWebhookProcessor> logger)
{
    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp"];
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private const int MaxMediaBytes = 10 * 1024 * 1024;
    private static readonly string[] AllowedMediaKinds = ["image", "video", "audio", "document", "sticker"];

    public async Task<WhatsAppWebhookProcessResult> ProcessAsync(
        IWhatsAppProvider provider,
        WhatsAppWebhookSignal signal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var activity = VantaTelemetry.ActivitySource.StartActivity("whatsapp.webhook");
        activity?.SetTag("whatsapp.provider", provider.Name);
        try
        {
            return await ProcessCoreAsync(provider, signal, correlationId, cancellationToken);
        }
        catch (Exception)
        {
            VantaTelemetry.Count(VantaTelemetry.WebhookFailures, "processing_exception");
            activity?.SetStatus(ActivityStatusCode.Error, "processing_exception");
            throw;
        }
    }

    private async Task<WhatsAppWebhookProcessResult> ProcessCoreAsync(
        IWhatsAppProvider provider,
        WhatsAppWebhookSignal signal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        using var _ = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["WhatsAppProvider"] = provider.Name,
        });

        if (!provider.ValidateWebhook(signal))
        {
            VantaTelemetry.Count(VantaTelemetry.WebhookFailures, "signature_rejected");
            logger.LogWarning("WhatsApp webhook rejected: signature validation failed.");
            return new WhatsAppWebhookProcessResult(false, []);
        }

        var events = provider.ParseWebhook(signal);
        if (events.Count == 0)
        {
            logger.LogInformation("WhatsApp webhook validated but produced no recognized events.");
        }

        var results = new List<WhatsAppEventResult>(events.Count);
        foreach (var @event in events)
        {
            using var eventScope = logger.BeginScope(new Dictionary<string, object> { ["WhatsAppEventId"] = @event.EventId });
            results.Add(await ProcessEventAsync(provider, @event, cancellationToken));
        }

        return new WhatsAppWebhookProcessResult(true, results);
    }

    private async Task<WhatsAppEventResult> ProcessEventAsync(IWhatsAppProvider provider, WhatsAppInboundEvent @event, CancellationToken cancellationToken)
    {
        var account = await db.WhatsAppChannelAccounts.FirstOrDefaultAsync(
            current => current.Provider == provider.Name && current.ExternalAccountId == @event.ChannelExternalId && current.IsActive,
            cancellationToken);
        if (account is null)
        {
            logger.LogWarning("No active WhatsApp channel account for {Provider}/{ChannelExternalId}.", provider.Name, @event.ChannelExternalId);
            return new WhatsAppEventResult(@event, WhatsAppEventOutcome.ChannelNotFound);
        }

        using var tenantScope = logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = account.TenantId });

        var alreadyReceived = await db.WhatsAppWebhookEvents.AnyAsync(
            existing => existing.TenantId == account.TenantId && existing.Provider == provider.Name && existing.EventId == @event.EventId,
            cancellationToken);
        if (alreadyReceived)
        {
            logger.LogInformation("Duplicate WhatsApp webhook event ignored.");
            return new WhatsAppEventResult(@event, WhatsAppEventOutcome.Duplicate);
        }

        var result = @event switch
        {
            WhatsAppInboundMessageEvent message => await ProcessInboundMessageAsync(account, message, cancellationToken),
            WhatsAppDeliveryStatusEvent status => await ProcessDeliveryStatusAsync(account, status, cancellationToken),
            WhatsAppContactUpdateEvent contact => await ProcessContactUpdateAsync(account, contact, cancellationToken),
            WhatsAppChatUpdateEvent chat => await ProcessChatUpdateAsync(account, chat, cancellationToken),
            WhatsAppReactionEvent reaction => await ProcessReactionAsync(account, reaction, cancellationToken),
            WhatsAppIdentityMappingEvent identity => await ProcessIdentityMappingAsync(account, identity, cancellationToken),
            _ => new WhatsAppEventResult(@event, WhatsAppEventOutcome.ChannelNotFound),
        };

        db.WhatsAppWebhookEvents.Add(new WhatsAppWebhookEvent
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            Provider = provider.Name,
            EventId = @event.EventId,
            ReceivedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        return result;
    }

    private async Task<WhatsAppEventResult> ProcessContactUpdateAsync(
        WhatsAppChannelAccount account,
        WhatsAppContactUpdateEvent contact,
        CancellationToken cancellationToken)
    {
        var customerIds = await db.Conversations.AsNoTracking()
            .Where(conversation => conversation.TenantId == account.TenantId &&
                conversation.ChannelAccountId == account.Id &&
                conversation.CustomerExternalId == contact.CustomerExternalId &&
                conversation.CustomerId != null)
            .Select(conversation => conversation.CustomerId!.Value)
            .ToListAsync(cancellationToken);

        var customers = await db.Customers.Where(current =>
            current.TenantId == account.TenantId &&
            (current.Phone == contact.CustomerExternalId || customerIds.Contains(current.Id)))
            .ToListAsync(cancellationToken);
        foreach (var customer in customers)
        {
            if (!string.IsNullOrWhiteSpace(contact.CustomerName) && !IsMaskedContactName(contact.CustomerName)) customer.Name = contact.CustomerName.Trim();
            if (!string.IsNullOrWhiteSpace(contact.CustomerImageUrl)) customer.ProfileImageUrl = contact.CustomerImageUrl.Trim();
            customer.UpdatedAt = DateTimeOffset.UtcNow;
        }
        if (customers.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return new WhatsAppEventResult(contact, WhatsAppEventOutcome.Processed);
    }

    private async Task<WhatsAppEventResult> ProcessIdentityMappingAsync(
        WhatsAppChannelAccount account,
        WhatsAppIdentityMappingEvent identity,
        CancellationToken cancellationToken)
    {
        if (identity.SourceExternalId == identity.CustomerExternalId)
            return new WhatsAppEventResult(identity, WhatsAppEventOutcome.Processed);

        var sourceConversations = await db.Conversations.Where(conversation =>
            conversation.TenantId == account.TenantId &&
            conversation.ChannelAccountId == account.Id &&
            conversation.CustomerExternalId == identity.SourceExternalId &&
            !conversation.IsGroup && !conversation.IsChannel)
            .ToListAsync(cancellationToken);
        if (sourceConversations.Count == 0)
            return new WhatsAppEventResult(identity, WhatsAppEventOutcome.Processed);

        var sourceCustomerIds = sourceConversations.Where(conversation => conversation.CustomerId is not null)
            .Select(conversation => conversation.CustomerId!.Value)
            .ToList();
        var canonicalCustomer = await db.Customers.FirstOrDefaultAsync(customer =>
            customer.TenantId == account.TenantId && customer.Phone == identity.CustomerExternalId,
            cancellationToken);
        if (canonicalCustomer is null)
        {
            canonicalCustomer = await db.Customers.FirstOrDefaultAsync(customer =>
                customer.TenantId == account.TenantId && sourceCustomerIds.Contains(customer.Id),
                cancellationToken);
            if (canonicalCustomer is null)
                canonicalCustomer = await GetOrCreateCustomerAsync(account.TenantId, identity.CustomerExternalId, cancellationToken);
            else
                canonicalCustomer.Phone = identity.CustomerExternalId;
        }
        if (!string.IsNullOrWhiteSpace(identity.CustomerName) && !IsMaskedContactName(identity.CustomerName)) canonicalCustomer.Name = identity.CustomerName.Trim();
        if (!string.IsNullOrWhiteSpace(identity.CustomerImageUrl)) canonicalCustomer.ProfileImageUrl = identity.CustomerImageUrl.Trim();
        canonicalCustomer.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var conversation in sourceConversations)
        {
            var alreadyCanonical = await db.Conversations.AnyAsync(current =>
                current.TenantId == account.TenantId && current.ChannelAccountId == account.Id &&
                current.CustomerExternalId == identity.CustomerExternalId && current.Id != conversation.Id,
                cancellationToken);
            if (!alreadyCanonical) conversation.CustomerExternalId = identity.CustomerExternalId;
            conversation.CustomerId = canonicalCustomer.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new WhatsAppEventResult(identity, WhatsAppEventOutcome.Processed, sourceConversations[0].Id);
    }

    private async Task<WhatsAppEventResult> ProcessChatUpdateAsync(
        WhatsAppChannelAccount account,
        WhatsAppChatUpdateEvent chat,
        CancellationToken cancellationToken)
    {
        Customer? customer = null;
        if (!chat.IsGroup && !chat.IsChannel)
        {
            customer = await GetOrCreateCustomerAsync(account.TenantId, chat.CustomerExternalId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(chat.CustomerName) && !IsMaskedContactName(chat.CustomerName)) customer.Name = chat.CustomerName.Trim();
            if (!string.IsNullOrWhiteSpace(chat.CustomerImageUrl)) customer.ProfileImageUrl = chat.CustomerImageUrl.Trim();
            customer.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var activityAt = chat.LastActivityAt ?? DateTimeOffset.UtcNow;
        var conversation = await db.Conversations.FirstOrDefaultAsync(current =>
            current.TenantId == account.TenantId && current.ChannelAccountId == account.Id && current.CustomerExternalId == chat.CustomerExternalId,
            cancellationToken);
        if (conversation is null)
        {
            var aiResponsesEnabled = await IsAiResponsesEnabledAsync(account.TenantId, cancellationToken);
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                TenantId = account.TenantId,
                ChannelAccountId = account.Id,
                CustomerId = customer?.Id,
                CustomerExternalId = chat.CustomerExternalId,
                DisplayName = chat.IsGroup || chat.IsChannel ? chat.CustomerName?.Trim() : null,
                IsGroup = chat.IsGroup,
                IsChannel = chat.IsChannel,
                Status = aiResponsesEnabled ? ConversationStatus.AiActive : ConversationStatus.Paused,
                LastMessageAt = activityAt,
                LastActivityAt = activityAt,
            };
            db.Conversations.Add(conversation);
            if (!aiResponsesEnabled)
            {
                db.ConversationActivities.Add(new ConversationActivity
                {
                    Id = Guid.NewGuid(), TenantId = account.TenantId, ConversationId = conversation.Id,
                    Type = ConversationActivityType.GlobalPaused, ToStatus = ConversationStatus.Paused, CreatedAt = activityAt,
                });
            }
        }
        else
        {
            conversation.CustomerId ??= customer?.Id;
            if ((chat.IsGroup || chat.IsChannel) && !string.IsNullOrWhiteSpace(chat.CustomerName)) conversation.DisplayName = chat.CustomerName.Trim();
            conversation.IsGroup = chat.IsGroup;
            conversation.IsChannel = chat.IsChannel;
            if (activityAt > conversation.LastMessageAt) conversation.LastMessageAt = activityAt;
            if (activityAt > conversation.LastActivityAt) conversation.LastActivityAt = activityAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new WhatsAppEventResult(chat, WhatsAppEventOutcome.Processed, conversation.Id);
    }

    private async Task<WhatsAppEventResult> ProcessReactionAsync(
        WhatsAppChannelAccount account,
        WhatsAppReactionEvent reaction,
        CancellationToken cancellationToken)
    {
        var message = await db.ConversationMessages.FirstOrDefaultAsync(current =>
            current.TenantId == account.TenantId && current.ExternalMessageId == reaction.MessageExternalId,
            cancellationToken);
        if (message is null) return new WhatsAppEventResult(reaction, WhatsAppEventOutcome.Processed);

        var reactions = string.IsNullOrWhiteSpace(message.ReactionsJson)
            ? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            : JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(message.ReactionsJson) ?? new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var senders in reactions.Values) senders.Remove(reaction.SenderExternalId);
        if (!string.IsNullOrWhiteSpace(reaction.Emoji))
        {
            if (!reactions.TryGetValue(reaction.Emoji, out var senders))
            {
                senders = new HashSet<string>(StringComparer.Ordinal);
                reactions[reaction.Emoji] = senders;
            }
            senders.Add(reaction.SenderExternalId);
        }
        foreach (var emoji in reactions.Where(entry => entry.Value.Count == 0).Select(entry => entry.Key).ToList()) reactions.Remove(emoji);
        message.ReactionsJson = reactions.Count == 0 ? null : JsonSerializer.Serialize(reactions);
        await db.SaveChangesAsync(cancellationToken);
        return new WhatsAppEventResult(reaction, WhatsAppEventOutcome.Processed, message.ConversationId);
    }

    private async Task<WhatsAppEventResult> ProcessInboundMessageAsync(
        WhatsAppChannelAccount account,
        WhatsAppInboundMessageEvent message,
        CancellationToken cancellationToken)
    {
        var duplicateMessage = await db.ConversationMessages.AnyAsync(
            existing => existing.TenantId == account.TenantId && existing.ExternalMessageId == message.MessageExternalId,
            cancellationToken);
        if (duplicateMessage)
        {
            logger.LogInformation("Duplicate WhatsApp inbound message ignored.");
            return new WhatsAppEventResult(message, WhatsAppEventOutcome.Duplicate);
        }

        byte[]? imageData = null;
        string? imageMediaType = null;
        byte[]? mediaData = null;
        string? mediaMimeType = null;
        string? mediaFileName = null;
        string? mediaKind = null;
        if (!string.IsNullOrWhiteSpace(message.InlineImageBase64))
        {
            if (string.IsNullOrWhiteSpace(message.InlineImageMediaType) || !AllowedImageTypes.Contains(message.InlineImageMediaType))
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "unsupported_media_type");
            try
            {
                imageData = Convert.FromBase64String(message.InlineImageBase64);
            }
            catch (FormatException)
            {
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "invalid_base64");
            }
            if (imageData.Length > MaxImageBytes)
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "media_too_large");
            imageMediaType = message.InlineImageMediaType;
        }
        else if (!string.IsNullOrWhiteSpace(message.MediaId))
        {
            var media = await mediaClient.DownloadAsync(message.MediaId, cancellationToken);
            if (media is null)
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaDownloadFailed);
            if (!AllowedImageTypes.Contains(media.MediaType) || media.Data.Length > MaxImageBytes)
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "unsupported_media_type_or_size");
            imageData = media.Data;
            imageMediaType = media.MediaType;
        }

        if (!string.IsNullOrWhiteSpace(message.InlineMediaBase64))
        {
            if (string.IsNullOrWhiteSpace(message.InlineMediaMimeType) || !AllowedMediaKinds.Contains(message.InlineMediaKind))
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "unsupported_media_type");
            try
            {
                mediaData = Convert.FromBase64String(message.InlineMediaBase64);
            }
            catch (FormatException)
            {
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "invalid_base64");
            }
            if (mediaData.Length > MaxMediaBytes)
                return new WhatsAppEventResult(message, WhatsAppEventOutcome.MediaRejected, Error: "media_too_large");
            mediaMimeType = message.InlineMediaMimeType;
            mediaFileName = message.InlineMediaFileName?.Trim();
            mediaKind = message.InlineMediaKind;
        }

        var audioTranscript = mediaKind == "audio" && mediaData is not null && !string.IsNullOrWhiteSpace(mediaMimeType)
            ? await audioTranscription.TranscribeAsync(mediaData, mediaMimeType, cancellationToken)
            : null;

        if (string.IsNullOrWhiteSpace(message.Text) && imageData is null && mediaData is null && !message.MediaUnavailable)
            return new WhatsAppEventResult(message, WhatsAppEventOutcome.Processed);

        Customer? customer = null;
        if (!message.IsGroup && !message.IsChannel)
        {
            customer = await GetOrCreateCustomerAsync(account.TenantId, message.CustomerExternalId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(message.CustomerName) && !message.IsFromMe && !IsMaskedContactName(message.CustomerName) &&
                (customer.Name.StartsWith("WhatsApp ", StringComparison.Ordinal) || customer.Name == message.CustomerExternalId || IsMaskedContactName(customer.Name)))
            {
                customer.Name = message.CustomerName.Trim();
                customer.UpdatedAt = DateTimeOffset.UtcNow;
            }
            if (!string.IsNullOrWhiteSpace(message.CustomerImageUrl)) customer.ProfileImageUrl = message.CustomerImageUrl.Trim();
        }
        var aiResponsesEnabledForTenant = await IsAiResponsesEnabledAsync(account.TenantId, cancellationToken);
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            current => current.TenantId == account.TenantId && current.ChannelAccountId == account.Id && current.CustomerExternalId == message.CustomerExternalId,
            cancellationToken);
        var now = message.MessageCreatedAt ?? DateTimeOffset.UtcNow;
        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                TenantId = account.TenantId,
                ChannelAccountId = account.Id,
                CustomerId = customer?.Id,
                CustomerExternalId = message.CustomerExternalId,
                IsGroup = message.IsGroup,
                IsChannel = message.IsChannel,
                Status = aiResponsesEnabledForTenant ? ConversationStatus.AiActive : ConversationStatus.Paused,
                LastMessageAt = now,
                LastActivityAt = now,
            };
            db.Conversations.Add(conversation);
            if (!aiResponsesEnabledForTenant)
            {
                db.ConversationActivities.Add(new ConversationActivity
                {
                    Id = Guid.NewGuid(), TenantId = account.TenantId, ConversationId = conversation.Id,
                    Type = ConversationActivityType.GlobalPaused, ToStatus = ConversationStatus.Paused, CreatedAt = now,
                });
            }
            commerceEvents.Record(account.TenantId, CommerceEventType.ConversationStarted, conversationId: conversation.Id, customerId: customer?.Id);
        }
        else
        {
            conversation.CustomerId ??= customer?.Id;
            conversation.IsGroup = message.IsGroup;
            conversation.IsChannel = message.IsChannel;
            if (now > conversation.LastMessageAt) conversation.LastMessageAt = now;
            conversation.LastActivityAt = now;
        }
        if (customer is not null) await AttachCustomerToOrdersAsync(account.TenantId, conversation.Id, customer.Id, cancellationToken);

        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            ConversationId = conversation.Id,
            ExternalMessageId = message.MessageExternalId,
            Direction = message.IsFromMe ? "Outbound" : "Inbound",
            Text = message.Text.Trim(),
            ImageData = imageData,
            ImageMediaType = imageMediaType,
            MediaData = mediaData,
            MediaMimeType = mediaMimeType,
            MediaFileName = mediaFileName,
            MediaKind = mediaKind,
            MediaUnavailable = message.MediaUnavailable,
            AudioTranscript = audioTranscript,
            QuotedExternalMessageId = message.QuotedMessageExternalId,
            QuotedText = message.QuotedText,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        // A customer writing again on a closed conversation hands it back to the AI.
        await conversations.ReopenIfClosedAsync(account.TenantId, conversation.Id, aiResponsesEnabledForTenant, cancellationToken);

        if (conversation.Status == ConversationStatus.AiActive && !message.IsFromMe && !message.IsHistorical && (!string.IsNullOrWhiteSpace(message.Text) || !string.IsNullOrWhiteSpace(audioTranscript) || !string.IsNullOrWhiteSpace(message.InlineImageBase64) || !string.IsNullOrWhiteSpace(message.MediaId)))
            await agent.RespondAsync(account.TenantId, conversation.Id, cancellationToken);

        return new WhatsAppEventResult(message, WhatsAppEventOutcome.Processed, conversation.Id);
    }

    private Task<bool> IsAiResponsesEnabledAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.AiResponsesEnabled)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<WhatsAppEventResult> ProcessDeliveryStatusAsync(
        WhatsAppChannelAccount account,
        WhatsAppDeliveryStatusEvent status,
        CancellationToken cancellationToken)
    {
        var outbound = await db.ConversationMessages.FirstOrDefaultAsync(
            message => message.TenantId == account.TenantId && message.ExternalMessageId == status.MessageExternalId && message.Direction == "Outbound",
            cancellationToken);
        if (outbound is null)
        {
            logger.LogInformation("No matching outbound message found for WhatsApp delivery status update.");
            return new WhatsAppEventResult(status, WhatsAppEventOutcome.Processed);
        }

        if (MetaWebhookParser.ShouldAdvanceDeliveryStatus(outbound.DeliveryStatus, status.Status))
        {
            outbound.DeliveryStatus = status.Status;
            outbound.DeliveryError = status.Error;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new WhatsAppEventResult(status, WhatsAppEventOutcome.Processed, outbound.ConversationId);
    }

    // WhatsApp hides the display name for phone-number-privacy contacts and reports a masked
    // string like "+595∙∙∙∙∙∙∙96" as the contact/pushName instead; never treat that as a real name.
    private static bool IsMaskedContactName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return false;
        var hasMaskChar = false;
        foreach (var character in trimmed)
        {
            if (character is '∙' or '•' or '●' or '·') { hasMaskChar = true; continue; }
            if (char.IsDigit(character) || character is '+' or ' ' or '-') continue;
            return false;
        }
        return hasMaskChar;
    }

    private async Task<Customer> GetOrCreateCustomerAsync(Guid tenantId, string externalPhone, CancellationToken cancellationToken)
    {
        var phone = externalPhone.Trim();
        var customer = await db.Customers.FirstOrDefaultAsync(current => current.TenantId == tenantId && current.Phone == phone, cancellationToken);
        if (customer is not null) return customer;

        customer = new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = $"WhatsApp {phone}",
            Phone = phone,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Customers.Add(customer);
        return customer;
    }

    private async Task AttachCustomerToOrdersAsync(Guid tenantId, Guid conversationId, Guid customerId, CancellationToken cancellationToken)
    {
        var orders = await db.Orders
            .Where(order => order.TenantId == tenantId && order.ConversationId == conversationId && order.CustomerId == null)
            .ToListAsync(cancellationToken);
        foreach (var order in orders)
        {
            order.CustomerId = customerId;
        }
    }
}
