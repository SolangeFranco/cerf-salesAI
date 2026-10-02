using Vanta.Api.Modules.Orders;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class OrderStateMachineTests
{
    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Paid)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Paid, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Paid, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Refunded)]
    public void Valid_transitions_are_accepted(string from, string to)
    {
        Assert.True(OrderStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Paid)]
    [InlineData(OrderStatus.New, OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.New, OrderStatus.Preparing)]
    [InlineData(OrderStatus.New, OrderStatus.Delivered)]
    [InlineData(OrderStatus.New, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Paid)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.New)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Preparing)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Preparing)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Refunded)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.Paid)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.Paid, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Paid, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Paid, OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.New)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Refunded, OrderStatus.Paid)]
    public void Invalid_transitions_are_rejected(string from, string to)
    {
        Assert.False(OrderStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed, OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Paid)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.AwaitingPayment)]
    public void Payment_driven_transitions_are_not_allowed_manually(string from, string to)
    {
        Assert.True(OrderStateMachine.CanTransition(from, to));
        Assert.False(OrderStateMachine.CanTransitionManually(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.PaymentFailed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Paid, OrderStatus.Preparing)]
    [InlineData(OrderStatus.Paid, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Preparing, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Refunded)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Refunded)]
    public void Manual_transitions_match_the_staff_allowed_subset(string from, string to)
    {
        Assert.True(OrderStateMachine.CanTransitionManually(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Refunded)]
    public void Terminal_states_have_no_outgoing_transitions(string status)
    {
        Assert.True(OrderStateMachine.IsTerminal(status));
        Assert.Empty(OrderStateMachine.GetValidNextStates(status));
    }

    [Theory]
    [InlineData(OrderStatus.New)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.AwaitingPayment)]
    [InlineData(OrderStatus.PaymentFailed)]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public void Non_terminal_states_have_at_least_one_outgoing_transition(string status)
    {
        Assert.False(OrderStateMachine.IsTerminal(status));
        Assert.NotEmpty(OrderStateMachine.GetValidNextStates(status));
    }

    [Fact]
    public void Cancelled_and_refunded_orders_require_inventory_release()
    {
        Assert.True(OrderStateMachine.RequiresInventoryRelease(OrderStatus.Cancelled));
        Assert.True(OrderStateMachine.RequiresInventoryRelease(OrderStatus.Refunded));
        Assert.False(OrderStateMachine.RequiresInventoryRelease(OrderStatus.Paid));
        Assert.False(OrderStateMachine.RequiresInventoryRelease(OrderStatus.Delivered));
    }

    [Fact]
    public void Only_delivered_commits_inventory()
    {
        Assert.True(OrderStateMachine.RequiresInventoryCommit(OrderStatus.Delivered));
        Assert.False(OrderStateMachine.RequiresInventoryCommit(OrderStatus.Shipped));
        Assert.False(OrderStateMachine.RequiresInventoryCommit(OrderStatus.Paid));
    }

    [Fact]
    public void Unknown_status_values_are_never_valid_transitions()
    {
        Assert.False(OrderStateMachine.CanTransition("Bogus", OrderStatus.Confirmed));
        Assert.False(OrderStateMachine.CanTransition(OrderStatus.New, "Bogus"));
        Assert.False(OrderStateMachine.CanTransitionManually("Bogus", OrderStatus.Confirmed));
    }
}
