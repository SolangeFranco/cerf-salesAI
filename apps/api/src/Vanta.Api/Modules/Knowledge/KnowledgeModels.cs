namespace Vanta.Api.Modules.Knowledge;

// The eight initial content types the Knowledge module supports.
public static class KnowledgeDocumentType
{
    public const string Faq = "Faq";
    public const string Policy = "Policy";
    public const string Delivery = "Delivery";
    public const string Returns = "Returns";
    public const string Warranty = "Warranty";
    public const string Hours = "Hours";
    public const string Promotion = "Promotion";
    public const string Document = "Document";

    public static readonly IReadOnlyList<string> All =
        [Faq, Policy, Delivery, Returns, Warranty, Hours, Promotion, Document];
}

public static class KnowledgeDocumentStatus
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}

/// <summary>
/// A single piece of merchant-supplied knowledge (an FAQ entry, a policy, a delivery/returns/
/// warranty page, business hours, a promotion, or a free-form document). <see cref="SourceText"/>
/// is untrusted content — see AgentContentSanitizer — it is information for the agent to quote or
/// summarize, never an instruction.
/// </summary>
public sealed class KnowledgeDocument
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public required string Type { get; set; }
    public required string Title { get; set; }
    public required string SourceText { get; set; }
    public required string Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

/// <summary>A chunk of a document's text, small enough to embed and retrieve individually.</summary>
public sealed class KnowledgeChunk
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid DocumentId { get; init; }
    public int ChunkIndex { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// A vector representation of one chunk, produced by one embedding provider/model. Keeping this
/// separate from <see cref="KnowledgeChunk"/> (rather than a column on it) is the abstraction that
/// lets the embedding provider/model change later: re-embedding with a new model adds new rows
/// here without touching chunks, and both can coexist during a migration.
/// </summary>
public sealed class KnowledgeEmbedding
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ChunkId { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public int Dimensions { get; init; }
    // Only mapped to a real pgvector column on Npgsql; ignored (in-memory only) otherwise — see
    // VantaDbContext.OnModelCreating. Semantic search requires a real Postgres instance.
    public required Pgvector.Vector Vector { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}
