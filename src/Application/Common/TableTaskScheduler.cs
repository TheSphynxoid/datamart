using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DataMart.Application.Common;

/// <summary>
/// Serializes background work per key (the import's target table) while letting
/// work on different keys run in parallel, bounded by a process-wide concurrency
/// cap. Used by the import job processor: imports to different tables may run
/// concurrently; imports to the same table run strictly in dispatch order
/// (FIFO chain), so a failed import never skips or reorders the next one.
///
/// A queued job only occupies a concurrency slot while it is actually running —
/// waiting behind another job on the same table holds no slot, so one busy table
/// cannot starve the others past the cap.
/// </summary>
public sealed class TableTaskScheduler
{
    private readonly SemaphoreSlim _slots;
    private readonly object _gate = new();
    private readonly Dictionary<string, Task> _tails = new(StringComparer.Ordinal);

    public TableTaskScheduler(int maxConcurrent)
    {
        if (maxConcurrent < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "The concurrency cap must be at least 1.");
        _slots = new SemaphoreSlim(maxConcurrent);
    }

    /// <summary>
    /// Dispatches a work item for <paramref name="key"/>. The returned task
    /// completes when THIS work item finishes (after every earlier work item for
    /// the same key); its exception is the work item's own failure. Callers must
    /// observe the returned task (fault logging) or it goes unobserved.
    /// </summary>
    public Task EnqueueAsync(string key, Func<Task> work)
    {
        lock (_gate)
        {
            var chain = _tails.TryGetValue(key, out var tail)
                ? ChainAsync(tail, work)
                : ChainAsync(Task.CompletedTask, work);
            _tails[key] = chain;
            return chain;
        }
    }

    private async Task ChainAsync(Task predecessor, Func<Task> work)
    {
        // A failed predecessor must not skip the next job for the same table —
        // the dispatcher observes and logs each job's failure separately.
        await predecessor.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        // Slot acquired only at run time: waiting behind another job on the same
        // table does not consume a concurrency slot.
        await _slots.WaitAsync().ConfigureAwait(false);
        try
        {
            await work().ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }
}
