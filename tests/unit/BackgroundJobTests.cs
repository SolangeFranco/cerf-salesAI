using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Vanta.Api.Data;
using Vanta.Api.Modules.Jobs;
using Xunit;

namespace Vanta.Api.UnitTests;

public sealed class BackgroundJobTests
{
    [Fact]
    public async Task Job_can_be_claimed_and_completed_successfully()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var created = await service.EnqueueAsync(tenantId, "test", "success-1", new { value = 1 });

        var claimed = await service.ClaimNextAsync("test", "worker-1");
        await service.CompleteAsync(tenantId, claimed!.Id, "worker-1");

        var stored = await db.BackgroundJobs.SingleAsync();
        Assert.Equal(created.Id, claimed.Id);
        Assert.Equal(BackgroundJobStatus.Succeeded, stored.Status);
        Assert.Equal(1, stored.Attempts);
    }

    [Fact]
    public async Task Failed_job_is_requeued_with_backoff_and_can_retry()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var created = await service.EnqueueAsync(tenantId, "test", "retry-1", new { }, maxAttempts: 3);
        var firstClaim = await service.ClaimNextAsync("test", "worker-1");

        await service.FailAsync(tenantId, firstClaim!.Id, "worker-1", new InvalidOperationException("temporary"));
        var pending = await db.BackgroundJobs.SingleAsync();
        pending.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var secondClaim = await service.ClaimNextAsync("test", "worker-2");

        Assert.Equal(created.Id, secondClaim!.Id);
        Assert.Equal(2, secondClaim.Attempts);
        Assert.Equal(BackgroundJobStatus.Running, secondClaim.Status);
    }

    [Fact]
    public async Task Duplicate_enqueue_returns_the_original_job()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();

        var first = await service.EnqueueAsync(tenantId, "test", "duplicate-1", new { value = 1 });
        var duplicate = await service.EnqueueAsync(tenantId, "test", "duplicate-1", new { value = 2 });

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Single(await db.BackgroundJobs.ToListAsync());
        Assert.Contains("value", duplicate.Payload);
        Assert.DoesNotContain("2", duplicate.Payload);
    }

    [Fact]
    public async Task Job_becomes_failed_after_max_attempts()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var tenantId = Guid.NewGuid();
        var job = await service.EnqueueAsync(tenantId, "test", "failure-1", new { }, maxAttempts: 1);
        var claimed = await service.ClaimNextAsync("test", "worker-1");

        await service.FailAsync(tenantId, claimed!.Id, "worker-1", new HttpRequestException("permanent"));

        var stored = await db.BackgroundJobs.SingleAsync(current => current.Id == job.Id);
        Assert.Equal(BackgroundJobStatus.Failed, stored.Status);
        Assert.Equal("permanent", stored.LastError);
    }

    [Fact]
    public async Task Tenant_scope_prevents_completing_another_tenants_job()
    {
        await using var db = CreateDb();
        var service = CreateService(db);
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var job = await service.EnqueueAsync(tenantA, "test", "tenant-1", new { });
        var claimed = await service.ClaimNextAsync("test", "worker-1");

        await service.CompleteAsync(tenantB, claimed!.Id, "worker-1");

        var stored = await db.BackgroundJobs.SingleAsync(current => current.Id == job.Id);
        Assert.Equal(BackgroundJobStatus.Running, stored.Status);
        await service.CompleteAsync(tenantA, claimed.Id, "worker-1");
        Assert.Equal(BackgroundJobStatus.Succeeded, (await db.BackgroundJobs.SingleAsync()).Status);
    }

    private static BackgroundJobService CreateService(VantaDbContext db) =>
        new(db, NullLogger<BackgroundJobService>.Instance);

    private static VantaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<VantaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new VantaDbContext(options);
    }
}
