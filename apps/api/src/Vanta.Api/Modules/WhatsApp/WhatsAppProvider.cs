using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record WhatsAppOutboundMessage(string RecipientExternalId, string Text, byte[]? ImageData = null, string? ImageMediaType = null, string? ChannelExternalId = null);
public sealed record WhatsAppSendResult(string ProviderMessageId);

public interface IWhatsAppMessageSender
{
    Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken);
}

public sealed class WhatsAppProviderOptions
{
    public string Provider { get; set; } = "mock";
    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com/v22.0";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string MockWebhookSecret { get; set; } = string.Empty;
    public string LinkedDeviceBridgeUrl { get; set; } = "http://localhost:5080";
    public string LinkedDeviceBridgeKey { get; set; } = string.Empty;
    public string LinkedDeviceWebhookSecret { get; set; } = string.Empty;
    public string LinkedDeviceWebhookUrl { get; set; } = "http://localhost:5052/api/webhooks/whatsapp/linked-device";
}

public sealed class MockWhatsAppSender(ILogger<MockWhatsAppSender> logger) : IWhatsAppMessageSender
{
    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation("Mock WhatsApp message queued for {RecipientExternalId}", message.RecipientExternalId);
        return Task.FromResult(new WhatsAppSendResult($"mock-{Guid.NewGuid():N}"));
    }
}

public sealed class MetaWhatsAppSender(
    HttpClient httpClient,
    IOptions<WhatsAppProviderOptions> options,
    ILogger<MetaWhatsAppSender> logger) : IWhatsAppMessageSender
{
    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var phoneNumberId = string.IsNullOrWhiteSpace(message.ChannelExternalId)
            ? configuration.PhoneNumberId
            : message.ChannelExternalId;
        if (string.IsNullOrWhiteSpace(phoneNumberId) || string.IsNullOrWhiteSpace(configuration.AccessToken))
            throw new InvalidOperationException("Meta WhatsApp requiere PhoneNumberId y AccessToken configurados.");

        string? mediaId = null;
        string? providerMessageId = null;

        if (message.ImageData is not null)
        {
            try
            {
                mediaId = await UploadMediaAsync(configuration, phoneNumberId, message, cancellationToken);
                using var imageRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{configuration.GraphApiBaseUrl.TrimEnd('/')}/{phoneNumberId}/messages");
                imageRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", configuration.AccessToken);
                imageRequest.Content = JsonContent.Create(new
                {
                    messaging_product = "whatsapp",
                    to = message.RecipientExternalId,
                    type = "image",
                    image = new { id = mediaId },
                });

                using var imageResponse = await httpClient.SendAsync(imageRequest, cancellationToken);
                var imageResponseBody = await imageResponse.Content.ReadAsStringAsync(cancellationToken);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Meta WhatsApp rechazó la imagen: {(int)imageResponse.StatusCode} {imageResponseBody}");
                }

                providerMessageId = ExtractMessageId(imageResponseBody);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Meta image delivery failed for recipient {Recipient}. Falling back to text-only delivery.", message.RecipientExternalId);
                providerMessageId = null;
                mediaId = null;
            }
        }

        if (mediaId is null && !string.IsNullOrWhiteSpace(message.Text))
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{configuration.GraphApiBaseUrl.TrimEnd('/')}/{phoneNumberId}/messages");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", configuration.AccessToken);
            request.Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                to = message.RecipientExternalId,
                type = "text",
                text = new { body = message.Text },
            });

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Meta WhatsApp rechazó el mensaje: {(int)response.StatusCode} {responseBody}");
            }

            providerMessageId ??= ExtractMessageId(responseBody);
            return new WhatsAppSendResult(providerMessageId);
        }

        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            if (mediaId is not null && !string.IsNullOrWhiteSpace(message.Text))
            {
                await SendTextOnlyAsync(configuration, phoneNumberId, message.RecipientExternalId, message.Text, cancellationToken);
            }
            return new WhatsAppSendResult(providerMessageId);
        }

        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            return await SendTextOnlyAsync(configuration, phoneNumberId, message.RecipientExternalId, message.Text, cancellationToken);
        }

        throw new InvalidOperationException("Meta WhatsApp no pudo enviar ni la imagen ni el texto.");
    }

    private async Task<WhatsAppSendResult> SendTextOnlyAsync(
        WhatsAppProviderOptions configuration,
        string phoneNumberId,
        string recipientExternalId,
        string text,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuration.GraphApiBaseUrl.TrimEnd('/')}/{phoneNumberId}/messages");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", configuration.AccessToken);
        request.Content = JsonContent.Create(new
        {
            messaging_product = "whatsapp",
            to = recipientExternalId,
            type = "text",
            text = new { body = text },
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Meta WhatsApp rechazó el texto: {(int)response.StatusCode} {body}");
        }

        return new WhatsAppSendResult(ExtractMessageId(body));
    }

    private async Task<string> UploadMediaAsync(
        WhatsAppProviderOptions configuration,
        string phoneNumberId,
        WhatsAppOutboundMessage message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuration.GraphApiBaseUrl.TrimEnd('/')}/{phoneNumberId}/media");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", configuration.AccessToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whatsapp"), "messaging_product");
        var imageContent = new ByteArrayContent(message.ImageData!);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(message.ImageMediaType ?? "image/jpeg");
        content.Add(imageContent, "file", "vanta-image");
        request.Content = content;

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Meta WhatsApp rechazó la imagen: {(int)response.StatusCode} {body}");

        using var document = System.Text.Json.JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("id", out var id))
            throw new HttpRequestException("Meta WhatsApp no devolvió un ID de imagen.");
        return id.GetString() ?? throw new HttpRequestException("Meta WhatsApp devolvió un ID de imagen vacío.");
    }

    private static string ExtractMessageId(string responseBody)
    {
        using var document = System.Text.Json.JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0 &&
            messages[0].TryGetProperty("id", out var id))
            return id.GetString() ?? string.Empty;
        return string.Empty;
    }
}
