using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;

namespace Vanta.Api.Modules.Knowledge;

public sealed record CreateKnowledgeDocumentRequest(
    [property: Required] string Type,
    [property: Required, StringLength(200, MinimumLength = 2)] string Title,
    [property: Required, StringLength(20000, MinimumLength = 1)] string SourceText);

public sealed record KnowledgeDocumentResponse(
    Guid Id, string Type, string Title, string Status, string? ErrorMessage,
    int ChunkCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ProcessedAt);

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeModule(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/knowledge/documents").RequireAuthorization();
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{documentId:guid}", GetAsync);
        group.MapPost("/{documentId:guid}/reprocess", ReprocessAsync);
        group.MapDelete("/{documentId:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var documents = await db.KnowledgeDocuments.AsNoTracking()
            .Where(document => document.TenantId == tenantId)
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken);
        var chunkCounts = await db.KnowledgeChunks.AsNoTracking()
            .Where(chunk => chunk.TenantId == tenantId)
            .GroupBy(chunk => chunk.DocumentId)
            .Select(group => new { DocumentId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.DocumentId, entry => entry.Count, cancellationToken);
        return Results.Ok(documents.Select(document => ToResponse(document, chunkCounts.GetValueOrDefault(document.Id))));
    }

    private static async Task<IResult> CreateAsync(
        CreateKnowledgeDocumentRequest request, ClaimsPrincipal user, KnowledgeIngestionService ingestion, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        if (!KnowledgeDocumentType.All.Contains(request.Type))
            return Results.BadRequest(new { error = "Tipo de documento no válido.", allowed = KnowledgeDocumentType.All });

        var document = await ingestion.IngestAsync(tenantId, request.Type, request.Title, request.SourceText, cancellationToken);
        var chunkCount = await db.KnowledgeChunks.AsNoTracking().CountAsync(chunk => chunk.DocumentId == document.Id && chunk.TenantId == tenantId, cancellationToken);
        return Results.Created($"/api/knowledge/documents/{document.Id}", ToResponse(document, chunkCount));
    }

    private static async Task<IResult> GetAsync(Guid documentId, ClaimsPrincipal user, VantaDbContext db, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var document = await db.KnowledgeDocuments.AsNoTracking()
            .FirstOrDefaultAsync(current => current.Id == documentId && current.TenantId == tenantId, cancellationToken);
        if (document is null) return Results.NotFound();
        var chunkCount = await db.KnowledgeChunks.AsNoTracking().CountAsync(chunk => chunk.DocumentId == documentId && chunk.TenantId == tenantId, cancellationToken);
        return Results.Ok(ToResponse(document, chunkCount));
    }

    private static async Task<IResult> ReprocessAsync(
        Guid documentId, ClaimsPrincipal user, KnowledgeIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var reprocessed = await ingestion.ReprocessAsync(tenantId, documentId, cancellationToken);
        return reprocessed ? Results.Ok(new { queued = true }) : Results.NotFound();
    }

    private static async Task<IResult> DeleteAsync(
        Guid documentId, ClaimsPrincipal user, VantaDbContext db, KnowledgeIngestionService ingestion, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(current => current.Id == documentId && current.TenantId == tenantId, cancellationToken);
        if (document is null) return Results.NotFound();

        await ingestion.DeleteChunksAndEmbeddingsAsync(tenantId, documentId, cancellationToken);
        db.KnowledgeDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);

    private static KnowledgeDocumentResponse ToResponse(KnowledgeDocument document, int chunkCount) => new(
        document.Id, document.Type, document.Title, document.Status, document.ErrorMessage,
        chunkCount, document.CreatedAt, document.UpdatedAt, document.ProcessedAt);
}
