using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Vanta.Api.Data;
using Vanta.Api.Modules.Analytics;
using Vanta.Api.Modules.Commerce;

namespace Vanta.Api.Modules.WhatsApp;

public sealed class SalesAgentService(VantaDbContext db, IAgentProvider agentProvider, IAgentTools tools, IOptions<CommerceOptions> commerceOptions, IVisualProductMatcher visualMatcher, IImageUnderstandingProvider imageUnderstanding, ConversationService conversations, CommerceEventRecorder commerceEvents)
{
    public async Task<AgentResponseResult?> RespondAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            current => current.Id == conversationId && current.TenantId == tenantId,
            cancellationToken);
        if (conversation is null) return null;

        var inbound = await db.ConversationMessages
            .Where(message => message.ConversationId == conversationId && message.TenantId == tenantId && message.Direction == "Inbound")
            .OrderByDescending(message => message.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (inbound is null) return new AgentResponseResult("No encontré un mensaje entrante para responder.", false);

        // Only one caller may answer a given inbound message, and only while the AI is in
        // control (not paused, not handed off to a human, not closed).
        if (!await conversations.TryClaimAiResponseAsync(tenantId, conversationId, inbound.Id, cancellationToken))
            return new AgentResponseResult("El agente de IA no está activo para esta conversación.", false);

        var effectiveInboundText = string.IsNullOrWhiteSpace(inbound.AudioTranscript) ? inbound.Text : inbound.AudioTranscript;
        var normalizedInbound = effectiveInboundText.Trim().ToLowerInvariant();
        var previousInbound = await db.ConversationMessages
            .Where(message => message.ConversationId == conversationId && message.TenantId == tenantId && message.Direction == "Inbound" && message.Id != inbound.Id)
            .OrderByDescending(message => message.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var previousOutbound = await db.ConversationMessages
            .Where(message => message.ConversationId == conversationId && message.TenantId == tenantId && message.Direction == "Outbound")
            .OrderByDescending(message => message.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var conversationContext = await BuildConversationContextAsync(conversationId, tenantId, inbound.Id, cancellationToken);
        AgentProviderResponse response;
        var availableProducts = new List<AgentProductToolResult>();
        var checkoutDetails = ExtractCheckoutDetails(effectiveInboundText);
        var hasCheckoutDetails = checkoutDetails.Name is not null && checkoutDetails.Address is not null;
        var isAlternativeConfirmation = (IsAffirmativeConfirmation(normalizedInbound) || IsPurchaseSelection(normalizedInbound)) && previousOutbound is not null && (previousOutbound.Text.Contains("alternativa", StringComparison.OrdinalIgnoreCase) || previousOutbound.Text.Contains("opciones disponibles", StringComparison.OrdinalIgnoreCase));
        if (inbound.ImageData is not null)
        {
            response = await RespondToImageProductQuestionAsync(tenantId, inbound, cancellationToken);
        }
        else if (hasCheckoutDetails)
        {
            var checkoutResponse = await HandleCheckoutDetailsAsync(tenantId, conversationId, normalizedInbound, effectiveInboundText, cancellationToken);
            response = checkoutResponse ?? new AgentProviderResponse("No pude completar el checkout. Verificá tu nombre y dirección e intentá nuevamente.");
        }
        else if (IsCartSummaryRequest(normalizedInbound))
        {
            var cart = await tools.GetCartAsync(tenantId, conversationId, cancellationToken);
            response = cart is null || cart.Items.Count == 0
                ? new AgentProviderResponse("Tu carrito está vacío.")
                : new AgentProviderResponse(BuildCartSummaryResponse(cart));
        }
        else if (IsAlternativeRequest(normalizedInbound) || isAlternativeConfirmation)
        {
            if (IsPurchaseSelection(normalizedInbound) && previousOutbound is not null)
            {
                var selectedProducts = await tools.SearchProductsAsync(tenantId, previousOutbound.Text, cancellationToken);
                var selected = selectedProducts.FirstOrDefault();
                var cart = selected is null ? null : await tools.AddToCartAsync(tenantId, conversationId, selected.Id, 1, selected.Name, cancellationToken);
                response = cart is null
                    ? new AgentProviderResponse("No pude identificar la alternativa elegida. ¿Podés decirme el nombre del producto?")
                    : new AgentProviderResponse($"Agregué {selected!.Name} al carrito. Total: {GuaraniAmount.Format(cart.Total)} Gs. ¿Confirmás el pedido?", selected.ImageBase64, selected.ImageMediaType);
            }
            else
            {
                var alternatives = await tools.ListAvailableProductsAsync(tenantId, previousInbound?.Text, cancellationToken);
                if (alternatives.Count > 0)
                {
                    commerceEvents.Record(tenantId, CommerceEventType.ProductRecommended, conversationId: conversationId, productId: alternatives[0].Id,
                        metadata: new Dictionary<string, string> { ["count"] = alternatives.Count.ToString() });
                }
                response = alternatives.Count == 0
                    ? new AgentProviderResponse("No encontré alternativas disponibles en el catálogo en este momento.")
                    : new AgentProviderResponse(BuildAlternativeResponse(alternatives));
            }
        }
        else
        {
            var checkoutResponse = await HandleCheckoutDetailsAsync(tenantId, conversationId, normalizedInbound, effectiveInboundText, cancellationToken);
            if (checkoutResponse is not null)
            {
                response = checkoutResponse;
            }
            else if (IsPaymentIntent(normalizedInbound))
            {
                var orderStatus = await tools.GetLatestOrderStatusAsync(tenantId, conversationId, cancellationToken);
                response = orderStatus is null
                    ? new AgentProviderResponse("Todavía no encontré un pedido asociado a esta conversación.")
                    : new AgentProviderResponse(BuildPaymentResponse(orderStatus));
            }
            else if (IsOrderStatusIntent(normalizedInbound))
            {
                var orderStatus = await tools.GetLatestOrderStatusAsync(tenantId, conversationId, cancellationToken);
                response = orderStatus is null
                    ? new AgentProviderResponse("Todavía no encontré un pedido asociado a esta conversación.")
                    : new AgentProviderResponse(BuildOrderStatusResponse(orderStatus));
            }
            else
            {
                response = IsKnowledgeQuestion(normalizedInbound)
                    ? await BuildKnowledgeResponseAsync(tenantId, effectiveInboundText, cancellationToken)
                    : IsCatalogRequest(normalizedInbound)
                        ? await BuildCatalogResponseAsync(tenantId, cancellationToken)
                        : await RespondToCatalogOrPurchaseAsync(tenantId, conversationId, normalizedInbound, effectiveInboundText, previousInbound, conversationContext, cancellationToken);
            }
        }
        var text = response.Text;

        var productWithImage = availableProducts.FirstOrDefault(product => product.ImageBase64 is not null);
        var imageBase64 = response.ImageBase64 ?? productWithImage?.ImageBase64;
        var imageMediaType = response.ImageMediaType ?? productWithImage?.ImageMediaType;
        var now = DateTimeOffset.UtcNow;
        conversation.LastMessageAt = now;
        conversation.LastActivityAt = now;
        conversation.CurrentIntent = DetectIntent(normalizedInbound, hasCheckoutDetails, inbound.ImageData is not null);
        var imageData = imageBase64 is not null ? Convert.FromBase64String(imageBase64) : null;
        db.ConversationMessages.Add(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ConversationId = conversationId,
            ExternalMessageId = $"agent-out-{Guid.NewGuid():N}",
            Direction = "Outbound",
            Text = text,
            ImageData = imageData,
            ImageMediaType = imageMediaType,
            DeliveryStatus = "Pending",
            CreatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        return new AgentResponseResult(text, true);
    }

    private static int ExtractQuantity(string text)
    {
        var match = Regex.Match(text, @"(?:x|cantidad|unidades?|llevar|quiero|comprar)\s*(\d+)|(\d+)\s*(?:unidades?|productos?)", RegexOptions.IgnoreCase);
        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return match.Success && int.TryParse(value, out var quantity) ? Math.Clamp(quantity, 1, 100) : 1;
    }

    // Best-effort label describing what the customer's last message was about; surfaced to the
    // merchant in the conversation context view, not used for any routing decision.
    private static string DetectIntent(string normalizedInbound, bool hasCheckoutDetails, bool hasImage)
    {
        if (hasImage) return "ProductImageQuery";
        if (hasCheckoutDetails) return "Checkout";
        if (IsCartSummaryRequest(normalizedInbound)) return "CartSummary";
        if (IsPaymentIntent(normalizedInbound)) return "Payment";
        if (IsOrderStatusIntent(normalizedInbound)) return "OrderStatus";
        if (IsAlternativeRequest(normalizedInbound) || IsPurchaseSelection(normalizedInbound)) return "ProductAlternatives";
        if (IsKnowledgeQuestion(normalizedInbound)) return "Knowledge";
        if (IsCatalogRequest(normalizedInbound)) return "Catalog";
        return "CatalogOrPurchase";
    }

    private static bool IsKnowledgeQuestion(string text) =>
        text.Contains("horario") || text.Contains("politica") || text.Contains("política") ||
        text.Contains("garantia") || text.Contains("garantía") || text.Contains("devolucion") ||
        text.Contains("devolución") || text.Contains("cambio de producto") || text.Contains("promocion") ||
        text.Contains("promoción") || text.Contains("descuento") || text.Contains("preguntas frecuentes") ||
        text.Contains("faq");

    private async Task<AgentProviderResponse> BuildKnowledgeResponseAsync(Guid tenantId, string rawText, CancellationToken cancellationToken)
    {
        var results = await tools.SearchKnowledgeAsync(tenantId, rawText, cancellationToken);
        // Content already went through AgentContentSanitizer inside AgentTools: information to
        // relay, never an instruction to follow.
        return results.Count == 0
            ? new AgentProviderResponse("No encontré información sobre eso todavía. ¿Querés que lo derive a un miembro del equipo?")
            : new AgentProviderResponse(results[0].Content);
    }

    private async Task<AgentProviderResponse> RespondToCatalogOrPurchaseAsync(Guid tenantId, Guid conversationId, string normalizedInbound, string rawText, ConversationMessage? previousInbound, string conversationContext, CancellationToken cancellationToken)
    {
        var confirmation = IsAffirmativeConfirmation(normalizedInbound);
        var purchaseRequest = confirmation ? previousInbound?.Text : rawText;
        var searchText = string.Join(" ", new[] { purchaseRequest, previousInbound?.Text }.Where(text => !string.IsNullOrWhiteSpace(text)));
        var products = await tools.SearchProductsAsync(tenantId, searchText.ToLowerInvariant(), cancellationToken);
        var availableProducts = new List<AgentProductToolResult>();
        foreach (var product in products)
        {
            var stock = await tools.CheckStockAsync(tenantId, product.Id, cancellationToken);
            if (stock is { HasVariants: true, StockQuantity: <= 0 }) continue;
            availableProducts.Add(product);
        }
        if (products.Count > 0 && availableProducts.Count == 0)
        {
            var unavailableProduct = products[0];
            if (confirmation && previousInbound is not null)
            {
                var subscribed = await tools.SubscribeStockAlertAsync(tenantId, conversationId, unavailableProduct.Id, cancellationToken);
                return subscribed
                    ? new AgentProviderResponse($"Perfecto, registré tu solicitud. Te avisaré cuando vuelva a haber stock de {unavailableProduct.Name}.")
                    : new AgentProviderResponse("No pude registrar el aviso de stock. Por favor intentá nuevamente.");
            }
            return new AgentProviderResponse($"Encontré {unavailableProduct.Name}, pero en este momento está sin stock. ¿Querés que te avise cuando vuelva a estar disponible?");
        }
        if (availableProducts.Count > 0)
        {
            commerceEvents.Record(tenantId, CommerceEventType.ProductViewed, conversationId: conversationId, productId: availableProducts[0].Id);
        }
        var hasPurchaseIntent = (normalizedInbound.Contains("comprar") || normalizedInbound.Contains("comprarla") || normalizedInbound.Contains("comprarlo") || normalizedInbound.Contains("compro") || normalizedInbound.Contains("la compro") || normalizedInbound.Contains("lo compro") || normalizedInbound.Contains("me la llevo") || normalizedInbound.Contains("me lo llevo") || normalizedInbound.Contains("la quiero") || normalizedInbound.Contains("lo quiero") || normalizedInbound.Contains("quiero esa") || normalizedInbound.Contains("quiero la ") || normalizedInbound.Contains("quiero el ")) && !normalizedInbound.Contains("no quiero");
        if (hasPurchaseIntent && availableProducts.Count > 0 && !confirmation)
        {
            var product = availableProducts[0];
            var quantity = ExtractQuantity(rawText);
            var cart = await tools.AddToCartAsync(tenantId, conversationId, product.Id, quantity, rawText, cancellationToken);
            return cart is null
                ? new AgentProviderResponse("No pude agregar el producto al carrito. Verificá la variante y el stock disponible.")
                : new AgentProviderResponse($"Agregué {quantity} unidad(es) de {product.Name} al carrito. Total: {GuaraniAmount.Format(cart.Total)} Gs. ¿Confirmás el pedido?", product.ImageBase64, product.ImageMediaType);
        }
        if (confirmation && previousInbound is not null && availableProducts.Count > 0)
        {
            var product = availableProducts[0];
            var order = await tools.CheckoutCartAsync(tenantId, conversationId, $"agent-order-{Guid.NewGuid():N}", cancellationToken);
            return order is null
                ? new AgentProviderResponse("No pude confirmar el carrito. Verificá el stock disponible.")
                : new AgentProviderResponse(BuildOrderCreatedResponse(order), product.ImageBase64, product.ImageMediaType);
        }
        return await agentProvider.GenerateResponseAsync(rawText, availableProducts.Take(3).ToList(), conversationContext, cancellationToken);
    }

    private async Task<string> BuildConversationContextAsync(Guid conversationId, Guid tenantId, Guid currentMessageId, CancellationToken cancellationToken)
    {
        var messages = await db.ConversationMessages.AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.TenantId == tenantId && message.Id != currentMessageId && !message.IsDeleted)
            .OrderByDescending(message => message.CreatedAt)
            .Take(8)
            .OrderBy(message => message.CreatedAt)
            .Select(message => new { message.Direction, message.Text, message.AudioTranscript })
            .ToListAsync(cancellationToken);

        return messages.Count == 0
            ? "Sin mensajes anteriores."
            : string.Join("\n", messages.Select(message =>
                $"{(message.Direction == "Inbound" ? "Cliente" : "VANTA")}: {AgentContentSanitizer.SanitizeUserMessageForPrompt(string.IsNullOrWhiteSpace(message.AudioTranscript) ? message.Text : message.AudioTranscript)}"));
    }

    private async Task<AgentProviderResponse> RespondToImageProductQuestionAsync(Guid tenantId, ConversationMessage inbound, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking()
            .Where(current => current.TenantId == tenantId && current.IsActive && current.ImageData != null && current.ImageData.SequenceEqual(inbound.ImageData!))
            .Select(current => new AgentProductToolResult(current.Id, current.Name, current.Sku, current.Price, current.ImageData == null ? null : Convert.ToBase64String(current.ImageData), current.ImageMediaType))
            .FirstOrDefaultAsync(cancellationToken)
            ?? await visualMatcher.FindBestMatchAsync(tenantId, inbound.ImageData!, cancellationToken);

        if (product is null && !string.IsNullOrWhiteSpace(inbound.Text))
        {
            var matches = await tools.SearchProductsAsync(tenantId, inbound.Text, cancellationToken);
            product = matches.FirstOrDefault();
        }

        if (product is null && inbound.ImageMediaType is not null)
        {
            var visualQuery = await imageUnderstanding.GenerateProductSearchQueryAsync(inbound.ImageData!, inbound.ImageMediaType, inbound.Text, cancellationToken);
            if (!string.IsNullOrWhiteSpace(visualQuery))
            {
                var matches = await tools.SearchProductsAsync(tenantId, visualQuery, cancellationToken);
                product = matches.FirstOrDefault();
            }
        }

        if (product is null)
            return new AgentProviderResponse("Recibí la imagen, pero todavía no puedo identificar ese producto con seguridad. ¿Podés decirme el nombre, marca, talle o alguna característica para buscarlo en el catálogo?");

        commerceEvents.Record(tenantId, CommerceEventType.ProductViewed, conversationId: inbound.ConversationId, productId: product.Id,
            metadata: new Dictionary<string, string> { ["source"] = "image" });

        var stock = await tools.CheckStockAsync(tenantId, product.Id, cancellationToken);
        if (stock is { HasVariants: true, StockQuantity: <= 0 })
            return new AgentProviderResponse($"Sí, identifiqué {product.Name}, pero ahora mismo figura sin stock. ¿Querés que te avise cuando vuelva a estar disponible?", product.ImageBase64, product.ImageMediaType);

        var stockText = stock is { HasVariants: true, StockQuantity: not null }
            ? $" Stock disponible: {stock.StockQuantity} unidad(es)."
            : " La disponibilidad se confirma al elegir variante.";
        return new AgentProviderResponse($"Sí, tenemos {product.Name} en el catálogo por {GuaraniAmount.Format(product.Price)} Gs.{stockText} ¿Querés que lo agregue al carrito?", product.ImageBase64, product.ImageMediaType);
    }

    private async Task<AgentProviderResponse?> HandleCheckoutDetailsAsync(Guid tenantId, Guid conversationId, string normalizedText, string rawText, CancellationToken cancellationToken)
    {
        var cart = await tools.GetCartAsync(tenantId, conversationId, cancellationToken);
        if (cart is null || cart.Items.Count == 0) return null;

        var customer = await tools.GetCustomerAsync(tenantId, conversationId, cancellationToken);
        if (customer is null) return null;
        var details = ExtractCheckoutDetails(rawText);
        if (details.Name is not null && details.Address is not null)
        {
            await tools.UpdateCustomerAsync(tenantId, conversationId, details.Name, details.Address, cancellationToken);
            var order = await tools.CheckoutCartAsync(tenantId, conversationId, $"agent-order-{Guid.NewGuid():N}", cancellationToken);
            return order is null
                ? new AgentProviderResponse("No pude confirmar el pedido. Verificá el stock disponible.")
                : new AgentProviderResponse(BuildOrderCreatedResponse(order));
        }

        if (details.Name is not null || details.Address is not null)
            return new AgentProviderResponse("Me falta un dato para completar el pedido. Para guardar correctamente tus datos, respondé exactamente con este formato: Nombre: Ana, Dirección: Av. España 123, Asunción.");

        if (!IsAffirmativeConfirmation(normalizedText)) return null;
        if (customer.Name.StartsWith("WhatsApp ", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(customer.Address))
            return new AgentProviderResponse("Para guardar los datos del cliente y completar el pedido, respondé exactamente con este formato: Nombre: Ana, Dirección: Av. España 123, Asunción.");

        var confirmedOrder = await tools.CheckoutCartAsync(tenantId, conversationId, $"agent-order-{Guid.NewGuid():N}", cancellationToken);
        return confirmedOrder is null
            ? new AgentProviderResponse("No pude confirmar el pedido. Verificá el stock disponible.")
            : new AgentProviderResponse(BuildOrderCreatedResponse(confirmedOrder));
    }

    private static (string? Name, string? Address) ExtractCheckoutDetails(string text)
    {
        var name = Regex.Match(text, @"(?:nombre|me llamo)\s*(?:es|:)?\s*([^,;]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        var address = Regex.Match(text, @"(?:dirección|direccion)\s*(?:es|:)?\s*(.+)$", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
        if (name.Length < 2 && address.Length < 5)
        {
            var natural = Regex.Match(text, @"^([^,;]{2,})[,;]\s*(.{5,})$", RegexOptions.IgnoreCase);
            if (natural.Success)
            {
                name = natural.Groups[1].Value.Trim();
                address = natural.Groups[2].Value.Trim();
            }
        }
        return (name.Length >= 2 ? name : null, address.Length >= 5 ? address : null);
    }

    private static bool IsOrderStatusIntent(string text) =>
        (text.Contains("pedido") || text.Contains("orden") || text.Contains("pago") || text.Contains("envío") || text.Contains("envio"))
        && (text.Contains("estado") || text.Contains("dónde") || text.Contains("donde") || text.Contains("ya está") || text.Contains("ya esta") || text.Contains("seguimiento") || text.Contains("pagado"));

    private static bool IsPaymentIntent(string text) =>
        text.Contains("pagar") || text.Contains("pago") || text.Contains("abon") || text.Contains("transferir") || text.Contains("transferencia");

    private static bool IsAlternativeRequest(string text) =>
        text.Contains("mostrar") || text.Contains("muéstrame") || text.Contains("muestrame") || text.Contains("alternativa") || text.Contains("otra opción") || text.Contains("otra opcion");

    private static bool IsCatalogRequest(string text) =>
        text.Contains("catálogo") || text.Contains("catalogo") || text.Contains("ver productos") || text.Contains("ver la tienda") || text.Contains("tienda online") || text.Contains("página web") || text.Contains("pagina web");

    private static bool IsCartSummaryRequest(string text) =>
        text.Contains("carrito") && (text.Contains("mostrar") || text.Contains("ver") || text.Contains("producto") || text.Contains("resumen"));

    private static bool IsPurchaseSelection(string text) =>
        text.Contains("la quiero") || text.Contains("lo quiero") || text.Contains("quiero esa") || text.Contains("quiero ese");

    private static bool IsAffirmativeConfirmation(string text)
    {
        var normalized = Regex.Replace(text, @"[^a-záéíóúüñ ]", " ", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized is "sí" or "si" or "confirmo" or "confirmado" or "dale" or "ok" or "okay" or "de acuerdo"
            || normalized.StartsWith("si confirm", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("sí confirm", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("confirmo el", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("ok confirm", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("dale confirm", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOrderStatusResponse(AgentOrderStatusToolResult order)
    {
        var status = order.Status switch
        {
            "New" => "nuevo",
            "Confirmed" => "confirmado",
            "Paid" => "pagado",
            "Preparing" => "en preparación",
            "Shipped" => "enviado",
            "Delivered" => "entregado",
            "Cancelled" => "cancelado",
            _ => order.Status,
        };
        var payment = order.PaymentStatus switch
        {
            "Paid" => "El pago está confirmado.",
            "Pending" => "El pago está pendiente.",
            _ => "Todavía no hay un pago solicitado.",
        };
        return $"Tu pedido {order.Number} por {GuaraniAmount.Format(order.Total)} Gs. está {status}. {payment}";
    }

    private static string BuildPaymentResponse(AgentOrderStatusToolResult order)
    {
        return order.PaymentStatus switch
        {
            "Paid" => $"El pago de tu pedido {order.Number} ya está confirmado. El pedido está pagado.",
            "Pending" when !string.IsNullOrWhiteSpace(order.PaymentReference) => $"Tu pedido {order.Number} está confirmado por {GuaraniAmount.Format(order.Total)} Gs. Para pagar, usá la referencia {order.PaymentReference}. El pago seguirá pendiente hasta que recibamos la confirmación del proveedor.",
            "Pending" => $"Tu pedido {order.Number} está confirmado por {GuaraniAmount.Format(order.Total)} Gs. El pago todavía está pendiente.",
            _ => $"Tu pedido {order.Number} está confirmado por {GuaraniAmount.Format(order.Total)} Gs., pero todavía no hay un pago generado.",
        };
    }

    private static string BuildAlternativeResponse(IReadOnlyList<AgentProductToolResult> products) =>
        "Estas son algunas opciones disponibles: " + string.Join("; ", products.Take(3).Select(product => $"{product.Name} ({GuaraniAmount.Format(product.Price)} Gs.)")) + ". ¿Querés consultar alguna?";

    private static string BuildCartSummaryResponse(AgentCartToolResult cart) =>
        "Tu carrito tiene: " + string.Join("; ", cart.Items.Select(item => $"{item.Quantity} x {item.ProductName} ({GuaraniAmount.Format(item.LineTotal)} Gs.)")) + $". Total: {GuaraniAmount.Format(cart.Total)} Gs. ¿Confirmás el pedido?";

    private async Task<AgentProviderResponse> BuildCatalogResponseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var slug = await db.Tenants.AsNoTracking().Where(tenant => tenant.Id == tenantId).Select(tenant => tenant.Slug).FirstOrDefaultAsync(cancellationToken) ?? tenantId.ToString("N");
        var url = CommerceUrlBuilder.CatalogUrl(commerceOptions.Value, slug);
        return new AgentProviderResponse($"Claro. Podés revisar nuestro catálogo completo acá: {url}. Si preferís, también puedo ayudarte a elegir y comprar directamente por esta conversación.");
    }

    private string BuildOrderCreatedResponse(AgentOrderToolResult order)
    {
        if (string.IsNullOrWhiteSpace(order.PaymentReference))
            return $"Listo. Creé y confirmé el pedido {order.Number} por {GuaraniAmount.Format(order.Total)} Gs. El pago quedó pendiente.";

        var paymentUrl = CommerceUrlBuilder.CheckoutUrl(commerceOptions.Value, order.PaymentReference);
        return $"Listo. Creé y confirmé el pedido {order.Number} por {GuaraniAmount.Format(order.Total)} Gs. Para completar el pago, ingresá acá: {paymentUrl}. El pago seguirá pendiente hasta que recibamos la confirmación.";
    }
}

public sealed record AgentResponseResult(string Text, bool Responded);
