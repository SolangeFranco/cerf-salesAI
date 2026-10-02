using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.Modules.Orders;

public sealed record CreateOrderItemRequest(
    [property: Required] Guid ProductId,
    Guid? VariantId,
    [property: Range(1, int.MaxValue)] int Quantity);

public sealed record CreateOrderRequest(
    [property: Required, MinLength(1)] IReadOnlyList<CreateOrderItemRequest> Items,
    Guid? CustomerId = null,
    Guid? ConversationId = null);

public sealed record UpdateOrderStatusRequest([property: Required] string Status);

public sealed record OrderItemResponse(
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    string Number,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    Guid? CustomerId,
    Guid? ConversationId,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record OrderCustomerResponse(string Name, string Phone, string? Email, string? Address);

public sealed record OrderDetailResponse(
    Guid Id,
    string Number,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    Guid? CustomerId,
    Guid? ConversationId,
    OrderCustomerResponse? Customer,
    PaymentResponse? Payment,
    IReadOnlyList<OrderStatusHistoryResponse> StatusHistory,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record OrderStatusHistoryResponse(string FromStatus, string ToStatus, DateTimeOffset CreatedAt);

public static class OrderEndpoints
{
    public static RouteGroupBuilder MapOrderEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("", ListAsync);
        group.MapGet("/{orderId:guid}", GetAsync);
        group.MapPost("", CreateAsync);
        group.MapPatch("/{orderId:guid}/status", UpdateStatusAsync);
        return group;
    }

    private static async Task<IResult> ListAsync(
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var orders = await db.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.TenantId == tenantId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        return Results.Ok(orders.Select(ToResponse));
    }

    private static async Task<IResult> GetAsync(
        Guid orderId,
        ClaimsPrincipal user,
        VantaDbContext db,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(current => current.Items)
            .Include(current => current.StatusHistory)
            .FirstOrDefaultAsync(current => current.Id == orderId && current.TenantId == tenantId, cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        var customer = order.CustomerId.HasValue
            ? await db.Customers.AsNoTracking()
                .Where(current => current.Id == order.CustomerId.Value && current.TenantId == tenantId)
                .Select(current => new OrderCustomerResponse(current.Name, current.Phone, current.Email, current.Address))
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var payment = await db.Payments.AsNoTracking()
            .Where(current => current.OrderId == order.Id && current.TenantId == tenantId)
            .OrderByDescending(current => current.CreatedAt)
            .Select(current => new PaymentResponse(current.Id, current.OrderId, current.Provider, current.ProviderReference, current.PublicReference, current.Status, current.Amount, current.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return Results.Ok(new OrderDetailResponse(
            order.Id,
            order.Number,
            order.Status,
            order.Total,
            order.CreatedAt,
            order.CustomerId,
            order.ConversationId,
            customer,
            payment,
            order.StatusHistory
                .OrderBy(history => history.CreatedAt)
                .Select(history => new OrderStatusHistoryResponse(history.FromStatus, history.ToStatus, history.CreatedAt))
                .ToList(),
            ToResponse(order).Items));
    }

    private static async Task<IResult> CreateAsync(
        CreateOrderRequest request,
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        VantaDbContext db,
        InventoryService inventory,
        CommerceEventRecorder commerceEvents,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 200)
        {
            return Results.BadRequest(new { error = "Idempotency-Key es obligatorio y debe tener entre 8 y 200 caracteres." });
        }

        var existingOrder = await db.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.TenantId == tenantId && order.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingOrder is not null)
        {
            return Results.Ok(ToResponse(existingOrder));
        }

        if (request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "La orden debe contener al menos un producto." });
        }

        if (request.CustomerId.HasValue && !await db.Customers.AnyAsync(
                customer => customer.Id == request.CustomerId.Value && customer.TenantId == tenantId,
                cancellationToken))
        {
            return Results.NotFound(new { error = "El cliente no existe en este tenant." });
        }

        if (request.ConversationId.HasValue && !await db.Conversations.AnyAsync(
            conversation => conversation.Id == request.ConversationId.Value && conversation.TenantId == tenantId,
            cancellationToken))
        {
            return Results.NotFound(new { error = "La conversación no existe en este tenant." });
        }

        var productIds = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await db.Products
            .Where(product => product.TenantId == tenantId && product.IsActive && productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        if (products.Count != productIds.Length)
        {
            return Results.NotFound(new { error = "Uno o más productos no existen o no están activos." });
        }

        var variantIds = request.Items.Where(item => item.VariantId.HasValue).Select(item => item.VariantId!.Value).Distinct().ToArray();
        var variants = await db.ProductVariants
            .Where(variant => variant.TenantId == tenantId && variant.IsActive && variantIds.Contains(variant.Id))
            .ToDictionaryAsync(variant => variant.Id, cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var requestedItem in request.Items)
        {
            var product = products[requestedItem.ProductId];
            ProductVariant? variant = null;
            if (requestedItem.VariantId.HasValue)
            {
                if (!variants.TryGetValue(requestedItem.VariantId.Value, out variant) || variant.ProductId != product.Id)
                {
                    return Results.NotFound(new { error = "Una variante no pertenece al producto indicado o no está activa." });
                }

                if (variant.StockQuantity - variant.ReservedStock < requestedItem.Quantity)
                {
                    return Results.Conflict(new { error = $"Stock insuficiente para la variante {variant.Sku}." });
                }
            }

            var unitPrice = ProductPricing.EffectivePrice(product, variant?.PriceOverride);
            orderItems.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                VariantId = variant?.Id,
                ProductName = product.Name,
                Sku = variant?.Sku ?? product.Sku,
                Quantity = requestedItem.Quantity,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * requestedItem.Quantity,
            });
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = request.CustomerId,
            ConversationId = request.ConversationId,
            Number = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            IdempotencyKey = idempotencyKey,
            Status = OrderStatus.New,
            Total = orderItems.Sum(item => item.LineTotal),
            CreatedAt = DateTimeOffset.UtcNow,
            Items = orderItems,
        };

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            FromStatus = "",
            ToStatus = OrderStatus.New,
            CreatedAt = order.CreatedAt,
        });

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            db.Orders.Add(order);
            commerceEvents.Record(tenantId, CommerceEventType.OrderCreated, conversationId: order.ConversationId, orderId: order.Id,
                metadata: new Dictionary<string, string> { ["source"] = AttributionSources.MerchantApi });
            await db.SaveChangesAsync(cancellationToken);
            await inventory.ReserveOrderAsync(order, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (InsufficientStockException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "Stock insuficiente para completar la reserva." });
        }

        return Results.Created($"/api/orders/{order.Id}", ToResponse(order));
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid orderId,
        UpdateOrderStatusRequest request,
        ClaimsPrincipal user,
        VantaDbContext db,
        InventoryService inventory,
        CommerceEventRecorder commerceEvents,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId))
        {
            return Results.Unauthorized();
        }

        var order = await db.Orders.FirstOrDefaultAsync(
            current => current.Id == orderId && current.TenantId == tenantId,
            cancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        var nextStatus = request.Status.Trim();
        if (!OrderStateMachine.CanTransitionManually(order.Status, nextStatus))
        {
            return Results.Conflict(new
            {
                error = $"No se puede cambiar una orden de {order.Status} a {nextStatus}.",
            });
        }

        var previousStatus = order.Status;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            order.Status = nextStatus;
            db.OrderStatusHistory.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
                FromStatus = previousStatus, ToStatus = nextStatus, CreatedAt = DateTimeOffset.UtcNow,
            });
            if (OrderStateMachine.RequiresInventoryRelease(nextStatus)) await inventory.ReleaseOrderAsync(order, cancellationToken);
            if (OrderStateMachine.RequiresInventoryCommit(nextStatus)) await inventory.CommitOrderAsync(order, cancellationToken);
            if (nextStatus == OrderStatus.Cancelled)
            {
                commerceEvents.Record(tenantId, CommerceEventType.OrderCancelled, conversationId: order.ConversationId, orderId: order.Id,
                    metadata: new Dictionary<string, string> { ["fromStatus"] = previousStatus });
            }
            else if (nextStatus == OrderStatus.Delivered)
            {
                commerceEvents.Record(tenantId, CommerceEventType.OrderDelivered, conversationId: order.ConversationId, orderId: order.Id);
            }
            if (order.ConversationId.HasValue)
            {
                var now = DateTimeOffset.UtcNow;
                db.ConversationMessages.Add(new ConversationMessage
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = order.ConversationId.Value,
                    ExternalMessageId = $"order-status-{order.Id:N}-{nextStatus}-{Guid.NewGuid():N}", Direction = "Outbound",
                    Text = $"Actualización de tu pedido {order.Number}: ahora está {StatusLabel(nextStatus)}.",
                    DeliveryStatus = "Pending", CreatedAt = now,
                });
            }
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "El estado del inventario no permite completar la transición." });
        }

        return Results.Ok(ToResponse(order));
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        return Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);
    }

    private static string StatusLabel(string status) => status switch
    {
        OrderStatus.New => "nuevo",
        OrderStatus.Confirmed => "confirmado",
        OrderStatus.AwaitingPayment => "esperando pago",
        OrderStatus.Paid => "pagado",
        OrderStatus.Preparing => "en preparación",
        OrderStatus.Shipped => "enviado",
        OrderStatus.Delivered => "entregado",
        OrderStatus.Cancelled => "cancelado",
        OrderStatus.Refunded => "reembolsado",
        OrderStatus.PaymentFailed => "con pago rechazado",
        _ => status,
    };

    private static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.Number,
        order.Status,
        order.Total,
        order.CreatedAt,
        order.CustomerId,
        order.ConversationId,
        order.Items.Select(item => new OrderItemResponse(
            item.ProductId,
            item.VariantId,
            item.ProductName,
            item.Sku,
            item.Quantity,
            item.UnitPrice,
            item.LineTotal)).ToList());
}
