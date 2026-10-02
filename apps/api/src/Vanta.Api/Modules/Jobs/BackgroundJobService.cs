using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vanta.Api.Data;
using Vanta.Api.Modules.Observability;

namespace Vanta.Api.Modules.Jobs;

public sealed class BackgroundJobService(VantaDbContext db, ILogger<BackgroundJobService> logger)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<BackgroundJob> EnqueueAsync(
        Guid tenantId,
        string type,
        string idempotencyKey,
        object payload,
        DateTimeOffset? availableAt = null,
        int maxAttempts = 5,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.BackgroundJobs.FirstOrDefaultAsync(job =>
            job.TenantId == tenantId && job.Type == type && job.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null) return existing;

        var job = new BackgroundJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Type = type,
            IdempotencyKey = idempotencyKey,
            Payload = JsonSerializer.Serialize(payload),
            Status = BackgroundJobStatus.Pending,
            MaxAttempts = Math.Clamp(maxAttempts, 1, 20),
            AvailableAt = availableAt ?? DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Background job {JobId} enqueued for tenant {TenantId}: {JobType}.", job.Id, tenantId, type);
        return job;
    }

    public async Task<BackgroundJob?> ClaimNextAsync(
        string type,
        string workerId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (!db.Database.IsRelational())
        {
            var expiredJobs = await db.BackgroundJobs
                .Where(job => job.Status == BackgroundJobStatus.Running && job.LockedUntil <= now)
                .ToListAsync(cancellationToken);
            foreach (var expiredJob in expiredJobs)
            {
                expiredJob.Status = BackgroundJobStatus.Pending;
                expiredJob.WorkerId = null;
                expiredJob.LockedUntil = null;
            }

            var inMemoryCandidate = await db.BackgroundJobs
                .Where(job => job.Type == type && job.Status == BackgroundJobStatus.Pending && job.AvailableAt <= now)
                .OrderBy(job => job.AvailableAt)
                .ThenBy(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (inMemoryCandidate is null) return null;
            inMemoryCandidate.Status = BackgroundJobStatus.Running;
            inMemoryCandidate.Attempts++;
            inMemoryCandidate.WorkerId = workerId;
            inMemoryCandidate.LockedUntil = now.Add(LeaseDuration);
            await db.SaveChangesAsync(cancellationToken);
            return inMemoryCandidate;
        }

        var expired = await db.BackgroundJobs
            .Where(job => job.Status == BackgroundJobStatus.Running && job.LockedUntil <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, BackgroundJobStatus.Pending)
                .SetProperty(job => job.WorkerId, (string?)null)
                .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null), cancellationToken);
        if (expired > 0) logger.LogWarning("Requeued {ExpiredJobs} expired background job leases.", expired);

        var candidate = await db.BackgroundJobs
            .Where(job => job.Type == type && job.Status == BackgroundJobStatus.Pending && job.AvailableAt <= now)
            .OrderBy(job => job.AvailableAt)
            .ThenBy(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate is null) return null;

        var claimed = await db.BackgroundJobs
            .Where(job => job.Id == candidate.Id && job.Status == BackgroundJobStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, BackgroundJobStatus.Running)
                .SetProperty(job => job.Attempts, job => job.Attempts + 1)
                .SetProperty(job => job.WorkerId, workerId)
                .SetProperty(job => job.LockedUntil, now.Add(LeaseDuration)), cancellationToken);
        if (claimed == 0) return null;

        return await db.BackgroundJobs.AsNoTracking().FirstAsync(job => job.Id == candidate.Id, cancellationToken);
    }

    public async Task CompleteAsync(Guid tenantId, Guid jobId, string workerId, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsRelational())
        {
            var inMemoryJob = await db.BackgroundJobs.FirstOrDefaultAsync(job =>
                job.Id == jobId && job.TenantId == tenantId && job.Status == BackgroundJobStatus.Running && job.WorkerId == workerId, cancellationToken);
            if (inMemoryJob is null) return;
            inMemoryJob.Status = BackgroundJobStatus.Succeeded;
            inMemoryJob.CompletedAt = DateTimeOffset.UtcNow;
            inMemoryJob.LockedUntil = null;
            inMemoryJob.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        await db.BackgroundJobs
            .Where(job => job.Id == jobId && job.TenantId == tenantId && job.Status == BackgroundJobStatus.Running && job.WorkerId == workerId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, BackgroundJobStatus.Succeeded)
                .SetProperty(job => job.CompletedAt, DateTimeOffset.UtcNow)
                .SetProperty(job => job.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(job => job.LastError, (string?)null), cancellationToken);
    }

    public async Task FailAsync(Guid tenantId, Guid jobId, string workerId, Exception exception, CancellationToken cancellationToken = default)
    {
        VantaTelemetry.Count(VantaTelemetry.BackgroundJobFailures, "execution_failed");
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(current =>
            current.Id == jobId && current.TenantId == tenantId && current.Status == BackgroundJobStatus.Running && current.WorkerId == workerId, cancellationToken);
        if (job is null) return;

        job.LastError = exception.Message[..Math.Min(exception.Message.Length, 1000)];
        job.LockedUntil = null;
        job.WorkerId = null;
        if (job.Attempts >= job.MaxAttempts)
        {
            job.Status = BackgroundJobStatus.Failed;
        }
        else
        {
            job.Status = BackgroundJobStatus.Pending;
            job.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, Math.Min(job.Attempts, 8)));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogWarning(exception, "Background job {JobId} failed on attempt {Attempt}/{MaxAttempts}.", job.Id, job.Attempts, job.MaxAttempts);
    }
}