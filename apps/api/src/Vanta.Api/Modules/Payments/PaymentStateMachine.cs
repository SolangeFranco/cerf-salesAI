namespace Vanta.Api.Modules.Payments;

// Identifies where a payment status transition originated, for audit purposes.
public static class PaymentTransitionSource
{
    public const string Creation = "Creation";
    public const string Webhook = "Webhook";
    public const string ServerVerification = "ServerVerification";
    public const string MockConfirm = "MockConfirm";
    public const string ManualConfirmation = "ManualConfirmation";
    public const string Refund = "Refund";
    public const string Expiration = "Expiration";
}

/// <summary>
/// Explicit, centralized payment lifecycle rules, mirroring <see cref="Orders.OrderStateMachine"/>.
/// Any transition not listed here is invalid regardless of caller.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly HashSet<(string From, string To)> Transitions =
    [
        (PaymentStatus.Pending, PaymentStatus.Paid),
        (PaymentStatus.Pending, PaymentStatus.Failed),
        (PaymentStatus.Pending, PaymentStatus.Expired),
        (PaymentStatus.Expired, PaymentStatus.PaidAfterExpiration),
        (PaymentStatus.Paid, PaymentStatus.Refunded),
        (PaymentStatus.PaidAfterExpiration, PaymentStatus.Refunded),
    ];

    public static bool CanTransition(string current, string next) => Transitions.Contains((current, next));

    public static bool IsTerminal(string status) => !Transitions.Any(transition => transition.From == status);
}
