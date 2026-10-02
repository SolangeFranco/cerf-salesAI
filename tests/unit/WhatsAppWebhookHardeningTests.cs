using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Vanta.Api.Modules.Identity;
using Xunit;

namespace Vanta.Api.UnitTests;

// Covers the hardened WhatsApp ingestion pipeline: validate -> identify provider -> normalize
// event -> idempotency check -> persist -> process. Complements WhatsAppTests.cs (conversation/
// agent behavior) and WhatsAppDeliveryTests.cs (outbound retries).
public class WhatsAppWebhookHardeningTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Mock_webhook_with_wrong_signature_is_rejected()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync("whatsapp-invalid-sig");
        var accountExternalId = await ConnectMockAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "not-the-real-secret");
        request.Content = JsonContent.Create(new
        {
            eventId = $"event-{Guid.NewGuid():N}",
            accountExternalId,
            customerExternalId = "customer-invalid-sig",
            messageId = $"msg-{Guid.NewGuid():N}",
            text = "Hola",
        });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_mock_webhook_payload_is_rejected_as_bad_request()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = new StringContent("{ this is not valid json", System.Text.Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Mock_webhook_missing_required_fields_is_rejected_as_bad_request()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { text = "Hola sin los campos requeridos" });

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_webhook_delivery_with_different_event_id_does_not_create_a_second_message()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync("whatsapp-dup-eventid");
        var accountExternalId = await ConnectMockAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var messageId = $"msg-{Guid.NewGuid():N}";

        var first = await SendMockWebhookAsync(accountExternalId, "customer-dup-001", messageId, $"event-a-{Guid.NewGuid():N}", "Hola");
        var firstBody = await first.Content.ReadFromJsonAsync<MockWebhookResponse>();

        // Same underlying WhatsApp message id, but a different transport-level event id: this
        // simulates a provider redelivering the business event under a new delivery envelope.
        var second = await SendMockWebhookAsync(accountExternalId, "customer-dup-001", messageId, $"event-b-{Guid.NewGuid():N}", "Hola");
        var secondBody = await second.Content.ReadFromJsonAsync<MockWebhookResponse>();

        var messages = await GetMessagesAsync(auth.AccessToken, firstBody!.ConversationId);

        Assert.False(firstBody.Duplicate);
        Assert.True(secondBody!.Duplicate);
        Assert.Single(messages, message => message.Direction == "Inbound");
    }

    [Fact]
    public async Task Duplicate_webhook_delivery_with_same_event_id_is_short_circuited()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync("whatsapp-dup-transport");
        var accountExternalId = await ConnectMockAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");
        var messageId = $"msg-{Guid.NewGuid():N}";
        var eventId = $"event-{Guid.NewGuid():N}";

        var first = await SendMockWebhookAsync(accountExternalId, "customer-dup-transport-001", messageId, eventId, "Hola");
        var second = await SendMockWebhookAsync(accountExternalId, "customer-dup-transport-001", messageId, eventId, "Hola");

        var firstBody = await first.Content.ReadFromJsonAsync<MockWebhookResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<MockWebhookResponse>();

        Assert.False(firstBody!.Duplicate);
        Assert.True(secondBody!.Duplicate);
    }

      [Fact]
      public async Task Empty_message_without_supported_media_does_not_create_a_conversation()
      {
        var client = factory.CreateClient();
        var auth = await RegisterAsync("whatsapp-empty-message");
        var accountExternalId = await ConnectMockAsync(auth.AccessToken, $"acc-{Guid.NewGuid():N}");

        var response = await SendMockWebhookAsync(accountExternalId, "customer-empty-001", $"msg-{Guid.NewGuid():N}", $"event-{Guid.NewGuid():N}", "");

        response.EnsureSuccessStatusCode();
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var body = await listResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("customer-empty-001", body);
      }

    [Fact]
    public async Task Unsupported_meta_event_type_is_acknowledged_without_processing()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync("whatsapp-unknown-event");
        var phoneNumberId = $"phone-{Guid.NewGuid():N}";
        await ConnectMetaAsync(auth.AccessToken, phoneNumberId);

        var payload = $$"""
        {
          "entry": [{
            "changes": [{
              "value": {
                "metadata": { "phone_number_id": "{{phoneNumberId}}" },
                "messages": [{ "from": "595981000001", "id": "wamid.STICKER-1", "type": "sticker" }]
              }
            }]
          }]
        }
        """;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/meta");
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Contains("unrecognized_event", body);
    }

    [Fact]
    public async Task Meta_delivery_status_update_is_isolated_per_tenant()
    {
        var client = factory.CreateClient();
        var tenantA = await RegisterAsync("whatsapp-isolation-a");
        var tenantB = await RegisterAsync("whatsapp-isolation-b");
        var phoneA = $"phone-a-{Guid.NewGuid():N}";
        var phoneB = $"phone-b-{Guid.NewGuid():N}";
        await ConnectMetaAsync(tenantA.AccessToken, phoneA);
        await ConnectMetaAsync(tenantB.AccessToken, phoneB);

        var conversationA = await CreateMetaConversationAsync(phoneA, "595981111001");
        var conversationB = await CreateMetaConversationAsync(phoneB, "595981111002");

        const string sharedIdempotencyKey = "collide-1";
        await SendOutboundAsync(tenantA.AccessToken, conversationA, sharedIdempotencyKey);
        await SendOutboundAsync(tenantB.AccessToken, conversationB, sharedIdempotencyKey);

        var statusPayload = $$"""
        {
          "entry": [{
            "changes": [{
              "value": {
                "metadata": { "phone_number_id": "{{phoneA}}" },
                "statuses": [{ "id": "mock-out-{{sharedIdempotencyKey}}", "status": "delivered" }]
              }
            }]
          }]
        }
        """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/meta");
        request.Content = new StringContent(statusPayload, System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var messagesA = await GetMessagesAsync(tenantA.AccessToken, conversationA);
        var messagesB = await GetMessagesAsync(tenantB.AccessToken, conversationB);
        var outboundA = Assert.Single(messagesA, message => message.Direction == "Outbound" && message.Text == "Actualización de pedido");
        var outboundB = Assert.Single(messagesB, message => message.Direction == "Outbound" && message.Text == "Actualización de pedido");

        Assert.Equal("Delivered", outboundA.DeliveryStatus);
        Assert.Equal("Pending", outboundB.DeliveryStatus);
    }

    private async Task<AuthResponse> RegisterAsync(string slug)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"Vanta {slug}", $"{slug}-{Guid.NewGuid():N}@vanta.local", "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> ConnectMockAsync(string token, string externalAccountId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId, displayName = "Mock Channel" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return externalAccountId;
    }

    private async Task ConnectMetaAsync(string token, string phoneNumberId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/meta");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { phoneNumberId, displayName = "Meta Channel" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> SendMockWebhookAsync(string accountExternalId, string customerExternalId, string messageId, string eventId, string text)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { eventId, accountExternalId, customerExternalId, messageId, text });
        return await client.SendAsync(request);
    }

    private async Task<Guid> CreateMetaConversationAsync(string phoneNumberId, string customerWaId)
    {
        var client = factory.CreateClient();
        var payload = $$"""
        {
          "entry": [{
            "changes": [{
              "value": {
                "metadata": { "phone_number_id": "{{phoneNumberId}}" },
                "messages": [{ "from": "{{customerWaId}}", "id": "wamid.{{Guid.NewGuid():N}}", "type": "text", "text": { "body": "Hola" } }]
              }
            }]
          }]
        }
        """;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/meta");
        request.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MetaWebhookResponse>();
        return body!.ConversationId!.Value;
    }

    private async Task SendOutboundAsync(string token, Guid conversationId, string idempotencyKey)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/conversations/{conversationId}/messages");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { text = "Actualización de pedido", idempotencyKey });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
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
    private sealed record MetaWebhookResponse(bool Received, bool Processed, bool? Duplicate, Guid? ConversationId);
    private sealed record ConversationMessageResponse(Guid Id, string Direction, string Text, string? ImageBase64, string? ImageMediaType, string? DeliveryStatus, int DeliveryAttempts, string? DeliveryError, DateTimeOffset CreatedAt);
}
