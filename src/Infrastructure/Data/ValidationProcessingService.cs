using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Application.ImportFile.Queries.GetImportReport;
using DataMart.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// Drains the <see cref="IValidationJobQueue"/> one job at a time. For each
/// job it wires a throttled progress callback to
/// <see cref="IValidationProgressStore"/>, runs
/// <see cref="ImportFileValidator.ValidateAsync"/>, then stores the
/// resulting <see cref="ImportReportDto"/> in the store. The temp file is
/// always deleted in <c>finally</c>.
///
/// In-flight validations are lost on process restart (the store is in-memory);
/// users re-trigger the validation, same as the existing import path which
/// marks orphaned <see cref="ImportStatus.Processing"/> batches as Failed.
/// </summary>
public sealed class ValidationProcessingService : BackgroundService
{
    private readonly IValidationJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IValidationProgressStore _store;
    private readonly ILogger<ValidationProcessingService> _logger;

    public ValidationProcessingService(
        IValidationJobQueue queue,
        IServiceScopeFactory scopeFactory,
        IValidationProgressStore store,
        ILogger<ValidationProcessingService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            if (job is null) continue;

            try
            {
                await ProcessAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation job {ValidationId} failed.", job.ValidationId);
                _store.MarkFailed(job.ValidationId, ex.Message);
            }
        }
    }

    private async Task ProcessAsync(ValidationJob job, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var validator = scope.ServiceProvider.GetRequiredService<ImportFileValidator>();

            // The pre-check (mapping lookup, header validation, row cap) has
            // already been done by StartValidationCommand. The store has the
            // totalRows from the count pass, so the validator can throttle
            // its progress callback to ~1% increments.
            var snapshot = _store.TryGet(job.ValidationId, out var s) ? s : null;
            var totalRows = snapshot?.Total ?? 0;
            _store.Update(job.ValidationId, 0); // ensure 0/totalRows visible immediately

            var progress = new Progress<int>(processed => _store.Update(job.ValidationId, processed));

            var result = await validator.ValidateAsync(
                job.TempPath,
                job.MappingId,
                job.SheetIndex,
                job.EmptyMode,
                cancellationToken,
                progress,
                totalRows);

            var errors = result.Records
                .Where(r => r.Status == ImportRowStatus.Rejected)
                .Select(r => new ImportReportError(
                    r.RowNumber ?? 0,
                    r.ErrorMessage ?? string.Empty))
                .OrderBy(e => e.RowNumber)
                .ToList();

            var report = new ImportReportDto(
                BatchId: null,
                Status: default,
                TotalRows: result.ImportedCount + result.RejectedCount,
                ImportedRows: result.ImportedCount,
                RejectedRows: result.RejectedCount,
                Errors: errors);

            _store.MarkDone(job.ValidationId, report);
        }
        finally
        {
            try
            {
                if (File.Exists(job.TempPath))
                    File.Delete(job.TempPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp file {TempPath} for validation {ValidationId}.",
                    job.TempPath, job.ValidationId);
            }
        }
    }
}
