using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Commerce;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Tenants;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.Modules.Payments;

public sealed record PaymentResponse(Guid Id, Guid OrderId, string Provider, string ProviderReference, string PublicReference, string Status, decimal Amount, DateTimeOffset CreatedAt);
public sealed record ManualTransferPaymentResponse(PaymentResponse Payment, string? BankName, string? BeneficiaryName, string? AccountNumber, string? DocumentNumber, string? Alias);

public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/orders/{orderId:guid}/payments", CreateAsync);
        group.MapPost("/orders/{orderId:guid}/payments/manual-transfer", CreateManualTransferAsync);
        group.MapPost("/payments/{paymentId:guid}/manual-confirm", ConfirmManualTransferAsync);
        group.MapPost("/payments/{paymentId:guid}/mock-confirm", ConfirmMockAsync);
        group.MapPost("/payments/{paymentId:guid}/verify", VerifyAsync);
        group.MapPost("/payments/{paymentId:guid}/refund", RefundAsync);
        group.MapPost("/webhooks/payments/{provider}", WebhookAsync).AllowAnonymous().RequireRateLimiting("webhook");
        return group;
    }

    private static async Task<IResult> WebhookAsync(
        string provider,
        HttpRequest httpRequest,
        VantaDbContext db,
        IEnumerable<IPaymentProvider> providers,
        PaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var matchedProvider = providers.FirstOrDefault(current => string.Equals(current.Name, provider, StringComparison.OrdinalIgnoreCase));
        if (matchedProvider is null) return Results.NotFound();

        httpRequest.EnableBuffering();
        using var reader = new StreamReader(httpRequest.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        httpRequest.Body.Position = 0;

        var headers = httpRequest.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var validation = matchedProvider.ValidateWebhook(new PaymentWebhookSignal(rawBody, headers));
        if (!validation.IsValid) return Results.Unauthorized();

        var payment = await db.Payments.FirstOrDefaultAsync(
            current => current.Provider == matchedProvider.Name && current.ProviderReference == validation.ProviderReference,
            cancellationToken);
        if (payment is null) return Results.NotFound();

        var duplicate = await db.PaymentWebhookEvents.AnyAsync(
            current => current.TenantId == payment.TenantId && current.Provider == matchedProvider.Name && current.EventId == validation.EventId,
            cancellationToken);
        if (duplicate) return Results.Ok(new { processed = true, duplicate = true });

        if (validation.Status != PaymentStatus.Paid && validation.Status != PaymentStatus.Failed)
            return Results.BadRequest(new { error = "Estado de webhook no soportado en el mock." });

        var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == payment.OrderId && current.TenantId == payment.TenantId, cancellationToken);
        if (order is null) return Results.NotFound();

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            if (validation.Status == PaymentStatus.Paid)
            {
                if (payment.Status == PaymentStatus.Expired)
                    await TryMarkPaidAfterExpirationAsync(paymentService, payment, PaymentTransitionSource.Webhook, cancellationToken);
                else
                    await TryMarkPaidAsync(db, paymentService, payment, order, PaymentTransitionSource.Webhook, cancellationToken);
            }
            else
                await TryMarkFailedAsync(db, paymentService, payment, order, PaymentTransitionSource.Webhook, cancellationToken);

            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent
            {
                Id = Guid.NewGuid(), TenantId = payment.TenantId, Provider = matchedProvider.Name,
                EventId = validation.EventId!, ReceivedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { processed = true, duplicate = false });
        }
        catch (DbUpdateException) when (transaction is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            var alreadyProcessed = await db.PaymentWebhookEvents.AsNoTracking().AnyAsync(
                current => current.TenantId == payment.TenantId && current.Provider == matchedProvider.Name && current.EventId == validation.EventId,
                cancellationToken);
            if (alreadyProcessed) return Results.Ok(new { processed = true, duplicate = true });
            throw;
        }
    }

    private static async Task<IResult> CreateAsync(
        Guid orderId,
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        VantaDbContext db,
        IPaymentProvider provider,
        Microsoft.Extensions.Options.IOptions<OrderReservationOptions> reservationOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var key = httpRequest.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 8 or > 200) return Results.BadRequest(new { error = "Idempotency-Key es obligatorio." });

        var existing = await db.Payments.FirstOrDefaultAsync(payment => payment.TenantId == tenantId && payment.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return Results.Ok(ToResponse(existing));

        var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == orderId && current.TenantId == tenantId, cancellationToken);
        if (order is null) return Results.NotFound();
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.AwaitingPayment))
        {
            return Results.Conflict(new { error = "El pedido debe estar confirmado (o con un pago rechazado) antes de solicitar el pago." });
        }

        var intent = await provider.CreatePaymentAsync(order.Id, order.Total, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var previousStatus = order.Status;
        order.Status = OrderStatus.AwaitingPayment;
        order.ReservationExpiresAt = OrderReservationPolicy.ExpiresAt(now, reservationOptions.Value);
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
            FromStatus = previousStatus, ToStatus = OrderStatus.AwaitingPayment, CreatedAt = now,
        });
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
            Provider = provider.Name, ProviderReference = intent.ProviderReference,
            PublicReference = PaymentReferenceGenerator.Generate(),
            IdempotencyKey = key, Status = intent.Status, Amount = order.Total,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Payments.Add(payment);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PaymentId = payment.Id,
            FromStatus = "", ToStatus = payment.Status, Source = PaymentTransitionSource.Creation, CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/payments/{payment.Id}", ToResponse(payment));
    }

    private static async Task<IResult> CreateManualTransferAsync(
        Guid orderId,
        HttpRequest httpRequest,
        ClaimsPrincipal user,
        VantaDbContext db,
        Microsoft.Extensions.Options.IOptions<OrderReservationOptions> reservationOptions,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var key = httpRequest.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 8 or > 200) return Results.BadRequest(new { error = "Idempotency-Key es obligatorio." });

        var existing = await db.Payments.FirstOrDefaultAsync(payment => payment.TenantId == tenantId && payment.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.Provider != "manual-transfer") return Results.Ok(ToResponse(existing));
            var existingTenant = await db.Tenants.AsNoTracking().FirstAsync(current => current.Id == tenantId, cancellationToken);
            return Results.Ok(new ManualTransferPaymentResponse(ToResponse(existing), existingTenant.BankName, existingTenant.BankBeneficiaryName, existingTenant.BankAccountNumber, existingTenant.BankDocumentNumber, existingTenant.BankAlias));
        }
        var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == orderId && current.TenantId == tenantId, cancellationToken);
        if (order is null) return Results.NotFound();
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.AwaitingPayment)) return Results.Conflict(new { error = "El pedido no está listo para solicitar el pago." });

        var tenant = await db.Tenants.AsNoTracking().FirstAsync(current => current.Id == tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(tenant.BankName) || string.IsNullOrWhiteSpace(tenant.BankBeneficiaryName) || string.IsNullOrWhiteSpace(tenant.BankAccountNumber) || string.IsNullOrWhiteSpace(tenant.BankDocumentNumber))
            return Results.Conflict(new { error = "Configurá los datos bancarios antes de solicitar una transferencia." });

        var now = DateTimeOffset.UtcNow;
        var previousStatus = order.Status;
        order.Status = OrderStatus.AwaitingPayment;
        order.ReservationExpiresAt = OrderReservationPolicy.ExpiresAt(now, reservationOptions.Value);
        db.OrderStatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = previousStatus, ToStatus = OrderStatus.AwaitingPayment, CreatedAt = now });
        var payment = new Payment { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, Provider = "manual-transfer", ProviderReference = $"manual-{order.Id:N}", PublicReference = PaymentReferenceGenerator.Generate(), IdempotencyKey = key, Status = PaymentStatus.Pending, Amount = order.Total, CreatedAt = now, UpdatedAt = now };
        db.Payments.Add(payment);
        db.PaymentStatusHistory.Add(new PaymentStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, PaymentId = payment.Id, FromStatus = "", ToStatus = payment.Status, Source = PaymentTransitionSource.Creation, CreatedAt = now });
        await AddTransferInstructionsNotificationAsync(db, order, tenant, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/payments/{payment.Id}", new ManualTransferPaymentResponse(ToResponse(payment), tenant.BankName, tenant.BankBeneficiaryName, tenant.BankAccountNumber, tenant.BankDocumentNumber, tenant.BankAlias));
    }

    private static async Task<IResult> ConfirmManualTransferAsync(Guid paymentId, ClaimsPrincipal user, VantaDbContext db, PaymentService paymentService, CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var payment = await db.Payments.FirstOrDefaultAsync(current => current.Id == paymentId && current.TenantId == tenantId && current.Provider == "manual-transfer", cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Status == PaymentStatus.Paid) return Results.Ok(ToResponse(payment));
        if (payment.Status != PaymentStatus.Pending) return Results.Conflict(new { error = "La transferencia no está pendiente." });
        var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == payment.OrderId && current.TenantId == tenantId, cancellationToken);
        if (order is null) return Results.NotFound();
        var applied = await TryMarkPaidAsync(db, paymentService, payment, order, PaymentTransitionSource.ManualConfirmation, cancellationToken);
        if (!applied) return Results.Conflict(new { error = "El pago ya fue procesado." });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(payment));
    }

    private static async Task<IResult> ConfirmMockAsync(
        Guid paymentId,
        ClaimsPrincipal user,
        VantaDbContext db,
        PaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var payment = await db.Payments.FirstOrDefaultAsync(current => current.Id == paymentId && current.TenantId == tenantId, cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Provider != "mock") return Results.Conflict(new { error = "Solo se puede confirmar el proveedor mock en desarrollo." });
        if (payment.Status == PaymentStatus.Paid) return Results.Ok(ToResponse(payment));
        if (payment.Status != PaymentStatus.Pending) return Results.Conflict(new { error = "El pago no está pendiente." });

        var order = await db.Orders.FirstOrDefaultAsync(
            current => current.Id == payment.OrderId && current.TenantId == tenantId,
            cancellationToken);
        if (order is null) return Results.NotFound();
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.Paid))
        {
            return Results.Conflict(new { error = "La orden no puede pasar a Paid desde su estado actual." });
        }

        var applied = await TryMarkPaidAsync(db, paymentService, payment, order, PaymentTransitionSource.MockConfirm, cancellationToken);
        if (!applied) return Results.Conflict(new { error = "El pago no está pendiente." });

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(payment));
    }

    // Server-to-server verification: actively asks the provider for the current status instead of
    // trusting a browser redirect. Useful when a webhook was never received.
    private static async Task<IResult> VerifyAsync(
        Guid paymentId,
        ClaimsPrincipal user,
        VantaDbContext db,
        IEnumerable<IPaymentProvider> providers,
        PaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var payment = await db.Payments.FirstOrDefaultAsync(current => current.Id == paymentId && current.TenantId == tenantId, cancellationToken);
        if (payment is null) return Results.NotFound();

        var provider = providers.FirstOrDefault(current => string.Equals(current.Name, payment.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null) return Results.Conflict(new { error = "El proveedor de este pago no está disponible." });

        if (payment.Status is PaymentStatus.Pending or PaymentStatus.Expired)
        {
            var status = await provider.GetPaymentStatusAsync(payment.ProviderReference, cancellationToken);
            var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == payment.OrderId && current.TenantId == tenantId, cancellationToken);
            if (order is not null)
            {
                if (status.Status == PaymentStatus.Paid)
                {
                    if (payment.Status == PaymentStatus.Expired)
                        await TryMarkPaidAfterExpirationAsync(paymentService, payment, PaymentTransitionSource.ServerVerification, cancellationToken);
                    else
                        await TryMarkPaidAsync(db, paymentService, payment, order, PaymentTransitionSource.ServerVerification, cancellationToken);
                }
                else if (status.Status == PaymentStatus.Failed)
                    await TryMarkFailedAsync(db, paymentService, payment, order, PaymentTransitionSource.ServerVerification, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(payment));
    }

    private static async Task<IResult> RefundAsync(
        Guid paymentId,
        ClaimsPrincipal user,
        VantaDbContext db,
        IEnumerable<IPaymentProvider> providers,
        InventoryService inventory,
        PaymentService paymentService,
        CancellationToken cancellationToken)
    {
        if (!TryGetTenantId(user, out var tenantId)) return Results.Unauthorized();
        var payment = await db.Payments.FirstOrDefaultAsync(current => current.Id == paymentId && current.TenantId == tenantId, cancellationToken);
        if (payment is null) return Results.NotFound();

        var order = await db.Orders.FirstOrDefaultAsync(current => current.Id == payment.OrderId && current.TenantId == tenantId, cancellationToken);
        if (order is null) return Results.NotFound();
        if (payment.Provider == "manual-transfer")
            return Results.Conflict(new { error = "Los reembolsos de transferencias deben gestionarse y auditarse manualmente fuera de VANTA." });
        var latePaymentRefund = payment.Status == PaymentStatus.PaidAfterExpiration && order.Status == OrderStatus.Cancelled;
        if (!latePaymentRefund && !OrderStateMachine.CanTransitionManually(order.Status, OrderStatus.Refunded))
            return Results.Conflict(new { error = "La orden no puede reembolsarse desde su estado actual." });
        if (!PaymentStateMachine.CanTransition(payment.Status, PaymentStatus.Refunded))
            return Results.Conflict(new { error = "El pago no puede reembolsarse desde su estado actual." });

        var provider = providers.FirstOrDefault(current => string.Equals(current.Name, payment.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null) return Results.Conflict(new { error = "El proveedor de este pago no está disponible." });

        await provider.RefundPaymentAsync(payment.ProviderReference, payment.Amount, cancellationToken);

        var applied = await paymentService.TryTransitionAsync(payment, PaymentStatus.Refunded, PaymentTransitionSource.Refund, cancellationToken);
        if (!applied) return Results.Conflict(new { error = "El pago ya fue procesado por otra solicitud." });

        if (!latePaymentRefund)
        {
            var previousStatus = order.Status;
            order.Status = OrderStatus.Refunded;
            db.OrderStatusHistory.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id,
                FromStatus = previousStatus, ToStatus = OrderStatus.Refunded, CreatedAt = payment.UpdatedAt,
            });
            if (OrderStateMachine.RequiresInventoryRelease(OrderStatus.Refunded)) await inventory.ReleaseOrderAsync(order, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(payment));
    }

    // Shared by webhook/verify/mock-confirm so the order-side effects (state machine + inventory
    // release + customer notification) of a successful payment are only ever applied once.
    private static async Task<bool> TryMarkPaidAsync(VantaDbContext db, PaymentService paymentService, Payment payment, Order order, string source, CancellationToken cancellationToken)
    {
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.Paid)) return false;
        var applied = await paymentService.TryTransitionAsync(payment, PaymentStatus.Paid, source, cancellationToken);
        if (!applied) return false;

        var previousStatus = order.Status;
        order.Status = OrderStatus.Paid;
        db.OrderStatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = payment.TenantId, OrderId = order.Id, FromStatus = previousStatus, ToStatus = OrderStatus.Paid, CreatedAt = payment.UpdatedAt });
        await AddPaymentNotificationAsync(db, order, payment.TenantId, cancellationToken);
        return true;
    }

    private static async Task<bool> TryMarkFailedAsync(VantaDbContext db, PaymentService paymentService, Payment payment, Order order, string source, CancellationToken cancellationToken)
    {
        if (!OrderStateMachine.CanTransition(order.Status, OrderStatus.PaymentFailed)) return false;
        var applied = await paymentService.TryTransitionAsync(payment, PaymentStatus.Failed, source, cancellationToken);
        if (!applied) return false;

        var previousStatus = order.Status;
        order.Status = OrderStatus.PaymentFailed;
        db.OrderStatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = payment.TenantId, OrderId = order.Id, FromStatus = previousStatus, ToStatus = OrderStatus.PaymentFailed, CreatedAt = payment.UpdatedAt });
        return true;
    }

    private static Task<bool> TryMarkPaidAfterExpirationAsync(
        PaymentService paymentService,
        Payment payment,
        string source,
        CancellationToken cancellationToken) =>
        paymentService.TryTransitionAsync(payment, PaymentStatus.PaidAfterExpiration, source, cancellationToken);

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId) => Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);

    // Sends the paid-order receipt over WhatsApp. Conversation-linked orders (AI/merchant flows)
    // already have a conversation; public-store orders don't, so one is found or created against
    // the tenant's active channel using the order customer's phone number.
    public static async Task AddTransferInstructionsNotificationAsync(VantaDbContext db, Order order, Tenant tenant, CancellationToken cancellationToken)
    {
        await AddOrderNotificationAsync(db, order, tenant.Id,
            $"Datos para transferir el pedido {order.Number}: Banco: {tenant.BankName}. Beneficiario: {tenant.BankBeneficiaryName}. Cuenta: {tenant.BankAccountNumber}. Documento: {tenant.BankDocumentNumber}. Alias: {tenant.BankAlias ?? "No informado"}. Total: {GuaraniAmount.Format(order.Total)} Gs. Luego de transferir, enviá el comprobante por este chat.", cancellationToken);
    }

    private static Task AddPaymentNotificationAsync(VantaDbContext db, Order order, Guid tenantId, CancellationToken cancellationToken) =>
        AddOrderNotificationAsync(db, order, tenantId, $"Recibo VANTA: recibimos el pago de tu pedido {order.Number}. Tu pedido está pagado y seguirá a preparación.", cancellationToken);

    private static async Task AddOrderNotificationAsync(VantaDbContext db, Order order, Guid tenantId, string text, CancellationToken cancellationToken)
    {
        var conversationId = order.ConversationId;
        if (!conversationId.HasValue)
        {
            if (!order.CustomerId.HasValue) return;
            var customer = await db.Customers.FirstOrDefaultAsync(current => current.Id == order.CustomerId.Value && current.TenantId == tenantId, cancellationToken);
            if (customer is null) return;
            var channel = await db.WhatsAppChannelAccounts.FirstOrDefaultAsync(current => current.TenantId == tenantId && current.IsActive, cancellationToken);
            if (channel is null) return;

            var conversation = await db.Conversations.FirstOrDefaultAsync(
                current => current.TenantId == tenantId && current.ChannelAccountId == channel.Id && current.CustomerExternalId == customer.Phone, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            if (conversation is null)
            {
                conversation = new Conversation
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ChannelAccountId = channel.Id, CustomerId = customer.Id,
                    CustomerExternalId = customer.Phone, Status = ConversationStatus.AiActive, LastMessageAt = now, LastActivityAt = now,
                };
                db.Conversations.Add(conversation);
            }
            else
            {
                conversation.CustomerId ??= customer.Id;
                conversation.LastMessageAt = now;
                conversation.LastActivityAt = now;
            }
            order.ConversationId = conversation.Id;
            conversationId = conversation.Id;
        }

        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId.Value,
            ExternalMessageId = $"payment-paid-{order.Id:N}-{Guid.NewGuid():N}",
            Direction = "Outbound",
            Text = text,
            DeliveryStatus = "Pending",
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static PaymentResponse ToResponse(Payment payment) => new(payment.Id, payment.OrderId, payment.Provider, payment.ProviderReference, payment.PublicReference, payment.Status, payment.Amount, payment.CreatedAt);
}
