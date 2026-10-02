using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Jobs;
using Vanta.Api.Modules.Payments;

namespace Vanta.Api.Modules.Orders;

public sealed class OrderExpirationService(
    VantaDbContext db,
    PaymentService paymentService,
    InventoryService inventory)
{
    public async Task EnqueueExpiredAsync(BackgroundJobService jobs, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredOrders = await db.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.AwaitingPayment && order.ReservationExpiresAt <= now)
            .Select(order => new { order.Id, order.TenantId })
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var order in expiredOrders)
        {
            await jobs.EnqueueAsync(
                order.TenantId,
                BackgroundJobType.OrderExpiration,
                $"order-expiration-{order.Id:N}",
                new { orderId = order.Id },
                cancellationToken: cancellationToken);
        }
    }

    public async Task ExpireAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(job.Payload);
        var orderId = payload.RootElement.GetProperty("orderId").GetGuid();
        var now = DateTimeOffset.UtcNow;
        var order = await db.Orders.FirstOrDefaultAsync(
            current => current.Id == orderId && current.TenantId == job.TenantId,
            cancellationToken);
        if (order is null || order.ReservationExpiresAt > now ||
            !OrderStateMachine.CanTransition(order.Status, OrderStatus.Cancelled))
        {
            return;
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var payment = await db.Payments
            .Where(current => current.OrderId == order.Id && current.TenantId == order.TenantId)
            .OrderByDescending(current => current.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (payment?.Status is PaymentStatus.Paid or PaymentStatus.PaidAfterExpiration)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return;
        }
        if (payment?.Status == PaymentStatus.Pending &&
            !await paymentService.TryTransitionAsync(payment, PaymentStatus.Expired, PaymentTransitionSource.Expiration, cancellationToken))
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return;
        }

        var transitioned = db.Database.IsRelational()
            ? await db.Orders
                .Where(current => current.Id == order.Id && current.TenantId == order.TenantId)
                .Where(current => current.Status == order.Status && current.ReservationExpiresAt <= now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(current => current.Status, OrderStatus.Cancelled), cancellationToken)
            : ApplyInMemory(order, now);
        if (transitioned != 1)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return;
        }

        order.Status = OrderStatus.Cancelled;
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = order.TenantId, OrderId = order.Id,
            FromStatus = OrderStatus.AwaitingPayment, ToStatus = OrderStatus.Cancelled, CreatedAt = now,
        });
        await inventory.ReleaseOrderAsync(order, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private static int ApplyInMemory(Order order, DateTimeOffset now)
    {
        if (order.Status != OrderStatus.AwaitingPayment || order.ReservationExpiresAt > now) return 0;
        order.Status = OrderStatus.Cancelled;
        return 1;
    }
}