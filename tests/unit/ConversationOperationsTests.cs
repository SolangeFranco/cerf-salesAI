using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Identity;
using Xunit;

namespace Vanta.Api.UnitTests;

// Covers the Conversation operational core: AI_ACTIVE/HUMAN_ACTIVE/PAUSED/CLOSED state machine,
// assignment, AI pause/resume, human handoff, reopening, the AI double-response guard, and
// tenant isolation of every new conversation-management endpoint.
public class ConversationOperationsTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task New_conversation_defaults_to_ai_active_and_responds_automatically()
    {
        var auth = await RegisterAsync("conv-default");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-default-001", "Quiero ver productos");

        var context = await GetContextAsync(auth.AccessToken, conversationId);

        Assert.Equal("AI_ACTIVE", context!.Status);
        var messages = await GetMessagesAsync(auth.AccessToken, conversationId);
        Assert.Contains(messages, message => message.Direction == "Outbound");
    }

    [Fact]
    public async Task Take_control_stops_automatic_ai_replies_and_assigns_the_actor()
    {
        var auth = await RegisterAsync("conv-take-control");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-tc-001", "Hola");

        var takeControl = await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/take-control");
        Assert.Equal(HttpStatusCode.OK, takeControl.StatusCode);

        var beforeCount = (await GetMessagesAsync(auth.AccessToken, conversationId)).Count;

        // A new inbound message must NOT get an automatic AI reply while a human is in control.
        await SendInboundAsync(accountExternalId, "customer-tc-001", "Sigo esperando", conversationId);
        var afterMessages = await GetMessagesAsync(auth.AccessToken, conversationId);

        var context = await GetContextAsync(auth.AccessToken, conversationId);
        Assert.Equal("HUMAN_ACTIVE", context!.Status);
        Assert.NotNull(context.AssignedUserId);
        // Only the new inbound message was added; no new outbound AI reply.
        Assert.Equal(beforeCount + 1, afterMessages.Count);
    }

    [Fact]
    public async Task Pause_then_resume_restores_automatic_ai_replies_and_clears_assignment()
    {
        var auth = await RegisterAsync("conv-pause-resume");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-pr-001", "Hola");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/pause")).StatusCode);
        var pausedContext = await GetContextAsync(auth.AccessToken, conversationId);
        Assert.Equal("PAUSED", pausedContext!.Status);

        // Sent while paused: the automatic reply must not fire, leaving this message unanswered.
        await SendInboundAsync(accountExternalId, "customer-pr-001", "Sigo esperando", conversationId);
        var countWhilePaused = (await GetMessagesAsync(auth.AccessToken, conversationId)).Count;
        var agentWhilePaused = await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/agent/mock-respond");
        agentWhilePaused.EnsureSuccessStatusCode();
        var resultWhilePaused = await agentWhilePaused.Content.ReadFromJsonAsync<AgentResponseBody>();
        Assert.False(resultWhilePaused!.Responded);
        Assert.Equal(countWhilePaused, (await GetMessagesAsync(auth.AccessToken, conversationId)).Count);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/resume")).StatusCode);
        var resumedContext = await GetContextAsync(auth.AccessToken, conversationId);
        Assert.Equal("AI_ACTIVE", resumedContext!.Status);
        Assert.Null(resumedContext.AssignedUserId);

        var agentAfterResume = await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/agent/mock-respond");
        var resultAfterResume = await agentAfterResume.Content.ReadFromJsonAsync<AgentResponseBody>();
        Assert.True(resultAfterResume!.Responded);
    }

    [Fact]
    public async Task Global_pause_pauses_all_chats_but_allows_individual_activation()
    {
        var auth = await RegisterAsync("conv-global-pause");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var globallyPausedId = await SendInboundAsync(accountExternalId, "customer-global-001", "Hola");
        var manuallyPausedId = await SendInboundAsync(accountExternalId, "customer-global-002", "Hola");
        var humanActiveId = await SendInboundAsync(accountExternalId, "customer-global-004", "Necesito una persona");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{manuallyPausedId}/pause")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{humanActiveId}/take-control")).StatusCode);

        var otherChatCount = (await GetMessagesAsync(auth.AccessToken, globallyPausedId)).Count;
        await SendInboundAsync(accountExternalId, "customer-global-001", "¿Qué productos tenés?", globallyPausedId);
        Assert.Equal(otherChatCount + 2, (await GetMessagesAsync(auth.AccessToken, globallyPausedId)).Count);
        Assert.Equal("AI_ACTIVE", (await GetContextAsync(auth.AccessToken, globallyPausedId))!.Status);

        Assert.Equal(HttpStatusCode.OK, (await UpdateAiResponsesEnabledAsync(auth.AccessToken, false)).StatusCode);
        Assert.Equal("PAUSED", (await GetContextAsync(auth.AccessToken, globallyPausedId))!.Status);
        Assert.Equal("PAUSED", (await GetContextAsync(auth.AccessToken, manuallyPausedId))!.Status);
        Assert.Equal("HUMAN_ACTIVE", (await GetContextAsync(auth.AccessToken, humanActiveId))!.Status);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{globallyPausedId}/resume")).StatusCode);
        var firstChatCount = (await GetMessagesAsync(auth.AccessToken, globallyPausedId)).Count;
        await SendInboundAsync(accountExternalId, "customer-global-001", "Quiero ver productos", globallyPausedId);
        Assert.Equal(firstChatCount + 2, (await GetMessagesAsync(auth.AccessToken, globallyPausedId)).Count);

        var secondChatCount = (await GetMessagesAsync(auth.AccessToken, manuallyPausedId)).Count;
        await SendInboundAsync(accountExternalId, "customer-global-002", "Sigo esperando", manuallyPausedId);
        Assert.Equal(secondChatCount + 1, (await GetMessagesAsync(auth.AccessToken, manuallyPausedId)).Count);

        var newChatId = await SendInboundAsync(accountExternalId, "customer-global-003", "Hola");
        Assert.Equal("PAUSED", (await GetContextAsync(auth.AccessToken, newChatId))!.Status);
        Assert.DoesNotContain(await GetMessagesAsync(auth.AccessToken, newChatId), message => message.Direction == "Outbound");

        Assert.Equal(HttpStatusCode.OK, (await UpdateAiResponsesEnabledAsync(auth.AccessToken, true)).StatusCode);
        Assert.Equal("AI_ACTIVE", (await GetContextAsync(auth.AccessToken, globallyPausedId))!.Status);
        Assert.Equal("PAUSED", (await GetContextAsync(auth.AccessToken, manuallyPausedId))!.Status);
        Assert.Equal("HUMAN_ACTIVE", (await GetContextAsync(auth.AccessToken, humanActiveId))!.Status);
        Assert.Equal("AI_ACTIVE", (await GetContextAsync(auth.AccessToken, newChatId))!.Status);
    }

    [Fact]
    public async Task Assign_to_a_user_outside_the_tenant_is_rejected()
    {
        var auth = await RegisterAsync("conv-assign-invalid");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-assign-001", "Hola");

        var response = await PostJsonAsync(auth.AccessToken, $"/api/conversations/{conversationId}/assign", new { userId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Closing_then_a_new_message_reopens_the_conversation_to_ai()
    {
        var auth = await RegisterAsync("conv-reopen");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-reopen-001", "Hola");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/close")).StatusCode);
        Assert.Equal("CLOSED", (await GetContextAsync(auth.AccessToken, conversationId))!.Status);

        await SendInboundAsync(accountExternalId, "customer-reopen-001", "Sigo interesado", conversationId);

        var context = await GetContextAsync(auth.AccessToken, conversationId);
        Assert.Equal("AI_ACTIVE", context!.Status);
        Assert.Contains(context.RecentActivity, activity => activity.Type == "Reopened");
    }

    [Fact]
    public async Task Sequential_agent_respond_calls_only_produce_one_reply_per_message()
    {
        var auth = await RegisterAsync("conv-double-response");
        var accountExternalId = await ConnectAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-double-001", "Hola");

        // Pause so this second message is NOT auto-answered, leaving one message both callers
        // will try to claim once the AI is resumed (true concurrent-claim safety is covered
        // against a real Postgres instance in ConversationConcurrencyTests).
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/pause")).StatusCode);
        await SendInboundAsync(accountExternalId, "customer-double-001", "Sigo esperando", conversationId);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/resume")).StatusCode);

        var beforeCount = (await GetMessagesAsync(auth.AccessToken, conversationId)).Count;

        var first = await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/agent/mock-respond");
        var second = await PostAsync(auth.AccessToken, $"/api/conversations/{conversationId}/agent/mock-respond");
        var firstBody = await first.Content.ReadFromJsonAsync<AgentResponseBody>();
        var secondBody = await second.Content.ReadFromJsonAsync<AgentResponseBody>();

        Assert.True(firstBody!.Responded);
        Assert.False(secondBody!.Responded);
        var afterCount = (await GetMessagesAsync(auth.AccessToken, conversationId)).Count;
        Assert.Equal(beforeCount + 1, afterCount);
    }

    [Fact]
    public async Task Tenant_cannot_operate_on_another_tenants_conversation()
    {
        var tenantA = await RegisterAsync("conv-isolation-a");
        var tenantB = await RegisterAsync("conv-isolation-b");
        var accountExternalId = await ConnectAsync(tenantA.AccessToken, $"acc-{Guid.NewGuid():N}");
        var conversationId = await SendInboundAsync(accountExternalId, "customer-isolation-001", "Hola");

        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(tenantB.AccessToken, $"/api/conversations/{conversationId}/take-control")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(tenantB.AccessToken, $"/api/conversations/{conversationId}/pause")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(tenantB.AccessToken, $"/api/conversations/{conversationId}/close")).StatusCode);
        using var contextRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/context");
        contextRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tenantB.AccessToken);
        var contextResponse = await factory.CreateClient().SendAsync(contextRequest);
        Assert.Equal(HttpStatusCode.NotFound, contextResponse.StatusCode);
    }

    private async Task<AuthResponse> RegisterAsync(string slug)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"Vanta {slug}", $"{slug}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> ConnectAsync(string token, string externalAccountId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId, displayName = "Mock Channel" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return externalAccountId;
    }

    private async Task<Guid> SendInboundAsync(string accountExternalId, string customerExternalId, string text, Guid? expectedConversationId = null)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new
        {
            eventId = $"event-{Guid.NewGuid():N}",
            accountExternalId,
            customerExternalId,
            messageId = $"msg-{Guid.NewGuid():N}",
            text,
        });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MockWebhookResponse>();
        if (expectedConversationId is not null) Assert.Equal(expectedConversationId, body!.ConversationId);
        return body!.ConversationId;
    }

    private async Task<HttpResponseMessage> PostAsync(string token, string path)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string token, string path, object body)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> UpdateAiResponsesEnabledAsync(string token, bool enabled)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/tenants/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "Tienda de prueba",
            deliveryBaseCostGs = 10000,
            deliveryIncludedDistanceKm = 10,
            deliveryExtraCostPerKmGs = 0,
            metropolitanDeliveryCostGs = 10000,
            interiorDeliveryCostGs = 10000,
            aiResponsesEnabled = enabled,
        });
        return await client.SendAsync(request);
    }

    private async Task<ConversationContextBody?> GetContextAsync(string token, Guid conversationId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConversationContextBody>();
    }

    private async Task<List<ConversationMessageResponse>> GetMessagesAsync(string token, Guid conversationId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<ConversationMessageResponse>>())!;
    }

    private sealed record MockWebhookResponse(bool Processed, bool Duplicate, Guid ConversationId);
    private sealed record AgentResponseBody(string Text, bool Responded);
    private sealed record ConversationActivityBody(string Type, string? FromStatus, string? ToStatus, Guid? ActorUserId, Guid? AssignedUserId, DateTimeOffset CreatedAt);
    private sealed record ConversationContextBody(
        Guid Id, string CustomerExternalId, string Status, string? CurrentIntent, Guid? AssignedUserId, DateTimeOffset? AssignedAt,
        DateTimeOffset LastMessageAt, DateTimeOffset LastActivityAt, object? Cart, object? LatestOrder, List<ConversationActivityBody> RecentActivity);
    private sealed record ConversationMessageResponse(Guid Id, string Direction, string Text, string? ImageBase64, string? ImageMediaType, string? DeliveryStatus, int DeliveryAttempts, string? DeliveryError, DateTimeOffset CreatedAt);
}
