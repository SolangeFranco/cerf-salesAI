namespace Vanta.Api.Modules.Knowledge;

/// <summary>
/// Splits a document's raw text into overlapping, word-boundary-safe windows small enough to
/// embed individually. Deliberately simple (fixed character window + overlap) rather than
/// sentence/semantic-aware splitting — this is enough for FAQ/policy/delivery-length content.
/// </summary>
public static class KnowledgeChunker
{
    private const int MaxChunkLength = 800;
    private const int OverlapLength = 100;

    public static IReadOnlyList<string> Chunk(string sourceText)
    {
        var normalized = (sourceText ?? string.Empty).Trim();
        if (normalized.Length == 0) return [];
        if (normalized.Length <= MaxChunkLength) return [normalized];

        var chunks = new List<string>();
        var start = 0;
        while (start < normalized.Length)
        {
            var length = Math.Min(MaxChunkLength, normalized.Length - start);
            var end = start + length;
            if (end < normalized.Length)
            {
                // Back off to the nearest preceding whitespace so words are never cut in half.
                var lastSpace = normalized.LastIndexOf(' ', end - 1, Math.Min(length, 200));
                if (lastSpace > start) end = lastSpace;
            }

            var chunk = normalized[start..end].Trim();
            if (chunk.Length > 0) chunks.Add(chunk);

            if (end >= normalized.Length) break;
            start = Math.Max(end - OverlapLength, start + 1);
        }
        return chunks;
    }
}
