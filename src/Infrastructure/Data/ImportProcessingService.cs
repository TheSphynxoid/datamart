using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// Processes queued import jobs one at a time, each in its own DI scope.
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

    public ImportProcessingService(IImportJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<ImportProcessingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // 1. Stale Processing batches → Failed.
            var stale = await context.ImportBatches
                .Where(b => b.Status == ImportStatus.Processing)
                .ToListAsync(cancellationToken);

            foreach (var batch in stale)
            {
                batch.Status = ImportStatus.Failed;
                batch.FailedReason = "Interrupted: the process stopped during processing (reconciled at startup).";
            }

            // 2. Success batches with a partial journal → JournalIncomplete = true.
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
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            if (job is null)
                continue;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var executor = scope.ServiceProvider.GetRequiredService<ImportFileExecutor>();
                await executor.ExecuteAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import job for batch {BatchId} failed.", job.BatchId);
            }
        }
    }
}