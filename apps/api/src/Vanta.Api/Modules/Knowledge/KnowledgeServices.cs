using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Knowledge;

/// <summary>
/// Owns the document processing pipeline: chunk -> embed -> ready. Mirrors the
/// state-machine-backed services elsewhere (PaymentService, ConversationService) but does not
/// need atomic conditional updates — a document is only ever processed by the single call that
/// created or reprocessed it, not raced by concurrent external triggers.
/// </summary>
public sealed class KnowledgeIngestionService(VantaDbContext db, IEmbeddingProvider embeddingProvider)
{
    public async Task<KnowledgeDocument> IngestAsync(
        Guid tenantId, string type, string title, string sourceText, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var document = new KnowledgeDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Type = type, Title = title.Trim(),
            SourceText = sourceText.Trim(), Status = KnowledgeDocumentStatus.Pending,
            CreatedAt = now, UpdatedAt = now,
        };
        db.KnowledgeDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await ProcessAsync(document, cancellationToken);
        return document;
    }

    public async Task<bool> ReprocessAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(
            current => current.Id == documentId && current.TenantId == tenantId, cancellationToken);
        if (document is null) return false;
        if (!KnowledgeDocumentStateMachine.CanTransition(document.Status, KnowledgeDocumentStatus.Processing)) return false;

        await DeleteChunksAndEmbeddingsAsync(tenantId, documentId, cancellationToken);

        document.Status = KnowledgeDocumentStatus.Processing;
        document.ErrorMessage = null;
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await ProcessAsync(document, cancellationToken);
        return true;
    }

    private async Task ProcessAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        if (document.Status == KnowledgeDocumentStatus.Pending)
        {
            document.Status = KnowledgeDocumentStatus.Processing;
            document.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var index = 0;
            foreach (var text in KnowledgeChunker.Chunk(document.SourceText))
            {
                var chunk = new KnowledgeChunk
                {
                    Id = Guid.NewGuid(), TenantId = document.TenantId, DocumentId = document.Id,
                    ChunkIndex = index++, Content = text, CreatedAt = now,
                };
                db.KnowledgeChunks.Add(chunk);

                var values = await embeddingProvider.EmbedAsync(text, cancellationToken);
                db.KnowledgeEmbeddings.Add(new KnowledgeEmbedding
                {
                    Id = Guid.NewGuid(), TenantId = document.TenantId, ChunkId = chunk.Id,
                    Provider = embeddingProvider.Name, Model = embeddingProvider.Model,
                    Dimensions = embeddingProvider.Dimensions, Vector = new Vector(values), CreatedAt = now,
                });
            }

            document.Status = KnowledgeDocumentStatus.Ready;
            document.ErrorMessage = null;
        }
        catch (Exception exception)
        {
            document.Status = KnowledgeDocumentStatus.Failed;
            document.ErrorMessage = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
        }

        document.ProcessedAt = DateTimeOffset.UtcNow;
        document.UpdatedAt = document.ProcessedAt.Value;
        await db.SaveChangesAsync(cancellationToken);
    }

    // EF Core's InMemory provider (used by tests without Docker) does not support the bulk
    // ExecuteDeleteAsync operations, so this falls back to loading + removing entities there.
    public async Task DeleteChunksAndEmbeddingsAsync(Guid tenantId, Guid documentId, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            var chunkIds = await db.KnowledgeChunks.AsNoTracking()
                .Where(chunk => chunk.DocumentId == documentId && chunk.TenantId == tenantId)
                .Select(chunk => chunk.Id)
                .ToListAsync(cancellationToken);
            if (chunkIds.Count == 0) return;

            await db.KnowledgeEmbeddings.Where(embedding => chunkIds.Contains(embedding.ChunkId) && embedding.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.KnowledgeChunks.Where(chunk => chunk.DocumentId == documentId && chunk.TenantId == tenantId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var chunks = await db.KnowledgeChunks.Where(chunk => chunk.DocumentId == documentId && chunk.TenantId == tenantId).ToListAsync(cancellationToken);
        if (chunks.Count == 0) return;
        var ids = chunks.Select(chunk => chunk.Id).ToList();
        var embeddings = await db.KnowledgeEmbeddings.Where(embedding => ids.Contains(embedding.ChunkId) && embedding.TenantId == tenantId).ToListAsync(cancellationToken);
        db.KnowledgeEmbeddings.RemoveRange(embeddings);
        db.KnowledgeChunks.RemoveRange(chunks);
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record KnowledgeSearchResult(Guid DocumentId, string DocumentType, string DocumentTitle, Guid ChunkId, string Content);

/// <summary>
/// Semantic search over Ready documents' chunks. Requires real Postgres/pgvector for actual
/// vector ranking; under the EF InMemory provider (unit tests without Docker) it falls back to a
/// plain keyword match so the tool boundary and tenant scoping remain testable — see
/// KnowledgeSemanticSearchTests for the real-Postgres ranking assertions.
/// </summary>
public sealed class KnowledgeSearchService(VantaDbContext db, IEmbeddingProvider embeddingProvider)
{
    private const int TopK = 3;

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(Guid tenantId, string query, CancellationToken cancellationToken)
    {
        var sanitizedQuery = WhatsApp.AgentContentSanitizer.SanitizeUserMessageForPrompt(query);
        if (string.IsNullOrWhiteSpace(sanitizedQuery)) return [];

        return db.Database.IsNpgsql()
            ? await SearchByVectorAsync(tenantId, sanitizedQuery, cancellationToken)
            : await SearchByKeywordAsync(tenantId, sanitizedQuery, cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeSearchResult>> SearchByVectorAsync(Guid tenantId, string query, CancellationToken cancellationToken)
    {
        var queryVector = new Vector(await embeddingProvider.EmbedAsync(query, cancellationToken));
        return await db.KnowledgeEmbeddings.AsNoTracking()
            .Where(embedding => embedding.TenantId == tenantId && embedding.Provider == embeddingProvider.Name && embedding.Model == embeddingProvider.Model)
            .Join(db.KnowledgeChunks.AsNoTracking(), embedding => embedding.ChunkId, chunk => chunk.Id, (embedding, chunk) => new { embedding, chunk })
            .Join(db.KnowledgeDocuments.AsNoTracking().Where(document => document.TenantId == tenantId && document.Status == KnowledgeDocumentStatus.Ready),
                row => row.chunk.DocumentId, document => document.Id, (row, document) => new { row.embedding, row.chunk, document })
            .OrderBy(row => row.embedding.Vector.CosineDistance(queryVector))
            .Take(TopK)
            .Select(row => new KnowledgeSearchResult(row.document.Id, row.document.Type, row.document.Title, row.chunk.Id, row.chunk.Content))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeSearchResult>> SearchByKeywordAsync(Guid tenantId, string query, CancellationToken cancellationToken)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(term => term.Length >= 3).ToArray();
        if (terms.Length == 0) return [];

        var candidates = await db.KnowledgeChunks.AsNoTracking()
            .Where(chunk => chunk.TenantId == tenantId)
            .Join(db.KnowledgeDocuments.AsNoTracking().Where(document => document.TenantId == tenantId && document.Status == KnowledgeDocumentStatus.Ready),
                chunk => chunk.DocumentId, document => document.Id, (chunk, document) => new { chunk, document })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(row => terms.Any(term => row.chunk.Content.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(TopK)
            .Select(row => new KnowledgeSearchResult(row.document.Id, row.document.Type, row.document.Title, row.chunk.Id, row.chunk.Content))
            .ToList();
    }
}
