using System.Net.Http.Json;
using System.Net.Http.Headers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Identity;
using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public class WhatsAppTests(VantaWebApplicationFactory factory) : IClassFixture<VantaWebApplicationFactory>
{
    [Fact]
    public async Task Signed_mock_message_creates_one_idempotent_conversation()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "WhatsApp Demo", "whatsapp-demo@vanta.local");
        var accountExternalId = await ConnectAsync(client, auth.AccessToken, "mock-account-001");

        var first = await SendMessageAsync(client, auth.TenantId, accountExternalId, "message-001");
        var duplicate = await SendMessageAsync(client, auth.TenantId, accountExternalId, "message-001");
        var conversations = await ListConversationsAsync(client, auth.AccessToken);

        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("duplicate", await duplicate.Content.ReadAsStringAsync());
        Assert.Single(conversations);
    }

    [Fact]
    public async Task Agent_purchase_message_creates_conversation_linked_order()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "WhatsApp Orders", "whatsapp-orders@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var accountExternalId = await ConnectAsync(client, auth.AccessToken, "mock-order-account-001");
        var messageId = $"purchase-{Guid.NewGuid():N}";

        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        webhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        webhook.Content = JsonContent.Create(new { eventId = $"event-{messageId}", accountExternalId, customerExternalId = "customer-order-001", messageId, text = "Quiero comprar 2 unidades" + $" de {product.Name}" });
        var webhookResponse = await client.SendAsync(webhook);
        webhookResponse.EnsureSuccessStatusCode();
        var webhookBody = await webhookResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        var confirmationMessageId = $"confirmation-{Guid.NewGuid():N}";
        using var confirmationWebhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        confirmationWebhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        confirmationWebhook.Content = JsonContent.Create(new { eventId = $"event-{confirmationMessageId}", accountExternalId, customerExternalId = "customer-order-001", messageId = confirmationMessageId, text = "Sí, confirmo" });
        var confirmationResponse = await client.SendAsync(confirmationWebhook);
        confirmationResponse.EnsureSuccessStatusCode();
        var confirmationBody = await confirmationResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        var detailsMessageId = $"details-{Guid.NewGuid():N}";
        using var detailsWebhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        detailsWebhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        detailsWebhook.Content = JsonContent.Create(new { eventId = $"event-{detailsMessageId}", accountExternalId, customerExternalId = "customer-order-001", messageId = detailsMessageId, text = "Nombre: Ana, Dirección: Av. España 123, Asunción" });
        var detailsResponse = await client.SendAsync(detailsWebhook);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsBody = await detailsResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        using var ordersRequest = new HttpRequestMessage(HttpMethod.Get, "/api/orders");
        ordersRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var ordersResponse = await client.SendAsync(ordersRequest);
        ordersResponse.EnsureSuccessStatusCode();
        var orders = await ordersResponse.Content.ReadFromJsonAsync<List<OrderResponse>>();
        var order = Assert.Single(orders!);

        Assert.Equal(webhookBody!.ConversationId, order.ConversationId);
        Assert.NotNull(order.CustomerId);
        Assert.Equal(2, order.Items[0].Quantity);
        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public async Task Agent_identifies_product_from_matching_inbound_image()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "WhatsApp Imagen", $"whatsapp-image-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var imageBytes = new byte[] { 1, 2, 3, 4, 5 };
        await UploadProductImageAsync(client, auth.AccessToken, product.Id, imageBytes);
        var accountExternalId = await ConnectAsync(client, auth.AccessToken, "mock-image-account-" + Guid.NewGuid().ToString("N"));
        var messageId = $"image-{Guid.NewGuid():N}";

        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        webhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        webhook.Content = JsonContent.Create(new { eventId = $"event-{messageId}", accountExternalId, customerExternalId = "customer-image-001", messageId, text = "Tienen este producto?", imageBase64 = Convert.ToBase64String(imageBytes), imageMediaType = "image/jpeg" });
        var webhookResponse = await client.SendAsync(webhook);
        webhookResponse.EnsureSuccessStatusCode();
        var webhookBody = await webhookResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        using var messagesRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{webhookBody!.ConversationId}/messages");
        messagesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var messagesResponse = await client.SendAsync(messagesRequest);
        messagesResponse.EnsureSuccessStatusCode();
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<ConversationMessageResponse>>();
        var outbound = Assert.Single(messages!, message => message.Direction == "Outbound");

        Assert.Contains(product.Name, outbound.Text);
        Assert.Contains("10.000 Gs.", outbound.Text);
    }

    [Fact]
    public async Task Agent_identifies_product_from_visually_similar_inbound_image()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "WhatsApp Vision", $"whatsapp-vision-{Guid.NewGuid():N}@vanta.local");
        var product = await CreateProductAsync(client, auth.AccessToken);
        var catalogImage = CreateSolidJpeg(48, 48, new Rgba32(40, 180, 90));
        var customerImage = CreateSolidJpeg(48, 48, new Rgba32(44, 176, 94));
        await UploadProductImageAsync(client, auth.AccessToken, product.Id, catalogImage);
        var accountExternalId = await ConnectAsync(client, auth.AccessToken, "mock-vision-account-" + Guid.NewGuid().ToString("N"));
        var messageId = $"vision-{Guid.NewGuid():N}";

        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        webhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        webhook.Content = JsonContent.Create(new { eventId = $"event-{messageId}", accountExternalId, customerExternalId = "customer-vision-001", messageId, text = "Tenés algo parecido a esto?", imageBase64 = Convert.ToBase64String(customerImage), imageMediaType = "image/jpeg" });
        var webhookResponse = await client.SendAsync(webhook);
        webhookResponse.EnsureSuccessStatusCode();
        var webhookBody = await webhookResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        using var messagesRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{webhookBody!.ConversationId}/messages");
        messagesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var messagesResponse = await client.SendAsync(messagesRequest);
        messagesResponse.EnsureSuccessStatusCode();
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<ConversationMessageResponse>>();
        var outbound = Assert.Single(messages!, message => message.Direction == "Outbound");

        Assert.Contains(product.Name, outbound.Text);
    }

    [Fact]
    public async Task Agent_asks_customer_for_text_when_image_ai_is_unavailable_and_no_match_exists()
    {
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "WhatsApp Fallback", $"whatsapp-fallback-{Guid.NewGuid():N}@vanta.local");
        var accountExternalId = await ConnectAsync(client, auth.AccessToken, "mock-fallback-account-" + Guid.NewGuid().ToString("N"));
        var messageId = $"fallback-{Guid.NewGuid():N}";
        var unknownImage = CreateSolidJpeg(48, 48, new Rgba32(240, 20, 20));

        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        webhook.Headers.Add("X-Webhook-Signature", "mock-dev");
        webhook.Content = JsonContent.Create(new { eventId = $"event-{messageId}", accountExternalId, customerExternalId = "customer-fallback-001", messageId, text = "", imageBase64 = Convert.ToBase64String(unknownImage), imageMediaType = "image/jpeg" });
        var webhookResponse = await client.SendAsync(webhook);
        webhookResponse.EnsureSuccessStatusCode();
        var webhookBody = await webhookResponse.Content.ReadFromJsonAsync<MockWebhookResponse>();

        using var messagesRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/conversations/{webhookBody!.ConversationId}/messages");
        messagesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var messagesResponse = await client.SendAsync(messagesRequest);
        messagesResponse.EnsureSuccessStatusCode();
        var messages = await messagesResponse.Content.ReadFromJsonAsync<List<ConversationMessageResponse>>();
        var outbound = Assert.Single(messages!, message => message.Direction == "Outbound");

        Assert.Contains("decirme el nombre", outbound.Text);
        Assert.Contains("buscarlo en el catálogo", outbound.Text);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, "Passw0rd!123"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task<string> ConnectAsync(HttpClient client, string token, string externalAccountId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/whatsapp/mock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { externalAccountId, displayName = "WhatsApp Demo" });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return externalAccountId;
    }

    private static async Task<ProductResponse> CreateProductAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { name = "Producto WhatsApp", sku = $"WHA-{Guid.NewGuid():N}", price = 10000 });
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private static async Task UploadProductImageAsync(HttpClient client, string token, Guid productId, byte[] imageBytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(imageBytes) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "product.jpg");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/products/{productId}/image");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static byte[] CreateSolidJpeg(int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    private static async Task<HttpResponseMessage> SendMessageAsync(HttpClient client, Guid tenantId, string accountExternalId, string messageId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/whatsapp/mock");
        request.Headers.Add("X-Webhook-Signature", "mock-dev");
        request.Content = JsonContent.Create(new { eventId = $"event-{messageId}", accountExternalId, customerExternalId = "customer-001", messageId, text = "Quiero ver productos" });
        return await client.SendAsync(request);
    }

    private static async Task<List<object>> ListConversationsAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<object>>())!;
    }

    private sealed record MockWebhookResponse(bool Processed, bool Duplicate, Guid ConversationId);
    private sealed record ConversationMessageResponse(Guid Id, string Direction, string Text, string? ImageBase64, string? ImageMediaType, string? DeliveryStatus, int DeliveryAttempts, string? DeliveryError, DateTimeOffset CreatedAt);
}
