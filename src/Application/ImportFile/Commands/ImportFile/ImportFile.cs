using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using ExcelDataReader;

namespace DataMart.Application.ImportFile.Commands.ImportFile;

/// <summary>
/// Response of the upload endpoint: the import runs in the background, so the
/// client receives the batch id immediately and polls for progress.
/// </summary>
public record ImportStarted(int BatchId, int TotalRows);

public record class ImportFileCommand(Stream FileStream, string FileName, int MappingId, EmptyFieldMode EmptyMode = EmptyFieldMode.Throw, int? SheetIndex = null) : IRequest<ImportStarted>
{
    /// <summary>Maximum number of data rows allowed in a single upload. Files exceeding
    /// this are rejected before the heavy processing pass to bound memory and time.</summary>
    public const int MaxRows = 1_100_000;
}

/// <summary>
/// Fast-path handler: copies the upload to a temp file, validates the mapping
/// against the Excel headers, pre-counts rows (for the progress bar), creates
/// the batch and enqueues the job. The heavy streaming import runs in
/// <see cref="ImportFileExecutor"/> (see <see cref="IImportJobQueue"/>).
/// </summary>
public class ImportFileCommandHandler : IRequestHandler<ImportFileCommand, ImportStarted>
{
    private readonly IApplicationDbContext _context;
    private readonly IImportJobQueue _queue;
    private readonly IUser _user;

    public ImportFileCommandHandler(IApplicationDbContext context, IImportJobQueue queue, IUser user)
    {
        _context = context;
        _queue = queue;
        _user = user;
    }

    public async Task<ImportStarted> Handle(ImportFileCommand request, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"datamart_{Guid.NewGuid()}.xlsx");
        string sourceChecksum;
        using (var fs = File.Create(tempPath))
        {
            await request.FileStream.CopyToAsync(fs, cancellationToken);
        }

        // Calculate SHA-256 of the saved file for audit trail (FC-1)
        using (var sha256 = SHA256.Create())
        using (var fs = File.OpenRead(tempPath))
        {
            var hash = await sha256.ComputeHashAsync(fs, cancellationToken);
            sourceChecksum = Convert.ToHexString(hash);
        }

        var mapping = await _context.Mappings.FindAsync([request.MappingId], cancellationToken);

        Guard.Against.NotFound(request.MappingId, mapping);

        var activeMappings = mapping.Columns.Where(c => !string.IsNullOrWhiteSpace(c.TargetColName)).ToList();

        int totalRows;
        try
        {
            // Validation + pre-count pass: cheap (no cell parsing), gives the
            // client an immediate 400 on bad mappings and a TotalRows for the bar.
            using (var readStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            using (var reader = ExcelReaderFactory.CreateOpenXmlReader(readStream))
            {
                ImportFileExecutor.AdvanceToSheet(reader, request.SheetIndex);

                if (!reader.Read())
                    throw new InvalidOperationException("Empty worksheet.");

                var excelHeaders = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    excelHeaders[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;

                var excelHeaderSet = new HashSet<string>(excelHeaders, StringComparer.OrdinalIgnoreCase);

                foreach (var col in activeMappings)
                {
                    if (!excelHeaderSet.Contains(col.ColumnName))
                        throw new AppValidationException(
                            $"Column '{col.ColumnName}' from the mapping was not found in the Excel file.");
                }

                totalRows = 0;
                while (reader.Read())
                {
                    if (++totalRows > ImportFileCommand.MaxRows)
                        throw new AppValidationException(
                            $"File contains more than {ImportFileCommand.MaxRows:N0} data rows; upload rejected.");
                }
            }
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        var batch = new ImportBatch
        {
            FileName = request.FileName,
            AffectedTable = mapping.TargetEntity,
            Status = ImportStatus.Processing,
            TotalRows = totalRows,
            SourceChecksum = sourceChecksum
        };
        
        batch.SetExecutedBy(_user.Id ?? "unknown");

        _context.ImportBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);

        await _queue.EnqueueAsync(
            new ImportJob(tempPath, batch.Id, request.MappingId, request.EmptyMode, request.SheetIndex),
            cancellationToken);

        return new ImportStarted(batch.Id, totalRows);
    }
}
