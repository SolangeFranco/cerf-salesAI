using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Customers;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Inventory;

namespace Vanta.Api.Modules.Commerce;

public sealed record PublicVariantResponse(Guid Id, string Name, string Sku, int StockQuantity, decimal? PriceOverride);
public sealed record PublicProductResponse(Guid Id, string Name, string? Description, string Sku, decimal Price, decimal DiscountPercent, decimal DiscountedPrice, string? ImageBase64, string? ImageMediaType, IReadOnlyList<PublicVariantResponse> Variants);
public sealed record PublicStoreResponse(string StoreName, string Slug, string? LogoUrl, IReadOnlyList<PublicProductResponse> Products);
public sealed record PublicCheckoutItemResponse(string ProductName, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record PublicCheckoutResponse(string StoreName, string OrderNumber, string OrderStatus, string PaymentStatus, decimal Total, IReadOnlyList<PublicCheckoutItemResponse> Items, string PaymentProvider, string? BankName, string? BeneficiaryName, string? AccountNumber, string? DocumentNumber, string? Alias);
public sealed record PublicOrderItemRequest(Guid ProductId, Guid? VariantId, [property: Range(1, 100)] int Quantity);
public sealed record PublicOrderRequest(
    [property: Required, StringLength(200, MinimumLength = 2)] string Name,
    [property: Required, StringLength(40, MinimumLength = 6)] string Phone,
    [property: EmailAddress, StringLength(200)] string? Email,
    [property: Required, StringLength(500, MinimumLength = 5)] string Address,
    [property: Required, MinLength(1), MaxLength(50)] IReadOnlyList<PublicOrderItemRequest> Items);
public sealed record PublicOrderCreatedResponse(string OrderNumber, string PaymentReference, string CheckoutUrl, decimal Total);

public static class PublicCommerceEndpoints
{
    public static IEndpointRouteBuilder MapPublicCommerceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/store/{slug}", GetStoreAsync).AllowAnonymous().RequireRateLimiting("public");
        endpoints.MapPost("/api/public/store/{slug}/orders", CreateOrderAsync).AllowAnonymous().RequireRateLimiting("public");
        endpoints.MapGet("/api/public/checkout/{paymentReference}", GetCheckoutAsync).AllowAnonymous().RequireRateLimiting("public");
        return endpoints;
    }

    private static async Task<IResult> CreateOrderAsync(
        string slug,
        PublicOrderRequest request,
        VantaDbContext db,
        IPaymentProvider paymentProvider,
        InventoryService inventory,
        CommerceEventRecorder commerceEvents,
        Microsoft.Extensions.Options.IOptions<CommerceOptions> commerceOptions,
        Microsoft.Extensions.Options.IOptions<OrderReservationOptions> reservationOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Address) || request.Items.Count == 0)
            return Results.BadRequest(new { error = "Nombre, teléfono, dirección y al menos un producto son obligatorios." });

        var tenant = await db.Tenants.FirstOrDefaultAsync(current => current.Slug == slug, cancellationToken);
        if (tenant is null) return Results.NotFound();
        if (paymentProvider.Name == "manual-transfer" &&
            (string.IsNullOrWhiteSpace(tenant.BankName) || string.IsNullOrWhiteSpace(tenant.BankBeneficiaryName) ||
             string.IsNullOrWhiteSpace(tenant.BankAccountNumber) || string.IsNullOrWhiteSpace(tenant.BankDocumentNumber)))
            return Results.Conflict(new { error = "La tienda todavía no configuró los datos necesarios para recibir transferencias." });

        // No client-generated idempotency key exists for the public storefront today, so a
        // duplicate submission (double click, retry) is detected by hashing the normalized
        // request content. This reuses the existing unique (TenantId, IdempotencyKey) index.
        var idempotencyKey = BuildIdempotencyKey(request);
        var existingOrder = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(current => current.TenantId == tenant.Id && current.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingOrder is not null)
        {
            var existingPayment = await db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(current => current.OrderId == existingOrder.Id, cancellationToken);
            if (existingPayment is not null)
            {
                return Results.Ok(new PublicOrderCreatedResponse(
                    existingOrder.Number, existingPayment.PublicReference,
                    CommerceUrlBuilder.CheckoutUrl(commerceOptions.Value, existingPayment.PublicReference), existingOrder.Total));
            }
        }

        var productIds = request.Items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await db.Products.Where(product => product.TenantId == tenant.Id && product.IsActive && productIds.Contains(product.Id)).ToDictionaryAsync(product => product.Id, cancellationToken);
        if (products.Count != productIds.Length) return Results.BadRequest(new { error = "Uno o más productos ya no están disponibles." });

        var variantIds = request.Items.Where(item => item.VariantId.HasValue).Select(item => item.VariantId!.Value).Distinct().ToArray();
        var variants = await db.ProductVariants.Where(variant => variant.TenantId == tenant.Id && variant.IsActive && variantIds.Contains(variant.Id)).ToDictionaryAsync(variant => variant.Id, cancellationToken);
        var orderItems = new List<OrderItem>();
        foreach (var requested in request.Items)
        {
            if (requested.Quantity is < 1 or > 100) return Results.BadRequest(new { error = "Cantidad inválida." });
            var product = products[requested.ProductId];
            ProductVariant? variant = null;
            if (requested.VariantId.HasValue && (!variants.TryGetValue(requested.VariantId.Value, out variant) || variant.ProductId != product.Id)) return Results.BadRequest(new { error = "Variante inválida." });
            if (variant is not null)
            {
                if (variant.StockQuantity - variant.ReservedStock < requested.Quantity) return Results.Conflict(new { error = $"Stock insuficiente para {variant.Name}." });
            }
            var unitPrice = ProductPricing.EffectivePrice(product, variant?.PriceOverride);
            orderItems.Add(new OrderItem { Id = Guid.NewGuid(), ProductId = product.Id, VariantId = variant?.Id, ProductName = product.Name, Sku = variant?.Sku ?? product.Sku, Quantity = requested.Quantity, UnitPrice = unitPrice, LineTotal = unitPrice * requested.Quantity });
        }

        var now = DateTimeOffset.UtcNow;
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = request.Name.Trim(), Phone = request.Phone.Trim(), Email = request.Email?.Trim(), Address = request.Address.Trim(), CreatedAt = now, UpdatedAt = now };
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant.Id, CustomerId = customer.Id, Number = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}", IdempotencyKey = idempotencyKey, Status = OrderStatus.AwaitingPayment, Total = orderItems.Sum(item => item.LineTotal), CreatedAt = now, ReservationExpiresAt = OrderReservationPolicy.ExpiresAt(now, reservationOptions.Value), Items = orderItems };
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, FromStatus = "", ToStatus = OrderStatus.New, CreatedAt = now });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, FromStatus = OrderStatus.New, ToStatus = OrderStatus.Confirmed, CreatedAt = now });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, FromStatus = OrderStatus.Confirmed, ToStatus = OrderStatus.AwaitingPayment, CreatedAt = now });
        var paymentIntent = await paymentProvider.CreatePaymentAsync(order.Id, order.Total, cancellationToken);
        var publicReference = PaymentReferenceGenerator.Generate();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            db.Customers.Add(customer);
            db.Orders.Add(order);
            commerceEvents.Record(tenant.Id, CommerceEventType.OrderCreated, orderId: order.Id,
                metadata: new Dictionary<string, string> { ["source"] = AttributionSources.PublicStore });
            db.Payments.Add(new Payment { Id = Guid.NewGuid(), TenantId = tenant.Id, OrderId = order.Id, Provider = paymentProvider.Name, ProviderReference = paymentIntent.ProviderReference, PublicReference = publicReference, IdempotencyKey = $"public-payment-{order.Id:N}", Status = paymentIntent.Status, Amount = order.Total, CreatedAt = now, UpdatedAt = now });
            if (paymentProvider.Name == "manual-transfer")
                await PaymentEndpoints.AddTransferInstructionsNotificationAsync(db, order, tenant, cancellationToken);
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
        catch (DbUpdateException) when (transaction is not null)
        {
            // Two concurrent identical submissions raced past the initial idempotency check;
            // the loser hits the unique (TenantId, IdempotencyKey) constraint here instead.
            await transaction.RollbackAsync(cancellationToken);
            var racedOrder = await db.Orders.AsNoTracking()
                .FirstOrDefaultAsync(current => current.TenantId == tenant.Id && current.IdempotencyKey == idempotencyKey, cancellationToken);
            var racedPayment = racedOrder is null ? null : await db.Payments.AsNoTracking()
                .FirstOrDefaultAsync(current => current.OrderId == racedOrder.Id, cancellationToken);
            if (racedOrder is null || racedPayment is null) throw;
            return Results.Ok(new PublicOrderCreatedResponse(
                racedOrder.Number, racedPayment.PublicReference,
                CommerceUrlBuilder.CheckoutUrl(commerceOptions.Value, racedPayment.PublicReference), racedOrder.Total));
        }
        return Results.Created($"/api/public/checkout/{publicReference}", new PublicOrderCreatedResponse(order.Number, publicReference, CommerceUrlBuilder.CheckoutUrl(commerceOptions.Value, publicReference), order.Total));
    }

    private static async Task<IResult> GetStoreAsync(string slug, VantaDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(current => current.Slug == slug, cancellationToken);
        if (tenant is null) return Results.NotFound();

        var products = await db.Products.AsNoTracking()
            .Where(product => product.TenantId == tenant.Id && product.IsActive)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        var productIds = products.Select(product => product.Id).ToArray();
        var variants = await db.ProductVariants.AsNoTracking()
            .Where(variant => variant.TenantId == tenant.Id && variant.IsActive && productIds.Contains(variant.ProductId))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PublicStoreResponse(
            tenant.Name,
            tenant.Slug,
            tenant.LogoUrl,
            products.Select(product => new PublicProductResponse(
                product.Id,
                product.Name,
                product.Description,
                product.Sku,
                product.Price,
                product.DiscountPercent,
                ProductPricing.EffectivePrice(product),
                product.ImageData is null ? null : Convert.ToBase64String(product.ImageData),
                product.ImageMediaType,
                variants.Where(variant => variant.ProductId == product.Id)
                    .Select(variant => new PublicVariantResponse(variant.Id, variant.Name, variant.Sku, variant.StockQuantity, variant.PriceOverride))
                    .ToList())).ToList()));
    }

    private static async Task<IResult> GetCheckoutAsync(string paymentReference, VantaDbContext db, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .FirstOrDefaultAsync(current => current.PublicReference == paymentReference, cancellationToken);
        if (payment is null) return Results.NotFound();

        var order = await db.Orders.AsNoTracking()
            .Include(current => current.Items)
            .FirstOrDefaultAsync(current => current.Id == payment.OrderId && current.TenantId == payment.TenantId, cancellationToken);
        if (order is null) return Results.NotFound();

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(current => current.Id == payment.TenantId, cancellationToken);

        return Results.Ok(new PublicCheckoutResponse(
            tenant?.Name ?? "VANTA Store",
            order.Number,
            order.Status,
            payment.Status,
            order.Total,
            order.Items.Select(item => new PublicCheckoutItemResponse(item.ProductName, item.Sku, item.Quantity, item.UnitPrice, item.LineTotal)).ToList(),
            payment.Provider,
            payment.Provider == "manual-transfer" ? tenant?.BankName : null,
            payment.Provider == "manual-transfer" ? tenant?.BankBeneficiaryName : null,
            payment.Provider == "manual-transfer" ? tenant?.BankAccountNumber : null,
            payment.Provider == "manual-transfer" ? tenant?.BankDocumentNumber : null,
            payment.Provider == "manual-transfer" ? tenant?.BankAlias : null));
    }

    private static string BuildIdempotencyKey(PublicOrderRequest request)
    {
        var normalizedItems = request.Items
            .Select(item => $"{item.ProductId}:{item.VariantId}:{item.Quantity}")
            .OrderBy(value => value, StringComparer.Ordinal);
        var seed = string.Join('|', request.Name.Trim(), request.Phone.Trim(), request.Address.Trim(), string.Join(',', normalizedItems));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return $"public-{Convert.ToHexString(hash)}";
    }
}