using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Customers;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record ConversationResponse(Guid Id, string CustomerExternalId, string CustomerName, string? CustomerImageUrl, bool IsGroup, bool IsChannel, string Status, string? CurrentIntent, Guid? AssignedUserId, DateTimeOffset LastMessageAt, DateTimeOffset LastActivityAt);
public sealed record ConnectMockChannelRequest([property: Required] string ExternalAccountId, [property: Required] string DisplayName);
public sealed record ConnectMetaChannelRequest([property: Required] string PhoneNumberId, [property: Required] string DisplayName);
public sealed record ConnectLinkedDeviceChannelRequest([property: Required] string DisplayName);
public sealed record SendMockMessageRequest([property: Required] string Text, [property: Required] string IdempotencyKey);
public sealed record SendConversationMessageRequest([property: Required] string Text, [property: StringLength(200)] string? QuotedMessageId);
public sealed record ReactToMessageRequest([property: Required, StringLength(16)] string Emoji);

public static class WhatsAppEndpoints
{
    public static IEndpointRouteBuilder MapWhatsAppModule(this IEndpointRouteBuilder endpoints, bool enableMockEndpoints)
    {
        if (enableMockEndpoints)
        {
            endpoints.MapPost("/api/webhooks/whatsapp/mock", ReceiveMockAsync).AllowAnonymous().RequireRateLimiting("webhook");
            endpoints.MapPost("/api/channels/whatsapp/mock", ConnectMockAsync).RequireAuthorization();
            endpoints.MapPost("/api/conversations/{conversationId:guid}/messages", SendMockAsync).RequireAuthorization();
            endpoints.MapPost("/api/conversations/{conversationId:guid}/agent/mock-respond", MockAgentRespondAsync).RequireAuthorization();
        }

        endpoints.MapGet("/api/webhooks/whatsapp/meta", VerifyMetaWebhookAsync).AllowAnonymous();
        endpoints.MapPost("/api/webhooks/whatsapp/meta", ReceiveMetaWebhookAsync).AllowAnonymous().RequireRateLimiting("webhook");
        endpoints.MapPost("/api/channels/whatsapp/meta", ConnectMetaAsync).RequireAuthorization();
        endpoints.MapDelete("/api/channels/whatsapp/meta/{channelId:guid}", DisconnectMetaAsync).RequireAuthorization();
        endpoints.MapPost("/api/channels/whatsapp/linked-device", ConnectLinkedDeviceAsync).RequireAuthorization();
        endpoints.MapPost("/api/channels/whatsapp/linked-device/{channelId:guid}/reconnect", ReconnectLinkedDeviceAsync).RequireAuthorization();
        endpoints.MapGet("/api/channels/whatsapp/linked-device/{channelId:guid}", LinkedDeviceStatusAsync).RequireAuthorization();
        endpoints.MapDelete("/api/channels/whatsapp/linked-device/{channelId:guid}", DisconnectLinkedDeviceAsync).RequireAuthorization();
        endpoints.MapGet("/api/channels/whatsapp", ListChannelsAsync).RequireAuthorization();
        endpoints.MapGet("/api/conversations", ListAsync).RequireAuthorization();
        endpoints.MapGet("/api/conversations/{conversationId:guid}/messages", MessagesAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/messages/{messageId:guid}/retry", RetryMessageAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/messages/send", SendLinkedDeviceMessageAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/messages/{messageId:guid}/react", ReactToMessageAsync).RequireAuthorization();
        endpoints.MapDelete("/api/conversations/{conversationId:guid}/messages/{messageId:guid}", DeleteMessageAsync).RequireAuthorization();
        endpoints.MapPost("/api/conversations/{conversationId:guid}/messages/image", SendImageAsync).RequireAuthorization().DisableAntiforgery();
        endpoints.MapPost("/api/webhooks/whatsapp/linked-device", ReceiveLinkedDeviceWebhookAsync)
            .AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());
        endpoints.MapGet("/api/ai/tools/search-products", SearchProductsAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> MockAgentRespondAsync(Guid conversationId, ClaimsPrincipal user, SalesAgentService agent, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var result = await agent.RespondAsync(tenantId, conversationId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static IResult VerifyMetaWebhookAsync(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge,
        IOptions<WhatsAppProviderOptions> options)
    {
        var configuration = options.Value;
        if (hubMode != "subscribe" || string.IsNullOrWhiteSpace(hubChallenge) ||
            string.IsNullOrWhiteSpace(configuration.WebhookVerifyToken) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(hubVerifyToken ?? string.Empty),
                Encoding.UTF8.GetBytes(configuration.WebhookVerifyToken)))
        {
            return Results.Unauthorized();
        }
        return Results.Text(hubChallenge);
    }

    private static async Task<IResult> ReceiveMetaWebhookAsync(
        HttpRequest request,
        IEnumerable<IWhatsAppProvider> providers,
        WhatsAppWebhookProcessor processor,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var provider = providers.First(current => current.Name == "meta");
        var (signal, correlationId) = await ReadSignalAsync(request, cancellationToken);

        WhatsAppWebhookProcessResult result;
        try
        {
            result = await processor.ProcessAsync(provider, signal, correlationId, cancellationToken);
        }
        catch (WhatsAppMalformedPayloadException exception)
        {
            logger.LogWarning(exception, "Malformed Meta webhook payload. CorrelationId={CorrelationId}", correlationId);
            return WithCorrelationId(request, Results.BadRequest(new { error = "El payload del webhook no es válido.", correlationId }), correlationId);
        }

        if (!result.Validated) return WithCorrelationId(request, Results.Unauthorized(), correlationId);

        if (result.Events.Count == 0)
            return WithCorrelationId(request, Results.Ok(new { received = true, processed = false, reason = "unrecognized_event", correlationId }), correlationId);

        var statusEvents = result.Events.Where(current => current.Event is WhatsAppDeliveryStatusEvent).ToList();
        if (statusEvents.Count > 0)
        {
            return WithCorrelationId(request, Results.Ok(new { received = true, processed = true, statuses = statusEvents.Count, correlationId }), correlationId);
        }

        var messageResult = result.Events[0];
        return WithCorrelationId(request, messageResult.Outcome switch
        {
            WhatsAppEventOutcome.Duplicate => Results.Ok(new { received = true, processed = true, duplicate = true, correlationId }),
            WhatsAppEventOutcome.ChannelNotFound => Results.NotFound(new { error = "No hay un canal Meta activo configurado para este Phone Number ID.", correlationId }),
            WhatsAppEventOutcome.MediaDownloadFailed => Results.StatusCode(StatusCodes.Status502BadGateway),
            WhatsAppEventOutcome.MediaRejected => Results.BadRequest(new { error = "La imagen de Meta no cumple el tipo o tamaño permitido.", correlationId }),
            _ => Results.Ok(new { received = true, processed = true, duplicate = false, conversationId = messageResult.ConversationId, correlationId }),
        }, correlationId);
    }

    private static async Task<(WhatsAppWebhookSignal Signal, string CorrelationId)> ReadSignalAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;

        var headers = request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString());
        var correlationId = request.Headers.TryGetValue("X-Correlation-Id", out var provided) && !string.IsNullOrWhiteSpace(provided)
            ? provided.ToString()
            : Guid.NewGuid().ToString("N");
        return (new WhatsAppWebhookSignal(rawBody, headers), correlationId);
    }

    private static IResult WithCorrelationId(HttpRequest request, IResult result, string correlationId)
    {
        request.HttpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        return result;
    }

    private static async Task<IResult> SendMockAsync(Guid conversationId, SendMockMessageRequest request, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        if (request.Text.Trim().Length is < 1 or > 10000) return Results.BadRequest(new { error = "El mensaje debe tener entre 1 y 10000 caracteres." });
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return Results.NotFound();
        var externalMessageId = $"mock-out-{request.IdempotencyKey}";
        var duplicate = await db.ConversationMessages.AnyAsync(message => message.TenantId == tenantId && message.ExternalMessageId == externalMessageId, cancellationToken);
        if (duplicate) return Results.Ok(new { processed = true, duplicate = true });
        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageAt = now;
        db.ConversationMessages.Add(new ConversationMessage { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversation.Id, ExternalMessageId = externalMessageId, Direction = "Outbound", Text = request.Text.Trim(), DeliveryStatus = "Pending", NextDeliveryAttemptAt = now, CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { processed = true, duplicate = false });
    }

    private static async Task<IResult> RetryMessageAsync(Guid conversationId, Guid messageId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var message = await db.ConversationMessages.FirstOrDefaultAsync(current =>
            current.Id == messageId && current.ConversationId == conversationId && current.TenantId == tenantId &&
            current.Direction == "Outbound" && current.DeliveryStatus == "Failed", cancellationToken);
        if (message is null) return Results.NotFound();
        message.DeliveryStatus = "Pending";
        message.NextDeliveryAttemptAt = DateTimeOffset.UtcNow;
        message.DeliveryError = null;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { queued = true });
    }

    private static async Task<IResult> SendLinkedDeviceMessageAsync(Guid conversationId, SendConversationMessageRequest request, ClaimsPrincipal user, VantaDbContext db, IHttpClientFactory httpClientFactory, IOptions<WhatsAppProviderOptions> options, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Trim().Length > 10000) return Results.BadRequest(new { error = "El mensaje debe tener entre 1 y 10000 caracteres." });
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return Results.NotFound();
        var channel = await db.WhatsAppChannelAccounts.AsNoTracking().FirstOrDefaultAsync(current => current.Id == conversation.ChannelAccountId && current.TenantId == tenantId, cancellationToken);
        if (channel?.Provider != "linked-device" || !channel.ExternalAccountId.StartsWith("linked:", StringComparison.Ordinal)) return Results.BadRequest(new { error = "Esta acción requiere un dispositivo WhatsApp vinculado por QR." });
        var quoted = string.IsNullOrWhiteSpace(request.QuotedMessageId) ? null : await db.ConversationMessages.AsNoTracking().FirstOrDefaultAsync(message => message.Id.ToString() == request.QuotedMessageId && message.ConversationId == conversationId && message.TenantId == tenantId, cancellationToken);
        var sessionId = channel.ExternalAccountId["linked:".Length..];
        using var bridgeRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}/messages") { Content = JsonContent.Create(new { to = conversation.CustomerExternalId, text = request.Text.Trim(), quotedMessageId = quoted?.ExternalMessageId, quotedFromMe = quoted?.Direction == "Outbound" }) };
        bridgeRequest.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var bridgeResponse = await httpClientFactory.CreateClient().SendAsync(bridgeRequest, cancellationToken);
        if (!bridgeResponse.IsSuccessStatusCode) return Results.Problem("WhatsApp no pudo enviar el mensaje.", statusCode: StatusCodes.Status502BadGateway);
        var result = await bridgeResponse.Content.ReadFromJsonAsync<BridgeMessageResponse>(cancellationToken: cancellationToken);
        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageAt = now;
        db.ConversationMessages.Add(new ConversationMessage { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, ExternalMessageId = result?.MessageId ?? $"linked-out-{Guid.NewGuid():N}", Direction = "Outbound", Text = request.Text.Trim(), QuotedExternalMessageId = quoted?.ExternalMessageId, QuotedText = quoted?.Text, DeliveryStatus = "Sent", CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { sent = true });
    }

    private static async Task<IResult> ReactToMessageAsync(Guid conversationId, Guid messageId, ReactToMessageRequest request, ClaimsPrincipal user, VantaDbContext db, IHttpClientFactory httpClientFactory, IOptions<WhatsAppProviderOptions> options, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var message = await db.ConversationMessages.FirstOrDefaultAsync(current => current.Id == messageId && current.ConversationId == conversationId && current.TenantId == tenantId, cancellationToken);
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        var channel = conversation is null ? null : await db.WhatsAppChannelAccounts.AsNoTracking().FirstOrDefaultAsync(current => current.Id == conversation.ChannelAccountId && current.TenantId == tenantId, cancellationToken);
        if (message is null || conversation is null || channel?.Provider != "linked-device") return Results.NotFound();
        var sessionId = channel.ExternalAccountId["linked:".Length..];
        using var bridgeRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}/reactions") { Content = JsonContent.Create(new { to = conversation.CustomerExternalId, messageId = message.ExternalMessageId, fromMe = message.Direction == "Outbound", emoji = request.Emoji.Trim() }) };
        bridgeRequest.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var bridgeResponse = await httpClientFactory.CreateClient().SendAsync(bridgeRequest, cancellationToken);
        return bridgeResponse.IsSuccessStatusCode ? Results.Ok(new { reacted = true }) : Results.Problem("WhatsApp no pudo registrar la reacción.", statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> DeleteMessageAsync(Guid conversationId, Guid messageId, ClaimsPrincipal user, VantaDbContext db, IHttpClientFactory httpClientFactory, IOptions<WhatsAppProviderOptions> options, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var message = await db.ConversationMessages.FirstOrDefaultAsync(current => current.Id == messageId && current.ConversationId == conversationId && current.TenantId == tenantId && current.Direction == "Outbound", cancellationToken);
        var conversation = await db.Conversations.AsNoTracking().FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        var channel = conversation is null ? null : await db.WhatsAppChannelAccounts.AsNoTracking().FirstOrDefaultAsync(current => current.Id == conversation.ChannelAccountId && current.TenantId == tenantId, cancellationToken);
        if (message is null || conversation is null || channel?.Provider != "linked-device") return Results.NotFound();
        var sessionId = channel.ExternalAccountId["linked:".Length..];
        using var bridgeRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}/messages/{Uri.EscapeDataString(message.ExternalMessageId)}:delete") { Content = JsonContent.Create(new { to = conversation.CustomerExternalId, fromMe = true }) };
        bridgeRequest.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var bridgeResponse = await httpClientFactory.CreateClient().SendAsync(bridgeRequest, cancellationToken);
        if (!bridgeResponse.IsSuccessStatusCode) return Results.Problem("WhatsApp no pudo eliminar el mensaje.", statusCode: StatusCodes.Status502BadGateway);
        message.IsDeleted = true;
        message.Text = "";
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { deleted = true });
    }

    private sealed record BridgeMessageResponse(string MessageId);

    private static async Task<IResult> MessagesAsync(Guid conversationId, int? limit, string? before, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var ownsConversation = await db.Conversations.AnyAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (!ownsConversation) return Results.NotFound();
        var query = db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.TenantId == tenantId &&
                (!string.IsNullOrWhiteSpace(message.Text) || message.ImageData != null || message.MediaData != null || message.MediaUnavailable));
        if (limit is null && string.IsNullOrWhiteSpace(before))
        {
            var allMessages = await query.OrderBy(message => message.CreatedAt).ThenBy(message => message.Id)
                .Select(message => new { message.Id, message.Direction, message.Text, message.AudioTranscript, imageBase64 = message.ImageData != null ? Convert.ToBase64String(message.ImageData) : null, message.ImageMediaType, mediaBase64 = message.MediaData != null ? Convert.ToBase64String(message.MediaData) : null, message.MediaMimeType, message.MediaFileName, message.MediaKind, message.MediaUnavailable, message.QuotedExternalMessageId, message.QuotedText, message.IsDeleted, message.DeliveryStatus, message.DeliveryAttempts, message.DeliveryError, message.ReactionsJson, message.CreatedAt }).ToListAsync(cancellationToken);
            return Results.Ok(allMessages);
        }
        var pageSize = Math.Clamp(limit ?? 100, 20, 200);
        if (DateTimeOffset.TryParse(before, out var beforeDate)) query = query.Where(message => message.CreatedAt < beforeDate);
        var page = await query.OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.Id).Take(pageSize + 1)
            .Select(message => new { message.Id, message.Direction, message.Text, message.AudioTranscript, imageBase64 = message.ImageData != null ? Convert.ToBase64String(message.ImageData) : null, message.ImageMediaType, mediaBase64 = message.MediaData != null ? Convert.ToBase64String(message.MediaData) : null, message.MediaMimeType, message.MediaFileName, message.MediaKind, message.MediaUnavailable, message.QuotedExternalMessageId, message.QuotedText, message.IsDeleted, message.DeliveryStatus, message.DeliveryAttempts, message.DeliveryError, message.ReactionsJson, message.CreatedAt }).ToListAsync(cancellationToken);
        var hasMore = page.Count > pageSize;
        var messages = page.Take(pageSize).OrderBy(message => message.CreatedAt).ThenBy(message => message.Id).ToList();
        return Results.Ok(new { messages, hasMore });
    }

    private static async Task<IResult> SearchProductsAsync(string? query, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var normalizedQuery = (query ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedQuery.Length < 2) return Results.BadRequest(new { error = "La búsqueda requiere al menos 2 caracteres." });
        var products = await db.Products.AsNoTracking().Where(product => product.TenantId == tenantId && product.IsActive && (product.Name.ToLower().Contains(normalizedQuery) || product.Sku.ToLower().Contains(normalizedQuery))).OrderBy(product => product.Name).Take(10).Select(product => new { product.Id, product.Name, product.Sku, product.Price }).ToListAsync(cancellationToken);
        return Results.Ok(products);
    }

    private static async Task<IResult> ConnectMockAsync(ConnectMockChannelRequest request, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var exists = await db.WhatsAppChannelAccounts.AnyAsync(current => current.Provider == "mock" && current.ExternalAccountId == request.ExternalAccountId, cancellationToken);
        if (exists) return Results.Conflict(new { error = "La cuenta de canal ya está conectada." });
        var account = new WhatsAppChannelAccount { Id = Guid.NewGuid(), TenantId = tenantId, Provider = "mock", ExternalAccountId = request.ExternalAccountId.Trim(), DisplayName = request.DisplayName.Trim(), IsActive = true };
        db.WhatsAppChannelAccounts.Add(account); await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/channels/whatsapp/{account.Id}", new { account.Id, account.Provider, account.ExternalAccountId, account.DisplayName });
    }

    private static async Task<IResult> ConnectMetaAsync(ConnectMetaChannelRequest request, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        if (await HasActiveOtherWhatsAppChannelAsync(db, tenantId, excludingProvider: "meta", cancellationToken))
            return Results.Conflict(new { error = "Ya tenés un canal de WhatsApp activo (QR). Desconectalo antes de conectar WhatsApp Cloud API." });
        var phoneNumberId = request.PhoneNumberId.Trim();
        if (phoneNumberId.Length is < 5 or > 200) return Results.BadRequest(new { error = "El Phone Number ID de Meta no es válido." });
        var exists = await db.WhatsAppChannelAccounts.AnyAsync(current => current.Provider == "meta" && current.ExternalAccountId == phoneNumberId, cancellationToken);
        if (exists) return Results.Conflict(new { error = "La cuenta Meta ya está conectada." });
        var account = new WhatsAppChannelAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Provider = "meta",
            ExternalAccountId = phoneNumberId,
            DisplayName = request.DisplayName.Trim(),
            IsActive = true,
        };
        db.WhatsAppChannelAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/channels/whatsapp/{account.Id}", new { account.Id, account.Provider, account.ExternalAccountId, account.DisplayName });
    }

    private static async Task<IResult> DisconnectMetaAsync(Guid channelId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var account = await db.WhatsAppChannelAccounts.FirstOrDefaultAsync(current =>
            current.Id == channelId && current.TenantId == tenantId && current.Provider == "meta" && current.IsActive, cancellationToken);
        if (account is null) return Results.NotFound();
        account.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ConnectLinkedDeviceAsync(
        ConnectLinkedDeviceChannelRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 200)
            return Results.BadRequest(new { error = "El nombre del canal no es válido." });

        var hasLinkedDeviceChannel = await db.WhatsAppChannelAccounts.AnyAsync(current =>
            current.TenantId == tenantId && current.Provider == "linked-device" && current.IsActive,
            cancellationToken);
        if (hasLinkedDeviceChannel)
            return Results.Conflict(new { error = "Ya existe una sesión QR activa. Reconectala o desvinculala antes de conectar otro número." });
        if (await HasActiveOtherWhatsAppChannelAsync(db, tenantId, excludingProvider: "linked-device", cancellationToken))
            return Results.Conflict(new { error = "Ya tenés un canal de WhatsApp Cloud API activo. Desconectalo antes de vincular un número por QR." });

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions")
        {
            Content = JsonContent.Create(new { webhookUrl = options.Value.LinkedDeviceWebhookUrl, webhookSecret = options.Value.LinkedDeviceWebhookSecret }),
        };
        requestMessage.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Results.Problem("No se pudo iniciar el bridge de WhatsApp por QR.", statusCode: StatusCodes.Status502BadGateway);

        var session = await response.Content.ReadFromJsonAsync<LinkedDeviceSessionResponse>(cancellationToken: cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(session.SessionId))
            return Results.Problem("El bridge QR devolvió una sesión inválida.", statusCode: StatusCodes.Status502BadGateway);

        var externalAccountId = $"linked:{session.SessionId}";
        var exists = await db.WhatsAppChannelAccounts.AnyAsync(current => current.Provider == "linked-device" && current.ExternalAccountId == externalAccountId, cancellationToken);
        if (exists) return Results.Conflict(new { error = "La sesión QR ya está conectada." });
        var account = new WhatsAppChannelAccount { Id = Guid.NewGuid(), TenantId = tenantId, Provider = "linked-device", ExternalAccountId = externalAccountId, DisplayName = request.DisplayName.Trim(), IsActive = true };
        db.WhatsAppChannelAccounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/channels/whatsapp/{account.Id}", new { account.Id, account.Provider, account.ExternalAccountId, account.DisplayName, sessionId = session.SessionId, qr = session.Qr, status = session.Status });
    }

    private static async Task<IResult> ReconnectLinkedDeviceAsync(
        Guid channelId,
        ClaimsPrincipal user,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var account = await db.WhatsAppChannelAccounts.FirstOrDefaultAsync(current =>
            current.Id == channelId && current.TenantId == tenantId && current.Provider == "linked-device", cancellationToken);
        if (account is null) return Results.NotFound();

        if (account.ExternalAccountId.StartsWith("linked:", StringComparison.Ordinal))
        {
            var previousSessionId = account.ExternalAccountId["linked:".Length..];
            using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(previousSessionId)}");
            deleteRequest.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
            using var deleteResponse = await httpClientFactory.CreateClient().SendAsync(deleteRequest, cancellationToken);
            if (!deleteResponse.IsSuccessStatusCode && deleteResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
                return Results.Problem("No se pudo limpiar la sesión QR anterior.", statusCode: StatusCodes.Status502BadGateway);
        }

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions")
        {
            Content = JsonContent.Create(new { webhookUrl = options.Value.LinkedDeviceWebhookUrl, webhookSecret = options.Value.LinkedDeviceWebhookSecret }),
        };
        createRequest.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var createResponse = await httpClientFactory.CreateClient().SendAsync(createRequest, cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
            return Results.Problem("No se pudo iniciar la reconexión QR.", statusCode: StatusCodes.Status502BadGateway);
        var session = await createResponse.Content.ReadFromJsonAsync<LinkedDeviceSessionResponse>(cancellationToken: cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(session.SessionId))
            return Results.Problem("El bridge QR devolvió una sesión inválida.", statusCode: StatusCodes.Status502BadGateway);

        account.ExternalAccountId = $"linked:{session.SessionId}";
        account.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { account.Id, account.Provider, account.ExternalAccountId, account.DisplayName, sessionId = session.SessionId, qr = session.Qr, status = session.Status, isNewConnection = false });
    }

    private static async Task<IResult> LinkedDeviceStatusAsync(
        Guid channelId,
        ClaimsPrincipal user,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var account = await db.WhatsAppChannelAccounts.AsNoTracking().FirstOrDefaultAsync(current => current.Id == channelId && current.TenantId == tenantId && current.Provider == "linked-device" && current.IsActive, cancellationToken);
        if (account is null) return Results.NotFound();
        var sessionId = account.ExternalAccountId["linked:".Length..];
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}");
        requestMessage.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode) return Results.Problem("No se pudo consultar el estado del canal QR.", statusCode: StatusCodes.Status502BadGateway);
        var status = await response.Content.ReadFromJsonAsync<LinkedDeviceSessionResponse>(cancellationToken: cancellationToken);
        return status is null ? Results.Problem("El bridge QR devolvió un estado inválido.", statusCode: StatusCodes.Status502BadGateway) : Results.Ok(new { account.Id, account.Provider, account.DisplayName, sessionId, status.Status, status.Qr });
    }

    private static async Task<IResult> DisconnectLinkedDeviceAsync(
        Guid channelId,
        ClaimsPrincipal user,
        VantaDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppProviderOptions> options,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var account = await db.WhatsAppChannelAccounts.FirstOrDefaultAsync(current =>
            current.Id == channelId && current.TenantId == tenantId && current.Provider == "linked-device" && current.IsActive, cancellationToken);
        if (account is null) return Results.NotFound();

        var sessionId = account.ExternalAccountId["linked:".Length..];
        using var requestMessage = new HttpRequestMessage(HttpMethod.Delete, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}");
        requestMessage.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
        using var response = await httpClientFactory.CreateClient().SendAsync(requestMessage, cancellationToken);
        if (!response.IsSuccessStatusCode && (int)response.StatusCode != StatusCodes.Status404NotFound)
            return Results.Problem("No se pudo desvincular la sesión QR.", statusCode: StatusCodes.Status502BadGateway);

        account.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReceiveLinkedDeviceWebhookAsync(
        HttpRequest request,
        IEnumerable<IWhatsAppProvider> providers,
        WhatsAppWebhookProcessor processor,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var provider = providers.First(current => current.Name == "linked-device");
        var (signal, correlationId) = await ReadSignalAsync(request, cancellationToken);
        try
        {
            var result = await processor.ProcessAsync(provider, signal, correlationId, cancellationToken);
            if (!result.Validated) return WithCorrelationId(request, Results.Unauthorized(), correlationId);
            return WithCorrelationId(request, Results.Ok(new { received = true, processed = result.Events.Count > 0, correlationId }), correlationId);
        }
        catch (WhatsAppMalformedPayloadException exception)
        {
            logger.LogWarning(exception, "Malformed linked-device webhook payload. CorrelationId={CorrelationId}", correlationId);
            return WithCorrelationId(request, Results.BadRequest(new { error = "El payload del bridge QR no es válido.", correlationId }), correlationId);
        }
    }

    private sealed record LinkedDeviceSessionResponse(string SessionId, string Status, string? Qr);

    // Only one real WhatsApp connection (Meta Cloud API or QR-linked device) may be active per
    // tenant at a time, to avoid splitting a customer's conversation history across two channels.
    private static Task<bool> HasActiveOtherWhatsAppChannelAsync(VantaDbContext db, Guid tenantId, string excludingProvider, CancellationToken cancellationToken) =>
        db.WhatsAppChannelAccounts.AnyAsync(current =>
            current.TenantId == tenantId && current.IsActive && current.Provider != excludingProvider &&
            (current.Provider == "meta" || current.Provider == "linked-device"), cancellationToken);

    private static async Task<IResult> ReceiveMockAsync(
        HttpRequest httpRequest,
        IEnumerable<IWhatsAppProvider> providers,
        WhatsAppWebhookProcessor processor,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var provider = providers.First(current => current.Name == "mock");
        var (signal, correlationId) = await ReadSignalAsync(httpRequest, cancellationToken);

        WhatsAppWebhookProcessResult result;
        try
        {
            result = await processor.ProcessAsync(provider, signal, correlationId, cancellationToken);
        }
        catch (WhatsAppMalformedPayloadException exception)
        {
            logger.LogWarning(exception, "Malformed mock webhook payload. CorrelationId={CorrelationId}", correlationId);
            return WithCorrelationId(httpRequest, Results.BadRequest(new { error = "El payload del webhook no es válido.", correlationId }), correlationId);
        }

        if (!result.Validated) return WithCorrelationId(httpRequest, Results.Unauthorized(), correlationId);
        if (result.Events.Count == 0) return WithCorrelationId(httpRequest, Results.Ok(new { processed = false, reason = "unrecognized_event", correlationId }), correlationId);

        var messageResult = result.Events[0];
        return WithCorrelationId(httpRequest, messageResult.Outcome switch
        {
            WhatsAppEventOutcome.Duplicate => Results.Ok(new { processed = true, duplicate = true, correlationId }),
            WhatsAppEventOutcome.ChannelNotFound => Results.NotFound(),
            WhatsAppEventOutcome.MediaRejected => Results.BadRequest(new { error = "La imagen no cumple el tipo o tamaño permitido.", correlationId }),
            _ => Results.Ok(new { processed = true, duplicate = false, conversationId = messageResult.ConversationId, correlationId }),
        }, correlationId);
    }

    private static async Task<IResult> ListAsync(int? limit, string? before, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        var tenantClaim = user.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId)) return Results.Unauthorized();
        var query = db.Conversations.AsNoTracking().Where(c => c.TenantId == tenantId);
        if (DateTimeOffset.TryParse(before, out var beforeDate)) query = query.Where(c => c.LastMessageAt < beforeDate);
        var projected = query.OrderByDescending(c => c.LastMessageAt).Select(c => new ConversationResponse(c.Id, c.CustomerExternalId, db.Customers.Where(customer => customer.Id == c.CustomerId && customer.TenantId == tenantId).Select(customer => customer.Name).FirstOrDefault() ?? c.DisplayName ?? c.CustomerExternalId, db.Customers.Where(customer => customer.Id == c.CustomerId && customer.TenantId == tenantId).Select(customer => customer.ProfileImageUrl).FirstOrDefault(), c.IsGroup, c.IsChannel, c.Status, c.CurrentIntent, c.AssignedUserId, c.LastMessageAt, c.LastActivityAt));
        if (limit is null && string.IsNullOrWhiteSpace(before)) return Results.Ok(await projected.ToListAsync(cancellationToken));
        var pageSize = Math.Clamp(limit ?? 50, 20, 100);
        var page = await projected.Take(pageSize + 1).ToListAsync(cancellationToken);
        return Results.Ok(new { conversations = page.Take(pageSize), hasMore = page.Count > pageSize });
    }

    private static async Task<IResult> SendImageAsync(Guid conversationId, HttpRequest httpRequest, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return Results.NotFound();
        var form = await httpRequest.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("image");
        if (file is null or { Length: 0 }) return Results.BadRequest(new { error = "El archivo de imagen es requerido." });
        if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { error = "La imagen no puede exceder 5MB." });
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType)) return Results.BadRequest(new { error = "Tipo de archivo no permitido. Use JPEG, PNG o WebP." });
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, cancellationToken);
        var imageData = stream.ToArray();
        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageAt = now;
        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversation.Id,
            ExternalMessageId = $"img-out-{Guid.NewGuid():N}",
            Direction = "Outbound",
            Text = form.ContainsKey("text") ? form["text"].ToString().Trim() : string.Empty,
            ImageData = imageData,
            ImageMediaType = file.ContentType,
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { processed = true });
    }

    private static async Task<IResult> ListChannelsAsync(ClaimsPrincipal user, VantaDbContext db, IHttpClientFactory httpClientFactory, IOptions<WhatsAppProviderOptions> options, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tenantId)) return Results.Unauthorized();
        var channels = await db.WhatsAppChannelAccounts.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.IsActive)
            .Select(current => new { current.Id, current.Provider, current.ExternalAccountId, current.DisplayName })
            .ToListAsync(cancellationToken);
        var result = new List<object>(channels.Count);
        foreach (var channel in channels)
        {
            var status = channel.Provider == "meta" ? "connected" : "disconnected";
            if (channel.Provider == "linked-device" && channel.ExternalAccountId.StartsWith("linked:", StringComparison.Ordinal))
            {
                try
                {
                    var sessionId = channel.ExternalAccountId["linked:".Length..];
                    using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.Value.LinkedDeviceBridgeUrl.TrimEnd('/')}/sessions/{Uri.EscapeDataString(sessionId)}");
                    request.Headers.Add("X-Bridge-Key", options.Value.LinkedDeviceBridgeKey);
                    using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var bridgeStatus = await response.Content.ReadFromJsonAsync<LinkedDeviceSessionResponse>(cancellationToken: cancellationToken);
                        status = bridgeStatus?.Status ?? status;
                    }
                }
                catch (HttpRequestException) { }
            }
            result.Add(new { channel.Id, channel.Provider, channel.ExternalAccountId, channel.DisplayName, status, isOnline = status == "connected" });
        }
        return Results.Ok(result);
    }
}
