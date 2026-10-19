using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Application.ImportFile.Queries.GetImportReport;
using DataMart.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DataMart.Application.ImportFile.Commands.RunValidation;

/// <summary>
/// Pre-flight validation: parses the file against the saved mapping, applies the
/// same cell-parsing + mandatory-field rules as the real import, and returns the
/// resulting <see cref="ImportReportDto"/>. Nothing is persisted and no
/// <c>ImportBatch</c> is created \u2014 the report is read-only evidence for the user
/// to review before they commit to an import.
/// </summary>
public record class RunValidationCommand(
    Stream FileStream,
    string FileName,
    int MappingId,
    EmptyFieldMode EmptyMode = EmptyFieldMode.Throw,
    int? SheetIndex = null) : IRequest<ImportReportDto>;

public class RunValidationCommandHandler : IRequestHandler<RunValidationCommand, ImportReportDto>
{
    private readonly ImportFileValidator _validator;
    private readonly ILogger<RunValidationCommandHandler> _logger;

    public RunValidationCommandHandler(ImportFileValidator validator, ILogger<RunValidationCommandHandler> logger)
    {
        _validator = validator;
        _logger = logger;
    }

    public async Task<ImportReportDto> Handle(RunValidationCommand request, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"datamart_validate_{Guid.NewGuid()}.xlsx");

        try
        {
            using (var fs = File.Create(tempPath))
            {
                await request.FileStream.CopyToAsync(fs, cancellationToken);
            }

            var result = await _validator.ValidateAsync(
                tempPath, request.MappingId, request.SheetIndex, request.EmptyMode, cancellationToken);

            var errors = result.Records
                .Where(r => r.Status == ImportRowStatus.Rejected)
                .Select(r => new ImportReportError(
                    r.RowNumber ?? 0,
                    r.ErrorMessage ?? string.Empty))
                .OrderBy(e => e.RowNumber)
                .ToList();

            // No ImportBatch was created (validation is a dry-run), so BatchId is null.
            return new ImportReportDto(
                BatchId: null,
                Status: default, // No batch status; a validation report is not a batch.
                TotalRows: result.ImportedCount + result.RejectedCount,
                ImportedRows: result.ImportedCount,
                RejectedRows: result.RejectedCount,
                Errors: errors);
        }
        catch (AppValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed for file {FileName} and mapping {MappingId}.",
                request.FileName, request.MappingId);
            throw;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
