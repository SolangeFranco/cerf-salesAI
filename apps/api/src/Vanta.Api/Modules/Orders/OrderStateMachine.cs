namespace Vanta.Api.Modules.Orders;

/// <summary>
/// Explicit, centralized order lifecycle rules. Any transition not listed here is invalid.
/// Payment-driven transitions (AwaitingPayment, Paid, PaymentFailed) are only reachable through
/// the payment flow (PaymentEndpoints), never through a direct staff-issued status change.
/// </summary>
public static class OrderStateMachine
{
    private static readonly HashSet<(string From, string To)> Transitions =
    [
        (OrderStatus.New, OrderStatus.Confirmed),
        (OrderStatus.New, OrderStatus.Cancelled),

        (OrderStatus.Confirmed, OrderStatus.AwaitingPayment),
        (OrderStatus.Confirmed, OrderStatus.Cancelled),

        (OrderStatus.AwaitingPayment, OrderStatus.Paid),
        (OrderStatus.AwaitingPayment, OrderStatus.PaymentFailed),
        (OrderStatus.AwaitingPayment, OrderStatus.Cancelled),

        (OrderStatus.PaymentFailed, OrderStatus.AwaitingPayment),
        (OrderStatus.PaymentFailed, OrderStatus.Cancelled),

        (OrderStatus.Paid, OrderStatus.Preparing),
        (OrderStatus.Paid, OrderStatus.Refunded),

        (OrderStatus.Preparing, OrderStatus.Shipped),
        (OrderStatus.Preparing, OrderStatus.Refunded),

        (OrderStatus.Shipped, OrderStatus.Delivered),
        (OrderStatus.Shipped, OrderStatus.Refunded),

        (OrderStatus.Delivered, OrderStatus.Refunded),
    ];

    // Subset of Transitions a merchant/staff can trigger directly via PATCH /orders/{id}/status.
    // AwaitingPayment, Paid and PaymentFailed are excluded: they must originate from the payment
    // flow (payment creation, webhook confirmation, or provider failure notification).
    private static readonly HashSet<(string From, string To)> ManualTransitions =
    [
        (OrderStatus.New, OrderStatus.Confirmed),
        (OrderStatus.New, OrderStatus.Cancelled),
        (OrderStatus.Confirmed, OrderStatus.Cancelled),
        (OrderStatus.AwaitingPayment, OrderStatus.Cancelled),
        (OrderStatus.PaymentFailed, OrderStatus.Cancelled),
        (OrderStatus.Paid, OrderStatus.Preparing),
        (OrderStatus.Paid, OrderStatus.Refunded),
        (OrderStatus.Preparing, OrderStatus.Shipped),
        (OrderStatus.Preparing, OrderStatus.Refunded),
        (OrderStatus.Shipped, OrderStatus.Delivered),
        (OrderStatus.Shipped, OrderStatus.Refunded),
        (OrderStatus.Delivered, OrderStatus.Refunded),
    ];

    public static bool CanTransition(string current, string next) => Transitions.Contains((current, next));

    public static bool CanTransitionManually(string current, string next) => ManualTransitions.Contains((current, next));

    // Cancelling or refunding an order releases any reservation still held; Refunded after
    // Delivered is a no-op release since the reservation was already committed at fulfillment.
    public static bool RequiresInventoryRelease(string next) => next is OrderStatus.Cancelled or OrderStatus.Refunded;

    public static bool RequiresInventoryCommit(string next) => next == OrderStatus.Delivered;

    public static bool IsTerminal(string status) => !Transitions.Any(transition => transition.From == status);

    public static IReadOnlyCollection<string> GetValidNextStates(string current) =>
        Transitions.Where(transition => transition.From == current).Select(transition => transition.To).ToArray();
}
