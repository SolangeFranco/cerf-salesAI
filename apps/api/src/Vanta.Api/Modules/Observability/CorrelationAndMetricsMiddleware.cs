using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Vanta.Api.Modules.Observability;

public sealed class CorrelationAndMetricsMiddleware(RequestDelegate next, ILogger<CorrelationAndMetricsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var requestId = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = requestId;
        var correlationId = GetSafeCorrelationId(context.Request.Headers[ObservabilityNames.CorrelationHeader].ToString())
            ?? requestId;
        context.Response.Headers[ObservabilityNames.RequestHeader] = requestId;
        context.Response.Headers[ObservabilityNames.CorrelationHeader] = correlationId;

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["CorrelationId"] = correlationId,
        });
        Activity.Current?.SetTag("vanta.request_id", requestId);
        Activity.Current?.SetTag("vanta.correlation_id", correlationId);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await next(context);
        }
        finally
        {
            var duration = Stopwatch.GetElapsedTime(startedAt);
            var route = context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown";
            var tags = new TagList
            {
                { "method", context.Request.Method },
                { "route", route },
                { "status_code", context.Response.StatusCode.ToString(CultureInfo.InvariantCulture) },
            };
            VantaTelemetry.Requests.Add(1, tags);
            VantaTelemetry.RequestDuration.Record(duration.TotalSeconds, tags);
            if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                VantaTelemetry.ApiErrors.Add(1, tags);
            }

            logger.LogInformation(
                "HTTP request completed {Method} {Path} with {StatusCode} in {ElapsedMilliseconds} ms.",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, duration.TotalMilliseconds);
        }
    }

    private static string? GetSafeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return null;
        foreach (var character in value)
        {
            if (!(char.IsLetterOrDigit(character) || character is '-' or '_' or '.')) return null;
        }
        return value;
    }
}
