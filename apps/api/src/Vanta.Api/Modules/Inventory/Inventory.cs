using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Orders;

namespace Vanta.Api.Modules.Inventory;

public static class InventoryReservationStatus
{
    public const string Reserved = "Reserved";
    public const string Released = "Released";
    public const string Committed = "Committed";
}

public static class InventoryMovementType
{
    public const string Reservation = "Reservation";
    public const string Release = "Release";
    public const string Commit = "Commit";
}

public sealed class StockReservation
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid OrderId { get; init; }
    public Guid OrderItemId { get; init; }
    public Guid ProductVariantId { get; init; }
    public int Quantity { get; init; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
}

public sealed class InventoryMovement
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid ProductVariantId { get; init; }
    public Guid? OrderId { get; init; }
    public Guid? StockReservationId { get; init; }
    public required string Type { get; init; }
    public int Quantity { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(Guid productVariantId)
        : base($"Insufficient stock for variant {productVariantId}.") { }
}

public sealed class InventoryService(VantaDbContext db)
{
    public async Task ReserveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        foreach (var item in order.Items.Where(item => item.VariantId.HasValue))
        {
            var variantId = item.VariantId!.Value;
            var updated = await ReserveVariantAsync(order.TenantId, variantId, item.Quantity, cancellationToken);
            if (updated != 1) throw new InsufficientStockException(variantId);

            var reservation = new StockReservation
            {
                Id = Guid.NewGuid(), TenantId = order.TenantId, OrderId = order.Id, OrderItemId = item.Id,
                ProductVariantId = variantId, Quantity = item.Quantity, Status = InventoryReservationStatus.Reserved,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.StockReservations.Add(reservation);
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(), TenantId = order.TenantId, ProductVariantId = variantId, OrderId = order.Id,
                StockReservationId = reservation.Id, Type = InventoryMovementType.Reservation, Quantity = item.Quantity,
                CreatedAt = reservation.CreatedAt,
            });
        }
    }

    public async Task ReleaseOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var reservations = await db.StockReservations
            .Where(reservation => reservation.TenantId == order.TenantId && reservation.OrderId == order.Id && reservation.Status == InventoryReservationStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var updated = await ReleaseVariantAsync(order.TenantId, reservation.ProductVariantId, reservation.Quantity, cancellationToken);
            if (updated != 1) throw new InvalidOperationException("Inventory reservation is inconsistent.");
            reservation.Status = InventoryReservationStatus.Released;
            reservation.ReleasedAt = DateTimeOffset.UtcNow;
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(), TenantId = order.TenantId, ProductVariantId = reservation.ProductVariantId,
                OrderId = order.Id, StockReservationId = reservation.Id, Type = InventoryMovementType.Release,
                Quantity = reservation.Quantity, CreatedAt = reservation.ReleasedAt.Value,
            });
        }
    }

    public async Task CommitOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var reservations = await db.StockReservations
            .Where(reservation => reservation.TenantId == order.TenantId && reservation.OrderId == order.Id && reservation.Status == InventoryReservationStatus.Reserved)
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var updated = await CommitVariantAsync(order.TenantId, reservation.ProductVariantId, reservation.Quantity, cancellationToken);
            if (updated != 1) throw new InvalidOperationException("Inventory reservation is inconsistent.");
            reservation.Status = InventoryReservationStatus.Committed;
            reservation.CommittedAt = DateTimeOffset.UtcNow;
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(), TenantId = order.TenantId, ProductVariantId = reservation.ProductVariantId,
                OrderId = order.Id, StockReservationId = reservation.Id, Type = InventoryMovementType.Commit,
                Quantity = reservation.Quantity, CreatedAt = reservation.CommittedAt.Value,
            });
        }
    }

    private async Task<int> ReserveVariantAsync(Guid tenantId, Guid variantId, int quantity, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            return await db.ProductVariants
                .Where(variant => variant.Id == variantId && variant.TenantId == tenantId && variant.IsActive)
                .Where(variant => variant.StockQuantity - variant.ReservedStock >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(variant => variant.ReservedStock, variant => variant.ReservedStock + quantity)
                    .SetProperty(variant => variant.UpdatedAt, _ => DateTimeOffset.UtcNow), cancellationToken);
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(current => current.Id == variantId && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (variant is null || variant.StockQuantity - variant.ReservedStock < quantity) return 0;
        variant.ReservedStock += quantity;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        return 1;
    }

    private async Task<int> ReleaseVariantAsync(Guid tenantId, Guid variantId, int quantity, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            return await db.ProductVariants
                .Where(variant => variant.Id == variantId && variant.TenantId == tenantId && variant.ReservedStock >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(variant => variant.ReservedStock, variant => variant.ReservedStock - quantity)
                    .SetProperty(variant => variant.UpdatedAt, _ => DateTimeOffset.UtcNow), cancellationToken);
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(current => current.Id == variantId && current.TenantId == tenantId, cancellationToken);
        if (variant is null || variant.ReservedStock < quantity) return 0;
        variant.ReservedStock -= quantity;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        return 1;
    }

    private async Task<int> CommitVariantAsync(Guid tenantId, Guid variantId, int quantity, CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            return await db.ProductVariants
                .Where(variant => variant.Id == variantId && variant.TenantId == tenantId)
                .Where(variant => variant.StockQuantity >= quantity && variant.ReservedStock >= quantity)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(variant => variant.StockQuantity, variant => variant.StockQuantity - quantity)
                    .SetProperty(variant => variant.ReservedStock, variant => variant.ReservedStock - quantity)
                    .SetProperty(variant => variant.UpdatedAt, _ => DateTimeOffset.UtcNow), cancellationToken);
        }

        var variant = await db.ProductVariants.FirstOrDefaultAsync(current => current.Id == variantId && current.TenantId == tenantId, cancellationToken);
        if (variant is null || variant.StockQuantity < quantity || variant.ReservedStock < quantity) return 0;
        variant.StockQuantity -= quantity;
        variant.ReservedStock -= quantity;
        variant.UpdatedAt = DateTimeOffset.UtcNow;
        return 1;
    }
}
