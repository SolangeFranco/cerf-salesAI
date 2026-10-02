using System.Security.Cryptography;
using System.Text;

namespace Vanta.Api.Modules.Knowledge;

public sealed class EmbeddingOptions
{
    public string Provider { get; set; } = "mock"; // "mock", "gemini"
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "mock-embedding-v1";
}

/// <summary>
/// Isolates the Knowledge module from any specific embedding model/provider. Swapping providers
/// later only means adding a new <see cref="IEmbeddingProvider"/> implementation and reprocessing
/// documents — chunks, storage and search code never change. All implementations must return a
/// vector of exactly <see cref="Dimensions"/> length, matching the fixed `vector(N)` column.
/// </summary>
public interface IEmbeddingProvider
{
    string Name { get; }
    string Model { get; }
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

/// <summary>
/// Deterministic, no-external-call embedding used by default and in every test. Uses the
/// "hashing trick": each normalized word is hashed into one of <see cref="Dimensions"/> buckets
/// and accumulated, then L2-normalized. Text that shares more words produces vectors with higher
/// cosine similarity, which is enough to exercise real semantic-search ranking in tests without
/// depending on an external API for the whole suite.
/// </summary>
public sealed class MockEmbeddingProvider : IEmbeddingProvider
{
    public const int VectorDimensions = 256;

    public string Name => "mock";
    public string Model => "mock-embedding-v1";
    public int Dimensions => VectorDimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = new float[Dimensions];
        var words = (text ?? string.Empty)
            .ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '"', '\''], StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            var bucket = (int)(Hash(word) % (uint)Dimensions);
            vector[bucket] += 1f;
        }

        var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0)
        {
            for (var i = 0; i < vector.Length; i++) vector[i] /= magnitude;
        }
        return Task.FromResult(vector);
    }

    private static uint Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt32(bytes, 0);
    }
}
