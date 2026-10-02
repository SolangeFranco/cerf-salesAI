using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vanta.Api.Modules.Observability;

public static class HealthResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                }),
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    public static HealthCheckOptions Options(bool readyOnly) => new()
    {
        Predicate = check => !readyOnly || check.Tags.Contains("ready"),
        ResponseWriter = WriteAsync,
    };
}
