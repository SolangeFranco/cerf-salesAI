using System.Text.Json;

namespace Vanta.Api.Modules.Payments;

public sealed record PaymentIntent(string ProviderReference, string Status);
public sealed record PaymentStatusResult(string ProviderReference, string Status);
public sealed record PaymentRefundResult(string ProviderReference, string Status);

public sealed class PaymentProviderOptions
{
    public string Provider { get; set; } = "mock";
}

// Framework-agnostic view of an inbound webhook call: raw body plus headers, so provider
// adapters can validate signatures without VANTA depending on ASP.NET request internals.
public sealed record PaymentWebhookSignal(string RawBody, IReadOnlyDictionary<string, string> Headers);

public sealed record PaymentWebhookValidationResult(bool IsValid, string? ProviderReference, string? EventId, string? Status)
{
    public static readonly PaymentWebhookValidationResult Invalid = new(false, null, null, null);
}

/// <summary>
/// Isolates VANTA's domain from any specific payment processor (Bancard, Pagopar, Tigo Money, ...).
/// Every provider adapter implements these four responsibilities; the domain never assumes a
/// particular provider's API shape, signature scheme or status vocabulary.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }

    Task<PaymentIntent> CreatePaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken);

    Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken cancellationToken);

    Task<PaymentRefundResult> RefundPaymentAsync(string providerReference, decimal amount, CancellationToken cancellationToken);

    // Validates the signature/authenticity of an inbound webhook call and normalizes its payload
    // into VANTA's own status vocabulary (PaymentStatus.*). Must not throw on malformed input.
    PaymentWebhookValidationResult ValidateWebhook(PaymentWebhookSignal signal);
}

public sealed class MockPaymentProvider : IPaymentProvider
{
    private const string SharedSecret = "mock-dev";

    public string Name => "mock";

    public Task<PaymentIntent> CreatePaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentIntent($"mock-{orderId:N}", PaymentStatus.Pending));

    // Deterministic mock: a server-to-server check always reports the payment as settled,
    // matching the mock-confirm dev workflow used by tests and demos.
    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentStatusResult(providerReference, PaymentStatus.Paid));

    public Task<PaymentRefundResult> RefundPaymentAsync(string providerReference, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentRefundResult(providerReference, PaymentStatus.Refunded));

    public PaymentWebhookValidationResult ValidateWebhook(PaymentWebhookSignal signal)
    {
        if (!signal.Headers.TryGetValue("X-Webhook-Signature", out var signature) || signature != SharedSecret)
            return PaymentWebhookValidationResult.Invalid;

        try
        {
            using var document = JsonDocument.Parse(signal.RawBody);
            var root = document.RootElement;
            var eventId = root.TryGetProperty("eventId", out var eventIdProperty) ? eventIdProperty.GetString() : null;
            var providerReference = root.TryGetProperty("providerReference", out var referenceProperty) ? referenceProperty.GetString() : null;
            var status = root.TryGetProperty("status", out var statusProperty) ? statusProperty.GetString() : null;

            return string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(providerReference) || string.IsNullOrWhiteSpace(status)
                ? PaymentWebhookValidationResult.Invalid
                : new PaymentWebhookValidationResult(true, providerReference, eventId, status);
        }
        catch (JsonException)
        {
            return PaymentWebhookValidationResult.Invalid;
        }
    }
}

public sealed class ManualTransferPaymentProvider : IPaymentProvider
{
    public string Name => "manual-transfer";

    public Task<PaymentIntent> CreatePaymentAsync(Guid orderId, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentIntent($"transfer-{orderId:N}", PaymentStatus.Pending));

    public Task<PaymentStatusResult> GetPaymentStatusAsync(string providerReference, CancellationToken cancellationToken) =>
        Task.FromResult(new PaymentStatusResult(providerReference, PaymentStatus.Pending));

    public Task<PaymentRefundResult> RefundPaymentAsync(string providerReference, decimal amount, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Los reembolsos por transferencia deben confirmarse manualmente por el comercio.");

    public PaymentWebhookValidationResult ValidateWebhook(PaymentWebhookSignal signal) => PaymentWebhookValidationResult.Invalid;
}

