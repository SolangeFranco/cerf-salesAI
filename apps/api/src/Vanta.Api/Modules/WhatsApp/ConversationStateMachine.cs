namespace Vanta.Api.Modules.WhatsApp;

// The four states a conversation can be in. Exactly one of these is true at any time.
public static class ConversationStatus
{
    public const string AiActive = "AI_ACTIVE";
    public const string HumanActive = "HUMAN_ACTIVE";
    public const string Paused = "PAUSED";
    public const string Closed = "CLOSED";
}

// Identifies why a conversation activity/state change happened, for the audit trail.
public static class ConversationActivityType
{
    public const string StateChanged = "StateChanged";
    public const string Assigned = "Assigned";
    public const string Unassigned = "Unassigned";
    public const string Paused = "Paused";
    public const string Resumed = "Resumed";
    public const string HandoffToHuman = "HandoffToHuman";
    public const string HandoffToAi = "HandoffToAi";
    public const string Closed = "Closed";
    public const string Reopened = "Reopened";
    public const string GlobalPaused = "GlobalPaused";
    public const string GlobalResumed = "GlobalResumed";
}

/// <summary>
/// Explicit, centralized conversation lifecycle rules, mirroring
/// <see cref="Orders.OrderStateMachine"/>/<see cref="Payments.PaymentStateMachine"/>. Any
/// transition not listed here is invalid regardless of caller.
/// </summary>
public static class ConversationStateMachine
{
    private static readonly HashSet<(string From, string To)> Transitions =
    [
        (ConversationStatus.AiActive, ConversationStatus.HumanActive),
        (ConversationStatus.AiActive, ConversationStatus.Paused),
        (ConversationStatus.AiActive, ConversationStatus.Closed),
        (ConversationStatus.HumanActive, ConversationStatus.AiActive),
        (ConversationStatus.HumanActive, ConversationStatus.Paused),
        (ConversationStatus.HumanActive, ConversationStatus.Closed),
        (ConversationStatus.Paused, ConversationStatus.AiActive),
        (ConversationStatus.Paused, ConversationStatus.HumanActive),
        (ConversationStatus.Paused, ConversationStatus.Closed),
        // A closed conversation reopens (to the AI) when the customer writes again.
        (ConversationStatus.Closed, ConversationStatus.AiActive),
        (ConversationStatus.Closed, ConversationStatus.Paused),
    ];

    public static bool CanTransition(string current, string next) => Transitions.Contains((current, next));
}
