namespace Vanta.Api.Modules.Knowledge;

/// <summary>
/// Explicit, centralized document processing lifecycle, mirroring
/// <see cref="Payments.PaymentStateMachine"/>/<see cref="Orders.OrderStateMachine"/>. Any
/// transition not listed here is invalid regardless of caller.
/// </summary>
public static class KnowledgeDocumentStateMachine
{
    private static readonly HashSet<(string From, string To)> Transitions =
    [
        (KnowledgeDocumentStatus.Pending, KnowledgeDocumentStatus.Processing),
        (KnowledgeDocumentStatus.Processing, KnowledgeDocumentStatus.Ready),
        (KnowledgeDocumentStatus.Processing, KnowledgeDocumentStatus.Failed),
        // Reprocessing: a ready or failed document can be sent back through the pipeline.
        (KnowledgeDocumentStatus.Ready, KnowledgeDocumentStatus.Processing),
        (KnowledgeDocumentStatus.Failed, KnowledgeDocumentStatus.Processing),
    ];

    public static bool CanTransition(string current, string next) => Transitions.Contains((current, next));
}
