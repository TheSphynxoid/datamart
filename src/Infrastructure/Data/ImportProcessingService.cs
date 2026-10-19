using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// Processes queued import jobs, each in its own DI scope. Imports to DIFFERENT
/// tables run concurrently (bounded by <c>Imports:MaxConcurrent</c>, default 2 —
/// each running import holds a COPY connection and its materialized rows in
/// memory); imports to the same table run strictly in enqueue order.
///
/// On startup, two reconciliations run:
///   1. Batches left in <see cref="ImportStatus.Processing"/> by a previous
///      (crashed) run are marked <see cref="ImportStatus.Failed"/> so they
///      can never be reported as in-flight.
///   2. Success batches whose journal upload was killed mid-flight (i.e.
///      <c>JournalRowsWritten &lt; TotalRows</c>) are marked
///      <c>JournalIncomplete = true</c>. The remaining rows cannot be
///      recovered (the source Excel file is already deleted), so the
///      <c>JournalIncomplete</c> flag is the audit-visible marker.
/// </summary>
public sealed class ImportProcessingService : BackgroundService
{
    private readonly IImportJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportProcessingService> _logger;
    private readonly TableTaskScheduler _scheduler;
    private readonly object _pendingGate = new();
    private readonly HashSet<Task> _pending = [];

    public ImportProcessingService(IImportJobQueue queue, IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ImportProcessingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scheduler = new TableTaskScheduler(MaxConcurrentImports(configuration));
    }

    private static int MaxConcurrentImports(IConfiguration configuration)
    {
        // Each running import holds a COPY connection + its materialized rows in
        // memory (bounded by the 1.1M-row cap), so the cap bounds memory too.
        // Default 2: two heavy imports overlap without doubling the worst case.
        return configuration.GetValue<int?>("Imports:MaxConcurrent") ?? 2;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Stale Processing batches -> Failed.
            var stale = await context.ImportBatches
                .Where(b => b.Status == ImportStatus.Processing)
                .ToListAsync(cancellationToken);

            foreach (var batch in stale)
            {
                batch.Fail("Interrupted: the process stopped during processing (reconciled at startup).");
            }

            // 2. Success batches with a partial journal -> JournalIncomplete = true.
            // The source file is already deleted, so the missing rows cannot be
            // re-derived. The flag is the only honest audit signal we can produce.
            var partial = await context.ImportBatches
                .Where(b => b.Status == ImportStatus.Success
                            && b.JournalRowsWritten < b.TotalRows
                            && !b.JournalIncomplete)
                .ToListAsync(cancellationToken);

            foreach (var batch in partial)
            {
                batch.JournalIncomplete = true;
                _logger.LogWarning("Batch {BatchId} ({FileName}) ended in Success but the journal only has {Written}/{Total} rows. The remaining rows cannot be re-derived because the source file is deleted. Marking JournalIncomplete=true.",
                    batch.Id, batch.FileName, batch.JournalRowsWritten, batch.TotalRows);
            }

            if (stale.Count > 0 || partial.Count > 0)
                await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: a stale-batch sweep must never prevent the
            // API from starting (e.g. DB briefly unavailable at boot).
            _logger.LogWarning(ex, "Could not reconcile stale import batches at startup.");
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await _queue.DequeueAsync(stoppingToken);
                if (job is null)
                    continue;

                DispatchJob(job, stoppingToken);
            }
        }
        finally
        {
            // Drain on shutdown: in-flight imports finish (or cancel themselves —
            // the executor marks their batches Failed), jobs never started leave
            // their batches to the next startup sweep. Queued-behind jobs whose
            // table turn came after the stop signal skip themselves.
            Task[] drain;
            lock (_pendingGate)
            {
                drain = [.. _pending];
            }
            await Task.WhenAll(drain).ConfigureAwait(false);
        }
    }

    private void DispatchJob(ImportJob job, CancellationToken stoppingToken)
    {
        var chain = _scheduler.EnqueueAsync(job.TargetTable, () => RunJobAsync(job, stoppingToken));
        lock (_pendingGate)
        {
            _pending.Add(chain);
        }
        // Observe faults so nothing goes unobserved; RunJobAsync already logs.
        chain.ContinueWith(t =>
        {
            _logger.LogError(t.Exception?.GetBaseException(), "Import job for batch {BatchId} faulted.", job.BatchId);
        }, TaskContinuationOptions.OnlyOnFaulted);
        chain.ContinueWith(_ =>
        {
            lock (_pendingGate)
            {
                _pending.Remove(chain);
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task RunJobAsync(ImportJob job, CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested)
        {
            // Never started: leave the batch in Processing for the startup sweep
            // (same terminal path as the serial V1 processor's un-reached jobs).
            _logger.LogWarning("Import job for batch {BatchId} was queued but the process stopped before it could run.", job.BatchId);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<ImportFileExecutor>();
            await executor.ExecuteAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Import job for batch {BatchId} was cancelled by shutdown.", job.BatchId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import job for batch {BatchId} failed.", job.BatchId);
        }
    }
}
