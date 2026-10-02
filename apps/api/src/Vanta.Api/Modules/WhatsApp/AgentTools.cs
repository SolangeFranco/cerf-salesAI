using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Catalog;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.Payments;
using Vanta.Api.Modules.Inventory;
using Vanta.Api.Modules.Knowledge;
using Microsoft.Extensions.Options;

namespace Vanta.Api.Modules.WhatsApp;

public sealed record AgentProductToolResult(Guid Id, string Name, string Sku, decimal Price, string? ImageBase64, string? ImageMediaType);
public sealed record AgentStockToolResult(Guid ProductId, int? StockQuantity, bool HasVariants);
public sealed record AgentOrderToolResult(Guid Id, string Number, decimal Total, string Status, string? PaymentReference = null);
public sealed record AgentOrderStatusToolResult(string Number, decimal Total, string Status, string? PaymentStatus, string? PaymentReference = null);
public sealed record AgentCartItemToolResult(string ProductName, string Sku, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record AgentCartToolResult(decimal Total, IReadOnlyList<AgentCartItemToolResult> Items);
public sealed record AgentCustomerToolResult(string Name, string Phone, string? Address);
// Content is sanitized untrusted data (see AgentContentSanitizer): information to cite, never an instruction.
public sealed record AgentKnowledgeResultToolResult(Guid DocumentId, string DocumentType, string DocumentTitle, string Content);

public interface IAgentTools
{
    Task<IReadOnlyList<AgentProductToolResult>> SearchProductsAsync(Guid tenantId, string query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentProductToolResult>> ListAvailableProductsAsync(Guid tenantId, string? categoryQuery, CancellationToken cancellationToken);
    Task<AgentProductToolResult?> GetProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken);
    Task<AgentStockToolResult?> CheckStockAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken);
    Task<AgentOrderToolResult?> CreateOrderAsync(Guid tenantId, Guid conversationId, Guid productId, int quantity, string requestText, string idempotencyKey, CancellationToken cancellationToken);
    Task<AgentCartToolResult> CreateCartAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken);
    Task<AgentCartToolResult?> AddToCartAsync(Guid tenantId, Guid conversationId, Guid productId, int quantity, string requestText, CancellationToken cancellationToken);
    Task<AgentCartToolResult?> GetCartAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken);
    Task<AgentOrderToolResult?> CheckoutCartAsync(Guid tenantId, Guid conversationId, string idempotencyKey, CancellationToken cancellationToken);
    Task<AgentCustomerToolResult?> GetCustomerAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken);
    Task<AgentCustomerToolResult?> UpdateCustomerAsync(Guid tenantId, Guid conversationId, string name, string address, CancellationToken cancellationToken);
    Task<bool> SubscribeStockAlertAsync(Guid tenantId, Guid conversationId, Guid productId, CancellationToken cancellationToken);
    Task<AgentOrderStatusToolResult?> GetLatestOrderStatusAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken);
    Task<AgentOrderStatusToolResult?> GetOrderAsync(Guid tenantId, Guid conversationId, Guid orderId, CancellationToken cancellationToken);
    Task<AgentOrderToolResult?> CreatePaymentAsync(Guid tenantId, Guid conversationId, Guid orderId, CancellationToken cancellationToken);
    Task<AgentShippingToolResult> CalculateShippingAsync(Guid tenantId, string address, CancellationToken cancellationToken);
    Task<bool> SendMessageAsync(Guid tenantId, Guid conversationId, string text, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentKnowledgeResultToolResult>> SearchKnowledgeAsync(Guid tenantId, string query, CancellationToken cancellationToken);
}

public sealed class AgentTools(VantaDbContext db, IPaymentProvider paymentProvider, InventoryService inventory, KnowledgeSearchService knowledgeSearch, CommerceEventRecorder commerceEvents, IOptions<OrderReservationOptions> reservationOptions) : IAgentTools
{
    public async Task<IReadOnlyList<AgentKnowledgeResultToolResult>> SearchKnowledgeAsync(Guid tenantId, string query, CancellationToken cancellationToken)
    {
        var results = await knowledgeSearch.SearchAsync(tenantId, query, cancellationToken);
        return results
            .Select(result => new AgentKnowledgeResultToolResult(
                result.DocumentId, result.DocumentType, result.DocumentTitle,
                AgentContentSanitizer.SanitizeKnowledgeContent(result.Content)))
            .ToList();
    }
    public async Task<AgentProductToolResult?> GetProductAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking()
            .Where(product => product.Id == productId && product.TenantId == tenantId && product.IsActive)
            .Select(product => new AgentProductToolResult(product.Id, product.Name, product.Sku, product.Price, product.ImageData == null ? null : Convert.ToBase64String(product.ImageData), product.ImageMediaType))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AgentCartToolResult> CreateCartAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var existing = await GetCartAsync(tenantId, conversationId, cancellationToken);
        if (existing is not null) return existing;
        var conversationExists = await db.Conversations.AnyAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (!conversationExists) return new AgentCartToolResult(0, []);
        var now = DateTimeOffset.UtcNow;
        var cart = new Cart { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, CreatedAt = now, UpdatedAt = now };
        db.Carts.Add(cart);
        commerceEvents.Record(tenantId, CommerceEventType.CartCreated, conversationId: conversationId, cartId: cart.Id);
        await db.SaveChangesAsync(cancellationToken);
        return new AgentCartToolResult(0, []);
    }

    public async Task<AgentShippingToolResult> CalculateShippingAsync(Guid tenantId, string address, CancellationToken cancellationToken)
    {
        var costs = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => new { tenant.MetropolitanDeliveryCostGs, tenant.InteriorDeliveryCostGs, tenant.DeliveryBaseCostGs, tenant.DeliveryIncludedDistanceKm, tenant.DeliveryExtraCostPerKmGs })
            .FirstOrDefaultAsync(cancellationToken);
        if (costs is null) throw new InvalidOperationException("Tenant not found for shipping calculation.");
        return ShippingCalculator.Calculate(address, costs.MetropolitanDeliveryCostGs, costs.InteriorDeliveryCostGs, costs.DeliveryIncludedDistanceKm, costs.DeliveryBaseCostGs, costs.DeliveryIncludedDistanceKm, costs.DeliveryExtraCostPerKmGs);
    }

    // Resource ownership: an order can only be paid/read through the conversation that created
    // it, never by tenant scope alone — this stops one conversation from acting on another
    // customer's order even within the same tenant.
    public async Task<AgentOrderToolResult?> CreatePaymentAsync(Guid tenantId, Guid conversationId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.FirstOrDefaultAsync(
            current => current.Id == orderId && current.TenantId == tenantId && current.ConversationId == conversationId,
            cancellationToken);
        if (order is null) return null;

        var existingPayment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(
            payment => payment.TenantId == tenantId && payment.OrderId == orderId, cancellationToken);
        if (existingPayment is not null) return new AgentOrderToolResult(order.Id, order.Number, order.Total, order.Status, existingPayment.PublicReference);

        if (order.Status != OrderStatus.AwaitingPayment) return null;

        var now = DateTimeOffset.UtcNow;
        var paymentIntent = await paymentProvider.CreatePaymentAsync(order.Id, order.Total, cancellationToken);
        var publicReference = PaymentReferenceGenerator.Generate();
        var payment = new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, Provider = paymentProvider.Name,
            ProviderReference = paymentIntent.ProviderReference, PublicReference = publicReference,
            IdempotencyKey = $"agent-payment-{order.Id:N}", Status = paymentIntent.Status, Amount = order.Total,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Payments.Add(payment);
        commerceEvents.Record(tenantId, CommerceEventType.PaymentStarted, conversationId: conversationId, orderId: order.Id, paymentId: payment.Id,
            metadata: new Dictionary<string, string> { ["provider"] = paymentProvider.Name });
        await db.SaveChangesAsync(cancellationToken);
        return new AgentOrderToolResult(order.Id, order.Number, order.Total, order.Status, publicReference);
    }

    public async Task<AgentOrderStatusToolResult?> GetOrderAsync(Guid tenantId, Guid conversationId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(current => current.Id == orderId && current.TenantId == tenantId && current.ConversationId == conversationId)
            .Select(current => new { current.Id, current.Number, current.Total, current.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return null;

        var payment = await db.Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.OrderId == order.Id)
            .OrderByDescending(payment => payment.CreatedAt)
            .Select(payment => new { payment.Status, payment.PublicReference })
            .FirstOrDefaultAsync(cancellationToken);
        return new AgentOrderStatusToolResult(order.Number, order.Total, order.Status, payment?.Status, payment?.PublicReference);
    }

    public async Task<IReadOnlyList<AgentProductToolResult>> ListAvailableProductsAsync(Guid tenantId, string? categoryQuery, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(product => product.TenantId == tenantId && product.IsActive)
            .OrderBy(product => product.Name)
            .Take(10)
            .Select(product => new { product.Id, product.Name, product.Sku, product.Price, product.ImageData, product.ImageMediaType })
            .ToListAsync(cancellationToken);
        var productIds = products.Select(product => product.Id).ToArray();
        var variantStockByProduct = await db.ProductVariants.AsNoTracking()
            .Where(variant => variant.TenantId == tenantId && variant.IsActive && productIds.Contains(variant.ProductId))
            .GroupBy(variant => variant.ProductId)
            .Select(group => new { ProductId = group.Key, HasVariants = true, StockQuantity = group.Sum(variant => variant.StockQuantity) })
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        var category = GetProductCategory(categoryQuery);
        var available = new List<AgentProductToolResult>();
        foreach (var product in products)
        {
            if (category is not null && GetProductCategory(product.Name) != category) continue;
            if (variantStockByProduct.TryGetValue(product.Id, out var stock) && stock.StockQuantity <= 0) continue;
            available.Add(new AgentProductToolResult(product.Id, product.Name, product.Sku, product.Price, product.ImageData is null ? null : Convert.ToBase64String(product.ImageData), product.ImageMediaType));
        }
        return available;
    }

    public async Task<bool> SubscribeStockAlertAsync(Guid tenantId, Guid conversationId, Guid productId, CancellationToken cancellationToken)
    {
        var customerId = await db.Conversations.Where(current => current.Id == conversationId && current.TenantId == tenantId).Select(current => current.CustomerId).FirstOrDefaultAsync(cancellationToken);
        if (!customerId.HasValue) return false;
        var exists = await db.StockAlertRequests.AnyAsync(current => current.TenantId == tenantId && current.CustomerId == customerId.Value && current.ProductId == productId && current.IsActive, cancellationToken);
        if (exists) return true;
        db.StockAlertRequests.Add(new StockAlertRequest { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId.Value, ProductId = productId, CreatedAt = DateTimeOffset.UtcNow, IsActive = true });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AgentCustomerToolResult?> GetCustomerAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var customerId = await db.Conversations.Where(current => current.Id == conversationId && current.TenantId == tenantId).Select(current => current.CustomerId).FirstOrDefaultAsync(cancellationToken);
        if (!customerId.HasValue) return null;
        return await db.Customers.AsNoTracking().Where(current => current.Id == customerId.Value && current.TenantId == tenantId)
            .Select(current => new AgentCustomerToolResult(current.Name, current.Phone, current.Address)).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AgentCustomerToolResult?> UpdateCustomerAsync(Guid tenantId, Guid conversationId, string name, string address, CancellationToken cancellationToken)
    {
        var customerId = await db.Conversations.Where(current => current.Id == conversationId && current.TenantId == tenantId).Select(current => current.CustomerId).FirstOrDefaultAsync(cancellationToken);
        if (!customerId.HasValue) return null;
        var customer = await db.Customers.FirstOrDefaultAsync(current => current.Id == customerId.Value && current.TenantId == tenantId, cancellationToken);
        if (customer is null) return null;
        customer.Name = name.Trim();
        customer.Address = address.Trim();
        customer.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new AgentCustomerToolResult(customer.Name, customer.Phone, customer.Address);
    }

    public async Task<AgentCartToolResult?> AddToCartAsync(Guid tenantId, Guid conversationId, Guid productId, int quantity, string requestText, CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 100) return null;
        var conversationExists = await db.Conversations.AnyAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (!conversationExists) return null;
        var product = await db.Products.FirstOrDefaultAsync(current => current.Id == productId && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (product is null) return null;
        var variants = await db.ProductVariants.Where(current => current.ProductId == productId && current.TenantId == tenantId && current.IsActive).OrderBy(current => current.Name).ToListAsync(cancellationToken);
        var terms = requestText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var variant = variants.FirstOrDefault(current => terms.Any(term => term.Length >= 2 && (current.Name.ToLower().Contains(term) || current.Sku.ToLower().Contains(term)))) ?? variants.FirstOrDefault();
        if (variants.Count > 0 && variant is null) return null;

        var cart = await db.Carts.AsNoTracking().FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (cart is null)
        {
            cart = new Cart { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, CreatedAt = now, UpdatedAt = now };
            db.Carts.Add(cart);
            commerceEvents.Record(tenantId, CommerceEventType.CartCreated, conversationId: conversationId, cartId: cart.Id);
        }
        var variantId = variant?.Id;
        var existing = cart.Id == Guid.Empty
            ? null
            : await db.CartItems.AsNoTracking().FirstOrDefaultAsync(item => item.CartId == cart.Id && item.ProductId == product.Id && item.VariantId == variantId, cancellationToken);
        var requestedQuantity = (existing?.Quantity ?? 0) + quantity;
        if (variant is not null && variant.StockQuantity < requestedQuantity) return null;
        if (existing is not null)
        {
            await db.CartItems.Where(item => item.Id == existing.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Quantity, requestedQuantity), cancellationToken);
        }
        else
        {
            db.CartItems.Add(new CartItem { Id = Guid.NewGuid(), CartId = cart.Id, ProductId = product.Id, VariantId = variant?.Id, ProductName = product.Name, Sku = variant?.Sku ?? product.Sku, Quantity = quantity, UnitPrice = variant?.PriceOverride ?? product.Price });
        }
        if (db.Entry(cart).State == EntityState.Added)
            cart.UpdatedAt = now;
        commerceEvents.Record(tenantId, CommerceEventType.ProductAddedToCart, conversationId: conversationId, productId: product.Id, cartId: cart.Id,
            metadata: new Dictionary<string, string> { ["quantity"] = quantity.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return await GetCartAsync(tenantId, conversationId, cancellationToken);
    }

    public async Task<AgentCartToolResult?> GetCartAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts.AsNoTracking().Include(current => current.Items).FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        return cart is null ? null : ToAgentCart(cart);
    }

    public async Task<AgentOrderToolResult?> CheckoutCartAsync(Guid tenantId, Guid conversationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        var cart = await db.Carts.Include(current => current.Items).FirstOrDefaultAsync(current => current.TenantId == tenantId && current.ConversationId == conversationId, cancellationToken);
        if (conversation is null || cart is null || cart.Items.Count == 0) return null;
        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(current => current.TenantId == tenantId && current.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null) return new AgentOrderToolResult(existing.Id, existing.Number, existing.Total, existing.Status);

        commerceEvents.Record(tenantId, CommerceEventType.CheckoutStarted, conversationId: conversationId, cartId: cart.Id);
        // A cart idle for over an hour that now converts counts as a recovered abandoned cart.
        var cartWasAbandoned = DateTimeOffset.UtcNow - cart.UpdatedAt > TimeSpan.FromHours(1);

        var productIds = cart.Items.Select(item => item.ProductId).ToArray();
        var products = await db.Products.Where(current => current.TenantId == tenantId && current.IsActive && productIds.Contains(current.Id)).ToDictionaryAsync(current => current.Id, cancellationToken);
        var variantIds = cart.Items.Where(item => item.VariantId.HasValue).Select(item => item.VariantId!.Value).ToArray();
        var variants = await db.ProductVariants.Where(current => current.TenantId == tenantId && current.IsActive && variantIds.Contains(current.Id)).ToDictionaryAsync(current => current.Id, cancellationToken);
        var orderItems = new List<OrderItem>();
        foreach (var cartItem in cart.Items)
        {
            if (!products.TryGetValue(cartItem.ProductId, out var product)) return null;
            ProductVariant? variant = null;
            if (cartItem.VariantId.HasValue && (!variants.TryGetValue(cartItem.VariantId.Value, out variant) || variant.ProductId != product.Id)) return null;
            if (variant is not null && variant.StockQuantity - variant.ReservedStock < cartItem.Quantity) return null;
            var unitPrice = variant?.PriceOverride ?? product.Price;
            orderItems.Add(new OrderItem { Id = Guid.NewGuid(), ProductId = product.Id, VariantId = variant?.Id, ProductName = product.Name, Sku = variant?.Sku ?? product.Sku, Quantity = cartItem.Quantity, UnitPrice = unitPrice, LineTotal = unitPrice * cartItem.Quantity });
        }
        var now = DateTimeOffset.UtcNow;
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = conversation.CustomerId, ConversationId = conversationId, Number = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}", IdempotencyKey = idempotencyKey, Status = OrderStatus.AwaitingPayment, Total = orderItems.Sum(item => item.LineTotal), CreatedAt = now, ReservationExpiresAt = OrderReservationPolicy.ExpiresAt(now, reservationOptions.Value), Items = orderItems };
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = "", ToStatus = OrderStatus.New, CreatedAt = now });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = OrderStatus.New, ToStatus = OrderStatus.Confirmed, CreatedAt = now });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = OrderStatus.Confirmed, ToStatus = OrderStatus.AwaitingPayment, CreatedAt = now });
        var paymentIntent = await paymentProvider.CreatePaymentAsync(order.Id, order.Total, cancellationToken);
        var publicReference = PaymentReferenceGenerator.Generate();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            Provider = paymentProvider.Name,
            ProviderReference = paymentIntent.ProviderReference,
            PublicReference = publicReference,
            IdempotencyKey = $"agent-payment-{order.Id:N}",
            Status = paymentIntent.Status,
            Amount = order.Total,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Payments.Add(payment);
        commerceEvents.Record(tenantId, CommerceEventType.OrderCreated, conversationId: conversationId, orderId: order.Id,
            metadata: new Dictionary<string, string> { ["source"] = AttributionSources.AiTool, ["total"] = order.Total.ToString("F2") });
        commerceEvents.Record(tenantId, CommerceEventType.PaymentStarted, conversationId: conversationId, orderId: order.Id, paymentId: payment.Id,
            metadata: new Dictionary<string, string> { ["provider"] = paymentProvider.Name });
        if (cartWasAbandoned)
        {
            commerceEvents.Record(tenantId, CommerceEventType.AbandonedCartRecovered, conversationId: conversationId, cartId: cart.Id, orderId: order.Id);
        }
        await using (var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null)
        {
            db.Orders.Add(order);
            db.CartItems.RemoveRange(cart.Items);
            await db.SaveChangesAsync(cancellationToken);
            await inventory.ReserveOrderAsync(order, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        return new AgentOrderToolResult(order.Id, order.Number, order.Total, order.Status, publicReference);
    }

    private static AgentCartToolResult ToAgentCart(Cart cart) => new(cart.Items.Sum(item => item.UnitPrice * item.Quantity), cart.Items.Select(item => new AgentCartItemToolResult(item.ProductName, item.Sku, item.Quantity, item.UnitPrice, item.UnitPrice * item.Quantity)).ToList());

    public async Task<IReadOnlyList<AgentProductToolResult>> SearchProductsAsync(Guid tenantId, string query, CancellationToken cancellationToken)
    {
        var queryTerms = query
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(term => term.Length >= 3)
            .Select(NormalizeSearchTerm)
            .Distinct()
            .ToArray();
        if (queryTerms.Length == 0) return [];

        var candidates = await db.Products.AsNoTracking()
            .Where(product => product.TenantId == tenantId && product.IsActive)
            .OrderBy(product => product.Name)
            .Select(product => new { product.Id, product.Name, product.Sku, product.Price, product.ImageData, product.ImageMediaType })
            .ToListAsync(cancellationToken);
        var category = GetProductCategory(query);
        var products = candidates
            .Where(product => queryTerms.Any(term => NormalizeSearchTerm(product.Name).Contains(term) || NormalizeSearchTerm(product.Sku).Contains(term)))
            .Where(product => category is null || GetProductCategory(product.Name) == category)
            .Take(10)
            .ToList();
        return products.Select(product => new AgentProductToolResult(
                product.Id,
                product.Name,
                product.Sku,
                product.Price,
                product.ImageData is null ? null : Convert.ToBase64String(product.ImageData),
                product.ImageMediaType)).ToList();
    }

    private static string NormalizeSearchTerm(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.EndsWith("es", StringComparison.Ordinal) && normalized.Length > 4)
            return normalized[..^2];
        return normalized.EndsWith('s') && normalized.Length > 3 ? normalized[..^1] : normalized;
    }

    private static string? GetProductCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.ToLowerInvariant();
        if (new[] { "zapato", "zapatos", "zapatilla", "zapatillas", "calzado", "botas", "sandalia" }.Any(normalized.Contains)) return "calzado";
        if (new[] { "camisa", "camisas", "remera", "remeras", "pantalon", "pantalones", "vestido", "ropa", "buzo", "campera" }.Any(normalized.Contains)) return "ropa";
        if (new[] { "telefono", "celular", "notebook", "laptop", "auricular", "electronica", "tecnologia" }.Any(normalized.Contains)) return "tecnologia";
        if (new[] { "silla", "mesa", "sofa", "mueble", "decoracion" }.Any(normalized.Contains)) return "hogar";
        return null;
    }

    public async Task<AgentStockToolResult?> CheckStockAsync(Guid tenantId, Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(current => current.Id == productId && current.TenantId == tenantId && current.IsActive, cancellationToken);
        if (product is null) return null;
        var variants = await db.ProductVariants.AsNoTracking().Where(variant => variant.ProductId == productId && variant.TenantId == tenantId && variant.IsActive).ToListAsync(cancellationToken);
        return variants.Count == 0 ? new AgentStockToolResult(productId, null, false) : new AgentStockToolResult(productId, variants.Sum(variant => variant.StockQuantity), true);
    }

    public async Task<AgentOrderToolResult?> CreateOrderAsync(Guid tenantId, Guid conversationId, Guid productId, int quantity, string requestText, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 100) return null;
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return null;

        var existing = await db.Orders.AsNoTracking().FirstOrDefaultAsync(
            current => current.TenantId == tenantId && current.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null) return new AgentOrderToolResult(existing.Id, existing.Number, existing.Total, existing.Status);

        var product = await db.Products.FirstOrDefaultAsync(
            current => current.Id == productId && current.TenantId == tenantId && current.IsActive,
            cancellationToken);
        if (product is null) return null;

        var variants = await db.ProductVariants
            .Where(current => current.ProductId == productId && current.TenantId == tenantId && current.IsActive && current.StockQuantity - current.ReservedStock >= quantity)
            .OrderBy(current => current.Name)
            .ToListAsync(cancellationToken);
        var requestTerms = requestText.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var variant = variants.FirstOrDefault(current => requestTerms.Any(term => term.Length >= 2 && (current.Name.ToLower().Contains(term) || current.Sku.ToLower().Contains(term))))
            ?? variants.FirstOrDefault();
        var activeVariantsExist = await db.ProductVariants.AnyAsync(
            current => current.ProductId == productId && current.TenantId == tenantId && current.IsActive,
            cancellationToken);
        if (activeVariantsExist && variant is null) return null;
        var unitPrice = variant?.PriceOverride ?? product.Price;
        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            VariantId = variant?.Id,
            ProductName = product.Name,
            Sku = variant?.Sku ?? product.Sku,
            Quantity = quantity,
            UnitPrice = unitPrice,
            LineTotal = unitPrice * quantity,
        };
        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = conversation.CustomerId,
            ConversationId = conversationId,
            Number = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            IdempotencyKey = idempotencyKey,
            Status = OrderStatus.AwaitingPayment,
            Total = item.LineTotal,
            CreatedAt = now,
            ReservationExpiresAt = OrderReservationPolicy.ExpiresAt(now, reservationOptions.Value),
            Items = [item],
        };
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = order.Id,
            FromStatus = "",
            ToStatus = OrderStatus.New,
            CreatedAt = now,
        });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = OrderStatus.New, ToStatus = OrderStatus.Confirmed, CreatedAt = now });
        order.StatusHistory.Add(new OrderStatusHistory { Id = Guid.NewGuid(), TenantId = tenantId, OrderId = order.Id, FromStatus = OrderStatus.Confirmed, ToStatus = OrderStatus.AwaitingPayment, CreatedAt = now });
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        db.Orders.Add(order);
        commerceEvents.Record(tenantId, CommerceEventType.OrderCreated, conversationId: conversationId, orderId: order.Id, productId: product.Id,
            metadata: new Dictionary<string, string> { ["source"] = AttributionSources.AiTool, ["total"] = order.Total.ToString("F2") });
        await db.SaveChangesAsync(cancellationToken);
        await inventory.ReserveOrderAsync(order, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new AgentOrderToolResult(order.Id, order.Number, order.Total, order.Status);
    }

    public async Task<AgentOrderStatusToolResult?> GetLatestOrderStatusAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.ConversationId == conversationId)
            .OrderByDescending(current => current.CreatedAt)
            .Select(current => new { current.Id, current.Number, current.Total, current.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (order is null) return null;

        var payment = await db.Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.OrderId == order.Id)
            .OrderByDescending(payment => payment.CreatedAt)
            .Select(payment => new { payment.Status, payment.PublicReference })
            .FirstOrDefaultAsync(cancellationToken);
        return new AgentOrderStatusToolResult(order.Number, order.Total, order.Status, payment?.Status, payment?.PublicReference);
    }

    public async Task<bool> SendMessageAsync(Guid tenantId, Guid conversationId, string text, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(current => current.Id == conversationId && current.TenantId == tenantId, cancellationToken);
        if (conversation is null) return false;
        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageAt = now;
        db.ConversationMessages.Add(new ConversationMessage { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, ExternalMessageId = $"agent-tool-{Guid.NewGuid():N}", Direction = "Outbound", Text = text.Trim(), CreatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
