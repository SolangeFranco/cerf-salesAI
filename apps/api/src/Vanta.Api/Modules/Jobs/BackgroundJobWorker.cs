using System.Text.Json;
using Vanta.Api.Modules.Orders;
using Vanta.Api.Modules.WhatsApp;

namespace Vanta.Api.Modules.Jobs;

public sealed class BackgroundJobWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BackgroundJobWorker> logger) : BackgroundService
{
    private readonly string workerId = $"api-{Environment.ProcessId}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<BackgroundJobService>();
                var delivery = scope.ServiceProvider.GetRequiredService<WhatsAppDeliveryService>();
                var expiration = scope.ServiceProvider.GetRequiredService<OrderExpirationService>();
                await delivery.EnqueuePendingJobsAsync(jobs, stoppingToken);
                await expiration.EnqueueExpiredAsync(jobs, stoppingToken);

                var deliveryJob = await jobs.ClaimNextAsync(BackgroundJobType.WhatsAppDelivery, workerId, stoppingToken);
                if (deliveryJob is not null)
                {
                    await ProcessDeliveryAsync(deliveryJob, jobs, delivery, stoppingToken);
                }

                var expirationJob = await jobs.ClaimNextAsync(BackgroundJobType.OrderExpiration, workerId, stoppingToken);
                if (expirationJob is not null)
                {
                    await ProcessExpirationAsync(expirationJob, jobs, expiration, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Background job worker failed while polling.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task ProcessDeliveryAsync(
        BackgroundJob job,
        BackgroundJobService jobs,
        WhatsAppDeliveryService delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            using var payload = JsonDocument.Parse(job.Payload);
            var messageId = payload.RootElement.GetProperty("messageId").GetGuid();
            await delivery.ProcessMessageAsync(messageId, cancellationToken);
            await jobs.CompleteAsync(job.TenantId, job.Id, workerId, cancellationToken);
            logger.LogInformation("Background job {JobId} completed for tenant {TenantId}.", job.Id, job.TenantId);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or HttpRequestException or InvalidOperationException)
        {
            await jobs.FailAsync(job.TenantId, job.Id, workerId, exception, cancellationToken);
        }
    }

    private async Task ProcessExpirationAsync(
        BackgroundJob job,
        BackgroundJobService jobs,
        OrderExpirationService expiration,
        CancellationToken cancellationToken)
    {
        try
        {
            await expiration.ExpireAsync(job, cancellationToken);
            await jobs.CompleteAsync(job.TenantId, job.Id, workerId, cancellationToken);
            logger.LogInformation("Order expiration job {JobId} completed for tenant {TenantId}.", job.Id, job.TenantId);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            await FailInIsolatedScopeAsync(job, exception, cancellationToken);
        }
    }

    private async Task FailInIsolatedScopeAsync(
        BackgroundJob job,
        Exception exception,
        CancellationToken cancellationToken)
    {
        using var failureScope = scopeFactory.CreateScope();
        var failureJobs = failureScope.ServiceProvider.GetRequiredService<BackgroundJobService>();
        await failureJobs.FailAsync(job.TenantId, job.Id, workerId, exception, cancellationToken);
    }
}