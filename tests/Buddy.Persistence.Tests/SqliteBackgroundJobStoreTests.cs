using Buddy.Core.Domain;

namespace Buddy.Persistence.Tests;

public sealed class SqliteBackgroundJobStoreTests
{
    [Fact]
    public async Task LeasePreventsConcurrentWorkersAndCompleteRequiresOwner()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteBackgroundJobStore jobs = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BackgroundJob pending = NewJob(now);
        await jobs.EnqueueAsync(pending);

        BackgroundJob? leased = await jobs.TryLeaseNextAsync("worker-a", TimeSpan.FromMinutes(1), now);
        BackgroundJob? blocked = await jobs.TryLeaseNextAsync("worker-b", TimeSpan.FromMinutes(1), now);

        Assert.NotNull(leased);
        Assert.Equal(BackgroundJobState.Running, leased.State);
        Assert.Equal(1, leased.AttemptCount);
        Assert.Equal("worker-a", leased.LeaseOwner);
        Assert.Null(blocked);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => jobs.CompleteAsync(pending.Id, "worker-b"));
        await jobs.CompleteAsync(pending.Id, "worker-a");
    }

    [Fact]
    public async Task ExpiredLeaseCanBeReclaimed()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteBackgroundJobStore jobs = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BackgroundJob pending = NewJob(now);
        await jobs.EnqueueAsync(pending);

        Assert.NotNull(await jobs.TryLeaseNextAsync("worker-a", TimeSpan.FromSeconds(10), now));
        BackgroundJob? reclaimed =
            await jobs.TryLeaseNextAsync("worker-b", TimeSpan.FromMinutes(1), now.AddSeconds(11));

        Assert.NotNull(reclaimed);
        Assert.Equal("worker-b", reclaimed.LeaseOwner);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Fact]
    public async Task FailedJobWaitsUntilRetryTime()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteBackgroundJobStore jobs = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BackgroundJob pending = NewJob(now);
        await jobs.EnqueueAsync(pending);
        Assert.NotNull(await jobs.TryLeaseNextAsync("worker-a", TimeSpan.FromMinutes(1), now));

        DateTimeOffset retryAt = now.AddMinutes(2);
        await jobs.FailAsync(
            pending.Id,
            "worker-a",
            "network.timeout",
            "The provider timed out.",
            retryAt,
            maximumAttempts: 3);

        Assert.Null(await jobs.TryLeaseNextAsync("worker-a", TimeSpan.FromMinutes(1), now.AddMinutes(1)));
        BackgroundJob? retry =
            await jobs.TryLeaseNextAsync("worker-b", TimeSpan.FromMinutes(1), retryAt);
        Assert.NotNull(retry);
        Assert.Equal(2, retry.AttemptCount);
    }

    [Fact]
    public async Task LeaseCanBeRenewedAndReleasedForShutdown()
    {
        await using TemporaryBuddyStore store = await TemporaryBuddyStore.CreateAsync();
        SqliteBackgroundJobStore jobs = new(store.Connections);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BackgroundJob pending = NewJob(now);
        await jobs.EnqueueAsync(pending);
        Assert.NotNull(await jobs.TryLeaseNextAsync(
            "worker-a",
            TimeSpan.FromSeconds(10),
            now));

        Assert.True(await jobs.RenewLeaseAsync(
            pending.Id,
            "worker-a",
            now.AddMinutes(2)));
        Assert.Null(await jobs.TryLeaseNextAsync(
            "worker-b",
            TimeSpan.FromMinutes(1),
            now.AddSeconds(20)));

        await jobs.ReleaseAsync(pending.Id, "worker-a", now.AddSeconds(21));
        BackgroundJob? resumed = await jobs.TryLeaseNextAsync(
            "worker-b",
            TimeSpan.FromMinutes(1),
            now.AddSeconds(21));
        Assert.NotNull(resumed);
        Assert.Equal("worker-b", resumed.LeaseOwner);
    }

    private static BackgroundJob NewJob(DateTimeOffset now)
    {
        return new BackgroundJob(
            Guid.NewGuid(),
            null,
            BackgroundJobType.Transcribe,
            "{}",
            BackgroundJobState.Pending,
            0,
            now,
            now,
            null,
            null,
            null,
            null);
    }
}
