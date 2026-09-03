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
/// On startup, batches left in <see cref="ImportStatus.Processing"/> by a
/// previous (crashed) run are marked <see cref="ImportStatus.Failed"/> so
/// they can never be reported as in-flight.
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

            var stale = await context.ImportBatches
                .Where(b => b.Status == ImportStatus.Processing)
                .ToListAsync(cancellationToken);

            foreach (var batch in stale)
                batch.Status = ImportStatus.Failed;

            if (stale.Count > 0)
                await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: a stale-batch sweep must never prevent the
            // API from starting (e.g. DB briefly unavailable at boot).
            _logger.LogWarning(ex, "Could not mark stale import batches as failed at startup.");
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