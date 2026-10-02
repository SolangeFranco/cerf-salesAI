using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.Knowledge;

/// <summary>
/// Real embedding provider backed by Gemini's embedding model. Only used when
/// Embeddings:Provider = "gemini" and an API key is configured; otherwise the DI registration in
/// Program.cs falls back to <see cref="MockEmbeddingProvider"/>, keeping the whole test suite
/// independent of any external API.
/// </summary>
public sealed class GeminiEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options, ILogger<GeminiEmbeddingProvider> logger) : IEmbeddingProvider
{
    // text-embedding-004 produces 768-dimensional vectors.
    public const int VectorDimensions = 768;

    public string Name => "gemini";
    public string Model => string.IsNullOrWhiteSpace(options.Value.Model) ? "text-embedding-004" : options.Value.Model;
    public int Dimensions => VectorDimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var config = options.Value;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent?key={config.ApiKey}";
        var payload = new { content = new { parts = new[] { new { text } } } };

        using var response = await httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini embedding API falló con estado {StatusCode}.", response.StatusCode);
            throw new InvalidOperationException($"Gemini embedding request failed with status {response.StatusCode}.");
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var values = document.RootElement.GetProperty("embedding").GetProperty("values");
        var vector = new float[values.GetArrayLength()];
        var index = 0;
        foreach (var value in values.EnumerateArray()) vector[index++] = value.GetSingle();
        return vector;
    }
}
