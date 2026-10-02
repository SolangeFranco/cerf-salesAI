using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Knowledge;
using Vanta.Api.Modules.Tenants;
using Xunit;

namespace Vanta.Api.UnitTests;

// Runs against real Postgres/pgvector: EF Core's InMemory provider ignores the Vector column
// entirely (see VantaDbContext), so real semantic ranking can only be verified here (mirrors
// PaymentConcurrencyTests/InventoryConcurrencyTests/ConversationConcurrencyTests).
public sealed class KnowledgeSemanticSearchTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=vanta;Username=vanta;Password=vanta_dev_only";

    [Fact]
    public async Task Search_ranks_the_semantically_closest_ready_document_first()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(setup);
        var ingestion = new KnowledgeIngestionService(setup, new MockEmbeddingProvider());

        await ingestion.IngestAsync(tenant.Id, KnowledgeDocumentType.Hours, "Horarios",
            "Nuestro horario de atención es de lunes a viernes de 8 a 18 horas.", CancellationToken.None);
        await ingestion.IngestAsync(tenant.Id, KnowledgeDocumentType.Returns, "Devoluciones",
            "Aceptamos devoluciones y cambios dentro de los 30 días con el ticket de compra.", CancellationToken.None);

        var search = new KnowledgeSearchService(setup, new MockEmbeddingProvider());
        var results = await search.SearchAsync(tenant.Id, "¿Cuál es el horario de atención los sábados?", CancellationToken.None);

        Assert.NotEmpty(results);
        Assert.Equal(KnowledgeDocumentType.Hours, results[0].DocumentType);
    }

    [Fact]
    public async Task Search_never_returns_another_tenants_documents()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenantA = await CreateTenantAsync(setup);
        var tenantB = await CreateTenantAsync(setup);
        var ingestion = new KnowledgeIngestionService(setup, new MockEmbeddingProvider());

        await ingestion.IngestAsync(tenantB.Id, KnowledgeDocumentType.Faq, "FAQ de otro tenant",
            "Nuestro horario de atención es de lunes a viernes de 8 a 18 horas.", CancellationToken.None);

        var search = new KnowledgeSearchService(setup, new MockEmbeddingProvider());
        var results = await search.SearchAsync(tenantA.Id, "¿A qué hora abren?", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Documents_not_yet_ready_are_excluded_from_search()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(setup);
        var ingestion = new KnowledgeIngestionService(setup, new MockEmbeddingProvider());
        var document = await ingestion.IngestAsync(tenant.Id, KnowledgeDocumentType.Faq, "FAQ",
            "Nuestro horario de atención es de lunes a viernes de 8 a 18 horas.", CancellationToken.None);

        // Force it back to Pending, simulating a document that hasn't finished processing yet.
        await setup.KnowledgeDocuments.Where(current => current.Id == document.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.Status, KnowledgeDocumentStatus.Pending));

        var search = new KnowledgeSearchService(setup, new MockEmbeddingProvider());
        var results = await search.SearchAsync(tenant.Id, "horario de atención", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Reprocessing_replaces_the_stored_embeddings()
    {
        await using var setup = CreateContext();
        await setup.Database.MigrateAsync();
        var tenant = await CreateTenantAsync(setup);
        var ingestion = new KnowledgeIngestionService(setup, new MockEmbeddingProvider());
        var document = await ingestion.IngestAsync(tenant.Id, KnowledgeDocumentType.Warranty, "Garantía",
            "La garantía cubre defectos de fábrica durante 12 meses.", CancellationToken.None);

        var chunkIdsBefore = await setup.KnowledgeChunks.AsNoTracking()
            .Where(chunk => chunk.DocumentId == document.Id).Select(chunk => chunk.Id).ToListAsync();

        var reprocessed = await ingestion.ReprocessAsync(tenant.Id, document.Id, CancellationToken.None);

        Assert.True(reprocessed);
        var chunkIdsAfter = await setup.KnowledgeChunks.AsNoTracking()
            .Where(chunk => chunk.DocumentId == document.Id).Select(chunk => chunk.Id).ToListAsync();
        Assert.NotEmpty(chunkIdsAfter);
        // Old chunk rows (and their embeddings) were replaced, not merely appended to.
        Assert.Empty(chunkIdsBefore.Intersect(chunkIdsAfter));

        var embeddingCount = await setup.KnowledgeEmbeddings.AsNoTracking().CountAsync(embedding => chunkIdsAfter.Contains(embedding.ChunkId));
        Assert.Equal(chunkIdsAfter.Count, embeddingCount);
    }

    private static async Task<Tenant> CreateTenantAsync(VantaDbContext db)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = $"Knowledge {Guid.NewGuid():N}", Slug = $"knowledge-{Guid.NewGuid():N}", CreatedAt = DateTimeOffset.UtcNow };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private static VantaDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VantaDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.UseVector()).Options);
}
