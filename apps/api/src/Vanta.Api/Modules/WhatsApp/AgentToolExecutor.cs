using System.Text.Json;

namespace Vanta.Api.Modules.WhatsApp;

public sealed class AgentToolInputException(string message) : Exception(message);

public sealed record AgentToolExecutionResult(bool Success, string ToolName, object? Data, string? Error)
{
    public static AgentToolExecutionResult Unauthorized(string toolName) => new(false, toolName, null, "unauthorized_tool");
    public static AgentToolExecutionResult InvalidInput(string toolName, string error) => new(false, toolName, null, error);
    public static AgentToolExecutionResult Ok(string toolName, object? data) => new(true, toolName, data, null);
}

/// <summary>
/// The ONLY way any part of VANTA (the deterministic planner today, or a future real
/// function-calling LLM) invokes agent capabilities. Enforces the closed tool catalog, parses and
/// validates arguments defensively, and always uses the caller-supplied (server-authenticated)
/// tenant/conversation — arguments can never redirect a call to another tenant's data because
/// tenantId/conversationId are never read from them. Every domain check (ownership, stock,
/// idempotency, business rules) still lives in <see cref="IAgentTools"/>/<see cref="ConversationService"/>;
/// this executor only adds the authorization boundary and an audit log per call.
/// </summary>
public sealed class AgentToolExecutor(IAgentTools tools, ConversationService conversations, ILogger<AgentToolExecutor> logger)
{
    public async Task<AgentToolExecutionResult> ExecuteAsync(
        Guid tenantId, Guid conversationId, string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!AgentToolCatalog.IsRegistered(toolName))
        {
            logger.LogWarning("Rejected call to unauthorized agent tool {ToolName} for tenant {TenantId}.", toolName, tenantId);
            return AgentToolExecutionResult.Unauthorized(toolName);
        }

        try
        {
            object? data = toolName switch
            {
                AgentToolNames.SearchProducts => await tools.SearchProductsAsync(tenantId, RequireString(arguments, "query"), cancellationToken),
                AgentToolNames.GetProduct => await tools.GetProductAsync(tenantId, RequireGuid(arguments, "productId"), cancellationToken),
                AgentToolNames.CheckStock => await tools.CheckStockAsync(tenantId, RequireGuid(arguments, "productId"), cancellationToken),
                AgentToolNames.GetCustomer => await tools.GetCustomerAsync(tenantId, conversationId, cancellationToken),
                AgentToolNames.CreateCart => await tools.CreateCartAsync(tenantId, conversationId, cancellationToken),
                AgentToolNames.AddToCart => await tools.AddToCartAsync(tenantId, conversationId, RequireGuid(arguments, "productId"), RequireInt(arguments, "quantity"), OptionalString(arguments, "requestText") ?? string.Empty, cancellationToken),
                AgentToolNames.CalculateShipping => await tools.CalculateShippingAsync(tenantId, RequireString(arguments, "address"), cancellationToken),
                AgentToolNames.CreateOrder => await tools.CreateOrderAsync(tenantId, conversationId, RequireGuid(arguments, "productId"), RequireInt(arguments, "quantity"), OptionalString(arguments, "requestText") ?? string.Empty, RequireString(arguments, "idempotencyKey"), cancellationToken),
                AgentToolNames.CreatePayment => await tools.CreatePaymentAsync(tenantId, conversationId, RequireGuid(arguments, "orderId"), cancellationToken),
                AgentToolNames.GetOrder => await tools.GetOrderAsync(tenantId, conversationId, RequireGuid(arguments, "orderId"), cancellationToken),
                AgentToolNames.SendMessage => await tools.SendMessageAsync(tenantId, conversationId, RequireString(arguments, "text"), cancellationToken),
                AgentToolNames.HandoffToHuman => await conversations.RequestHandoffAsync(tenantId, conversationId, cancellationToken),
                AgentToolNames.SearchKnowledge => await tools.SearchKnowledgeAsync(tenantId, RequireString(arguments, "query"), cancellationToken),
                _ => throw new AgentToolInputException("unreachable"),
            };
            logger.LogInformation("Agent tool {ToolName} executed for tenant {TenantId} conversation {ConversationId}.", toolName, tenantId, conversationId);
            return AgentToolExecutionResult.Ok(toolName, data);
        }
        catch (AgentToolInputException exception)
        {
            logger.LogWarning("Agent tool {ToolName} rejected invalid input for tenant {TenantId}: {Reason}", toolName, tenantId, exception.Message);
            return AgentToolExecutionResult.InvalidInput(toolName, exception.Message);
        }
    }

    private static string RequireString(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new AgentToolInputException($"Missing or invalid required argument '{name}'.");
        return value.GetString()!;
    }

    private static string? OptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid RequireGuid(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var guid))
            throw new AgentToolInputException($"Missing or invalid required argument '{name}'.");
        return guid;
    }

    private static int RequireInt(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new AgentToolInputException($"Missing or invalid required argument '{name}'.");
        return number;
    }
}
