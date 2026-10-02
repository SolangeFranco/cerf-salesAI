using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.WhatsApp;

public interface IVisualProductMatcher
{
    Task<AgentProductToolResult?> FindBestMatchAsync(Guid tenantId, byte[] imageData, CancellationToken cancellationToken);
}

public sealed class VisualProductMatcher(VantaDbContext db) : IVisualProductMatcher
{
    private const int FingerprintSize = 8;
    private const double MatchThreshold = 0.82;

    public async Task<AgentProductToolResult?> FindBestMatchAsync(Guid tenantId, byte[] imageData, CancellationToken cancellationToken)
    {
        var incoming = TryCreateFingerprint(imageData);
        if (incoming is null) return null;

        var products = await db.Products.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.IsActive && current.ImageData != null)
            .Select(current => new { current.Id, current.Name, current.Sku, current.Price, current.ImageData, current.ImageMediaType })
            .ToListAsync(cancellationToken);

        AgentProductToolResult? bestProduct = null;
        var bestScore = 0d;
        foreach (var product in products)
        {
            var candidate = TryCreateFingerprint(product.ImageData!);
            if (candidate is null) continue;
            var score = Similarity(incoming, candidate);
            if (score <= bestScore) continue;
            bestScore = score;
            bestProduct = new AgentProductToolResult(product.Id, product.Name, product.Sku, product.Price, Convert.ToBase64String(product.ImageData!), product.ImageMediaType);
        }

        return bestScore >= MatchThreshold ? bestProduct : null;
    }

    private static double[]? TryCreateFingerprint(byte[] data)
    {
        try
        {
            using var image = Image.Load<Rgba32>(data);
            image.Mutate(context => context.Resize(FingerprintSize, FingerprintSize));
            var fingerprint = new double[FingerprintSize * FingerprintSize * 3];
            var index = 0;
            for (var y = 0; y < FingerprintSize; y++)
            {
                for (var x = 0; x < FingerprintSize; x++)
                {
                    var pixel = image[x, y];
                    fingerprint[index++] = pixel.R / 255d;
                    fingerprint[index++] = pixel.G / 255d;
                    fingerprint[index++] = pixel.B / 255d;
                }
            }
            return fingerprint;
        }
        catch
        {
            return null;
        }
    }

    private static double Similarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var distance = 0d;
        for (var i = 0; i < left.Count; i++)
        {
            var delta = left[i] - right[i];
            distance += delta * delta;
        }

        var normalized = Math.Sqrt(distance / left.Count);
        return 1d - normalized;
    }
}