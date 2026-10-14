using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DataMart.Application.ImportFile.Commands.RunValidation;

/// <summary>
/// Starts an async dry-run validation: saves the upload to a temp file,
/// pre-counts rows (for the progress bar denominator), registers the
/// validation id in the progress store and enqueues the work. Returns
/// immediately with the <see cref="ValidationStarted"/>; the caller polls
/// for progress via <c>GetValidationStatusQuery</c>.
///
/// This path is used by the HTTP endpoint so the user gets real progress
/// on multi-million-row files. The existing synchronous
/// <see cref="RunValidationCommand"/> stays in place for direct callers
/// (notably the functional-test suite) that want the report inline.
/// </summary>
public record class StartValidationCommand(
    Stream FileStream,
    string FileName,
    int MappingId,
    EmptyFieldMode EmptyMode = EmptyFieldMode.Throw,
    int? SheetIndex = null) : IRequest<ValidationStarted>;

public record ValidationStarted(Guid ValidationId, int TotalRows);

public class StartValidationCommandHandler : IRequestHandler<StartValidationCommand, ValidationStarted>
{
    private readonly IApplicationDbContext _context;
    private readonly IValidationProgressStore _store;
    private readonly IValidationJobQueue _queue;
    private readonly ILogger<StartValidationCommandHandler> _logger;

    public StartValidationCommandHandler(
        IApplicationDbContext context,
        IValidationProgressStore store,
        IValidationJobQueue queue,
        ILogger<StartValidationCommandHandler> logger)
    {
        _context = context;
        _store = store;
        _queue = queue;
        _logger = logger;
    }

    public async Task<ValidationStarted> Handle(StartValidationCommand request, CancellationToken cancellationToken)
    {
        var validationId = Guid.NewGuid();
        var tempPath = Path.Combine(Path.GetTempPath(), $"datamart_validate_{validationId}.xlsx");

        try
        {
            using (var fs = File.Create(tempPath))
            {
                await request.FileStream.CopyToAsync(fs, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save uploaded file for validation {ValidationId}.", validationId);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        // Fail fast on a bad mapping id / missing columns / oversized file
        // before we hand the work off. The row count returned is the
        // denominator the validator's progress callback will use.
        var mapping = await _context.Mappings.FindAsync([request.MappingId], cancellationToken);
        if (mapping is null)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw new Ardalis.GuardClauses.NotFoundException($"Mapping {request.MappingId} not found.",
                request.MappingId.ToString());
        }

        var requiredColumns = mapping.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.TargetColName))
            .Select(c => c.ColumnName)
            .ToList();

        int totalRows;
        try
        {
            totalRows = ImportRowCounter.Count(tempPath, request.SheetIndex, requiredColumns, cancellationToken);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        // From here the store owns the validation entry and the job queue will own
        // the temp file after a SUCCESSFUL enqueue — so a failure in either must
        // both delete the temp file and close the store entry (otherwise the SPA
        // polls a validation that will never finish).
        try
        {
            _store.Register(validationId, totalRows);

            await _queue.EnqueueAsync(
                new ValidationJob(validationId, tempPath, request.MappingId, request.EmptyMode, request.SheetIndex),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _store.MarkFailed(validationId, ex.Message);
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }

        return new ValidationStarted(validationId, totalRows);
    }
}
