using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Vanta.Api.Modules.Observability;

public static class VantaTelemetry
{
    public const string ActivitySourceName = "Vanta.Api";
    public const string MeterName = "Vanta.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "vanta.api.requests", unit: "{request}", description: "HTTP requests completed.");
    public static readonly Counter<long> ApiErrors = Meter.CreateCounter<long>(
        "vanta.api.errors", unit: "{error}", description: "HTTP 4xx and 5xx responses.");
    public static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "vanta.api.request.duration", unit: "s", description: "HTTP request duration.");
    public static readonly Counter<long> WebhookFailures = Meter.CreateCounter<long>(
        "vanta.webhook.failures", unit: "{failure}", description: "Webhook validation and processing failures.");
    public static readonly Counter<long> PaymentFailures = Meter.CreateCounter<long>(
        "vanta.payment.failures", unit: "{failure}", description: "Payment transition or provider failures.");
    public static readonly Counter<long> AiFailures = Meter.CreateCounter<long>(
        "vanta.ai.failures", unit: "{failure}", description: "AI provider failures and fallbacks.");
    public static readonly Counter<long> BackgroundJobFailures = Meter.CreateCounter<long>(
        "vanta.background_job.failures", unit: "{failure}", description: "Background job failures.");
    public static readonly Counter<long> WhatsAppFailures = Meter.CreateCounter<long>(
        "vanta.whatsapp.failures", unit: "{failure}", description: "WhatsApp provider failures.");

    public static void Count(Counter<long> counter, string operation)
    {
        var tags = new TagList { { "operation", operation } };
        counter.Add(1, tags);
    }
}

public static class ObservabilityNames
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string RequestHeader = "X-Request-Id";
}
