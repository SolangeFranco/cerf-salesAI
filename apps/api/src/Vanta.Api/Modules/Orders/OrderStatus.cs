namespace Vanta.Api.Modules.Orders;

public static class OrderStatus
{
    public const string New = "New";
    public const string Confirmed = "Confirmed";
    public const string AwaitingPayment = "AwaitingPayment";
    public const string Paid = "Paid";
    public const string Preparing = "Preparing";
    public const string Shipped = "Shipped";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";
    public const string Refunded = "Refunded";
    public const string PaymentFailed = "PaymentFailed";

    // Kept for backward compatibility with existing call sites; delegates to the full lifecycle table.
    public static bool CanTransition(string current, string next) => OrderStateMachine.CanTransition(current, next);
}
