using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common;
using Shouldly;
using Xunit;

namespace DataMart.Application.UnitTests.Common;

/// <summary>
/// Contracts of the per-table import scheduler: same-key FIFO serialization,
/// cross-key concurrency, a global slot cap that waiting jobs do not occupy,
/// and failed jobs not blocking the next one for the same key.
/// </summary>
public class TableTaskSchedulerTests
{
    private sealed class JobRecord
    {
        public long StartTicks;
        public long EndTicks;
    }

    [Fact]
    public async Task SameKey_RunsSerialized_InDispatchOrder()
    {
        var scheduler = new TableTaskScheduler(maxConcurrent: 4);
        var records = new List<JobRecord>();

        var chains = new List<Task>();
        for (var i = 0; i < 5; i++)
        {
            chains.Add(scheduler.EnqueueAsync("ClientAccounts", async () =>
            {
                var record = new JobRecord();
                records.Add(record);
                record.StartTicks = DateTime.UtcNow.Ticks;
                await Task.Delay(30);
                record.EndTicks = DateTime.UtcNow.Ticks;
            }));
        }
        await Task.WhenAll(chains);

        records.Count.ShouldBe(5);
        for (var i = 1; i < records.Count; i++)
        {
            // No overlap between consecutive same-key jobs.
            (records[i].StartTicks >= records[i - 1].EndTicks).ShouldBeTrue($"job {i} started before job {i - 1} finished");
        }
    }

    [Fact]
    public async Task DifferentKeys_RunConcurrently()
    {
        var scheduler = new TableTaskScheduler(maxConcurrent: 4);
        var bothRunning = 0;
        var overlapObserved = false;

        var gateA = new TaskCompletionSource();
        var gateB = new TaskCompletionSource();

        var a = scheduler.EnqueueAsync("ClientAccounts", async () =>
        {
            Interlocked.Increment(ref bothRunning);
            await gateA.Task;
            Interlocked.Decrement(ref bothRunning);
        });
        var b = scheduler.EnqueueAsync("CustomerTransactions", async () =>
        {
            // A holds its table's chain; B must still be able to start.
            Interlocked.Increment(ref bothRunning);
            await gateB.Task;
            overlapObserved = bothRunning > 1;
            Interlocked.Decrement(ref bothRunning);
        });

        // Both chains are running: release them.
        gateB.TrySetResult();
        await Task.Yield();
        gateA.TrySetResult();
        await Task.WhenAll(a, b);

        overlapObserved.ShouldBeTrue();
    }

    [Fact]
    public async Task GlobalCap_BoundsRunningJobs_WaitingJobsHoldNoSlot()
    {
        var scheduler = new TableTaskScheduler(maxConcurrent: 2);
        var running = 0;
        var maxObserved = 0;
        var release = new TaskCompletionSource();

        var chains = new List<Task>();
        for (var i = 0; i < 6; i++)
        {
            chains.Add(scheduler.EnqueueAsync($"table-{i}", async () =>
            {
                var current = Interlocked.Increment(ref running);
                maxObserved = Math.Max(maxObserved, current);
                await release.Task;
                Interlocked.Decrement(ref running);
            }));
        }

        // Two run (the cap); the other four wait without slots. Release one wave,
        // then the rest — the cap must never be exceeded.
        await Task.Yield();
        maxObserved.ShouldBeLessThanOrEqualTo(2);
        release.TrySetResult();
        await Task.WhenAll(chains);
        maxObserved.ShouldBeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task FailedJob_DoesNotBlockTheNextJobForTheSameKey()
    {
        var scheduler = new TableTaskScheduler(maxConcurrent: 4);
        var secondRan = false;

        var first = scheduler.EnqueueAsync("ClientAccounts", () => throw new InvalidOperationException("boom"));
        var second = scheduler.EnqueueAsync("ClientAccounts", async () =>
        {
            secondRan = true;
            await Task.CompletedTask;
        });

        await Should.ThrowAsync<InvalidOperationException>(() => first);
        await second;
        secondRan.ShouldBeTrue();
    }
}
