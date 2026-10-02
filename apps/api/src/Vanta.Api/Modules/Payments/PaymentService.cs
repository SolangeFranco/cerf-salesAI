using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Observability;

namespace Vanta.Api.Modules.Payments;

/// <summary>
/// Owns Payment status transitions: enforces <see cref="PaymentStateMachine"/> rules, applies
/// them atomically (protecting against a webhook, a server-to-server verification and a mock
/// confirmation racing for the same payment) and writes an audit trail entry per transition.
/// </summary>
public sealed class PaymentService(VantaDbContext db, CommerceEventRecorder commerceEvents)
{
    /// Returns false when the payment does not exist, or the transition is not currently valid
    /// (already applied by a concurrent caller, or not reachable from the payment's status).
    public async Task<bool> TryTransitionAsync(Payment payment, string toStatus, string source, CancellationToken cancellationToken)
    {
        using var activity = VantaTelemetry.ActivitySource.StartActivity("payment.transition");
        activity?.SetTag("payment.status.from", payment.Status);
        activity?.SetTag("payment.status.to", toStatus);
        activity?.SetTag("payment.source", source);
        var fromStatus = payment.Status;
        if (!PaymentStateMachine.CanTransition(fromStatus, toStatus))
        {
            VantaTelemetry.Count(VantaTelemetry.PaymentFailures, "invalid_transition");
            activity?.SetStatus(ActivityStatusCode.Error, "invalid_transition");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var updated = db.Database.IsRelational()
            ? await db.Payments
                .Where(current => current.Id == payment.Id && current.TenantId == payment.TenantId && current.Status == fromStatus)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.Status, toStatus)
                    .SetProperty(current => current.UpdatedAt, now), cancellationToken)
            : ApplyInMemory(payment, fromStatus, toStatus, now);

        if (updated != 1)
        {
            VantaTelemetry.Count(VantaTelemetry.PaymentFailures, "concurrent_transition");
            activity?.SetStatus(ActivityStatusCode.Error, "concurrent_transition");
            return false;
        }

        payment.Status = toStatus;
        payment.UpdatedAt = now;
        db.PaymentStatusHistory.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = payment.TenantId, PaymentId = payment.Id,
            FromStatus = fromStatus, ToStatus = toStatus, Source = source, CreatedAt = now,
        });
        if (toStatus == PaymentStatus.Paid)
        {
            commerceEvents.Record(payment.TenantId, CommerceEventType.PaymentConfirmed, orderId: payment.OrderId, paymentId: payment.Id,
                metadata: new Dictionary<string, string> { ["source"] = source });
        }
        return true;
    }

    private static int ApplyInMemory(Payment payment, string expectedFrom, string toStatus, DateTimeOffset now)
    {
        if (payment.Status != expectedFrom) return 0;
        payment.Status = toStatus;
        payment.UpdatedAt = now;
        return 1;
    }
}
