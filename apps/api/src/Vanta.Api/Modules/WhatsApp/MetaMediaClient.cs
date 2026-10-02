using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record MetaMediaDownload(string MediaType, byte[] Data);

public interface IMetaMediaClient
{
    Task<MetaMediaDownload?> DownloadAsync(string mediaId, CancellationToken cancellationToken);
}

public sealed class MetaMediaClient(
    HttpClient httpClient,
    IOptions<WhatsAppProviderOptions> options) : IMetaMediaClient
{
    public async Task<MetaMediaDownload?> DownloadAsync(string mediaId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return null;
        var configuration = options.Value;
        if (string.IsNullOrWhiteSpace(configuration.AccessToken))
            throw new InvalidOperationException("Meta WhatsApp requiere AccessToken configurado.");

        using var metadataRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"{configuration.GraphApiBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(mediaId)}");
        metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);
        using var metadataResponse = await httpClient.SendAsync(metadataRequest, cancellationToken);
        if (!metadataResponse.IsSuccessStatusCode) return null;
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<MetaMediaMetadata>(cancellationToken);
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Url)) return null;

        using var mediaRequest = new HttpRequestMessage(HttpMethod.Get, metadata.Url);
        mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);
        using var mediaResponse = await httpClient.SendAsync(mediaRequest, cancellationToken);
        if (!mediaResponse.IsSuccessStatusCode) return null;
        return new MetaMediaDownload(metadata.MimeType ?? "application/octet-stream", await mediaResponse.Content.ReadAsByteArrayAsync(cancellationToken));
    }

    private sealed record MetaMediaMetadata(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("mime_type")] string? MimeType);
}
