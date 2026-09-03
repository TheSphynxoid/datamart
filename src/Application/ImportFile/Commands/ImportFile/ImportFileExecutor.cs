using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataMart.Application.ImportFile.Commands.ImportFile;

/// <summary>
    /// Executes a queued import job: streams the temp file with ExcelDataReader,
    /// applies mapping + format options, bulk-inserts via Npgsql COPY and updates
    /// the batch status. Runs inside a DI scope created by the hosting service.
    /// </summary>
    public class ImportFileExecutor
    {
        private readonly IBulkImporter _importer;
        private readonly IApplicationDbContext _context;
        private readonly IEntitySchemaService _entitySchemaService;
        private readonly ILogger<ImportFileExecutor> _logger;

        public ImportFileExecutor(IBulkImporter importer, IApplicationDbContext context, IEntitySchemaService entitySchemaService, ILogger<ImportFileExecutor> logger)
        {
            _importer = importer;
            _context = context;
            _entitySchemaService = entitySchemaService;
            _logger = logger;
        }



    // Control totals for reconciliation (FC-1)
    internal sealed class ColumnStats
    {
        public long Count = 0;
        public decimal? Sum = null;
        public decimal? Min = null;
        public decimal? Max = null;
        public DateTime? MinDate = null;
        public DateTime? MaxDate = null;
        public ImportColumnType Type;
        public string ColumnName = string.Empty;
    }

    internal static void AdvanceToSheet(IExcelDataReader reader, int? sheetIndex)
    {
        // ExcelDataReader is forward-only and exposes sheets as sequential result
        // sets, so the only way to reach a sheet is to skip forward with NextResult.
        if (sheetIndex is > 0)
        {
            for (int s = 0; s < sheetIndex.Value; s++)
            {
                if (!reader.NextResult())
                    throw new InvalidOperationException($"Worksheet index {sheetIndex} not found in the workbook.");
            }
        }
    }

    public async Task ExecuteAsync(ImportJob job, CancellationToken cancellationToken)
    {
        var batch = await _context.ImportBatches.FindAsync([job.BatchId], cancellationToken);
        Guard.Against.NotFound(job.BatchId, batch);

        try
        {
            using (var readStream = new FileStream(job.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
            using (var reader = ExcelReaderFactory.CreateOpenXmlReader(readStream))
            {
                AdvanceToSheet(reader, job.SheetIndex);

                if (!reader.Read())
                    throw new InvalidOperationException("Empty worksheet.");

                var excelHeaders = new string[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    excelHeaders[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;

                var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < excelHeaders.Length; i++)
                    columnIndex[excelHeaders[i]] = i;

                var mapping = await _context.Mappings.FindAsync([job.MappingId], cancellationToken);
                Guard.Against.NotFound(job.MappingId, mapping);

                var activeMappings = mapping.Columns.Where(c => !string.IsNullOrWhiteSpace(c.TargetColName)).ToList();

                var schema = _entitySchemaService.GetEntityList();
                var entitySchema = schema.FirstOrDefault(e => e.Name == mapping.TargetEntity)
                    ?? throw new InvalidOperationException($"Entity '{mapping.TargetEntity}' not found.");

                var fieldTypes = entitySchema.Properties!
                    .ToDictionary(p => p.Name!, p => p.DataType ?? "String", StringComparer.OrdinalIgnoreCase);

                var requiredFields = entitySchema.Properties!
                    .Where(p => !p.IsNullable && !p.IsPrimary)
                    .Select(p => p.Name!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Merge user-declared mandatory (obligatoire) mapping columns with the
                // entity's non-nullable columns: both force a value on every row.
                var requiredColumns = new HashSet<string>(requiredFields, StringComparer.OrdinalIgnoreCase);
                foreach (var am in activeMappings)
                {
                    if (am.IsRequired)
                        requiredColumns.Add(am.TargetColName);
                }
                var columns = new List<ImportColumn>();
                var columnStats = new List<ColumnStats>();

                foreach (var col in activeMappings)
                {
                    var clrType = fieldTypes.TryGetValue(col.TargetColName, out var t) ? t : "String";
                    var importType = ImportCellParser.MapType(clrType);
                    columns.Add(new ImportColumn(col.TargetColName, importType));
                    columnStats.Add(new ColumnStats { ColumnName = col.TargetColName, Type = importType });
                }

                columns.Add(new ImportColumn("ImportBatchId", ImportColumnType.Integer));
                columns.Add(new ImportColumn("SourceKey", ImportColumnType.Text));

                int batchIdIndex = activeMappings.Count;
                int sourceKeyIndex = activeMappings.Count + 1;
                int rowNumber = 1;
                int skippedCount = 0;
                var skippedRows = new List<int>();
                var importRecords = new List<ImportRecord>();

                IEnumerable<ImportRow> ReadRows()
                {
                    while (reader.Read())
                    {
                        var values = new object?[columns.Count];
                        bool skipRow = false;
                        string? rowError = null;

                        for (int i = 0; i < activeMappings.Count; i++)
                        {
                            var col = activeMappings[i];
                            int excelIdx = columnIndex[col.ColumnName];
                            var cell = reader.IsDBNull(excelIdx) ? null : reader.GetValue(excelIdx);
                            // Parse errors are data errors, not "empty" choices: they are always
                            // recorded as row-level rejections and the batch continues (so the
                            // import mirrors the pre-flight validator's outcome for the same
                            // bad-type row). EmptyMode.Throw still aborts on empty REQUIRED
                            // fields below — that is its intended semantics.
                            try
                            {
                                values[i] = ImportCellParser.Parse(cell, col, columns[i].DbType);
                            }
                            catch (AppValidationException ex)
                            {
                                rowError ??= $"Row {rowNumber}: {ex.Message}";
                                skipRow = true;
                                break; // Row is rejected; skip parsing the remaining columns.
                            }

                            // Accumulate stats for reconciliation (FC-1)
                            var stats = columnStats[i];
                            if (values[i] != null)
                            {
                                stats.Count++;
                                UpdateStats(stats, values[i]);
                            }

                            if (ImportCellParser.IsEmpty(values[i]) && requiredColumns.Contains(col.TargetColName))
                            {
                                switch (job.EmptyMode)
                                {
                                    case EmptyFieldMode.Throw:
                                        throw new AppValidationException(
                                            $"Row {rowNumber}: required field '{col.TargetColName}' is empty.");
                                    case EmptyFieldMode.SkipRow:
                                        skipRow = true;
                                        rowError ??= $"Row {rowNumber}: required field '{col.TargetColName}' is empty";
                                        break;
                                    case EmptyFieldMode.Default:
                                        values[i] = ImportCellParser.GetDefault(columns[i].DbType);
                                        break;
                                }
                            }
                        }
                        // DIAG (values omitted: out-of-range when iterating past activeMappings.Count; rawCells above suffice).


                         // Row-level journal (FC-4): JSON snapshot of the mapped values plus
                        // the outcome, so the import report / a future dashboard can present it.
                        var payload = new Dictionary<string, object?>();
                        for (int i = 0; i < activeMappings.Count; i++)
                            payload[activeMappings[i].TargetColName] = values[i];

                        importRecords.Add(new ImportRecord
                        {
                            ImportBatchId = batch.Id,
                            SourceKey = rowNumber.ToString(),
                            Status = skipRow ? ImportRowStatus.Rejected : ImportRowStatus.Imported,
                            ErrorMessage = rowError,
                            Payload = JsonSerializer.Serialize(payload),
                        });

                        if (skipRow)
                        {
                            skippedCount++;
                            skippedRows.Add(rowNumber);
                            rowNumber++;
                            continue;
                        }

                        values[batchIdIndex] = batch.Id;
                        values[sourceKeyIndex] = rowNumber.ToString();
                        rowNumber++;

                        yield return new ImportRow { Values = values };
                    }
                }

                async Task UpdateProgressAsync(int processed)
                {
                    batch.ProcessedRows = processed;
                    await _context.SaveChangesAsync(cancellationToken);
                }

                // Materialise the parsed rows into an in-memory list BEFORE the retry loop.
                // ReadRows() is a forward-only generator over IExcelDataReader; if the COPY
                // fails transiently and the importer enumerates `rows` a second time, the
                // reader is already at EOF and yields 0 rows — the COPY would then commit
                // an empty import and the batch would be marked Success (the silent
                // 0-row retry bug). Materialising once gives ImportWithRetryAsync a
                // re-iterable, deterministic source of rows. The journal, stats, and
                // skipped-rows state are populated as a side effect of this single
                // enumeration; the retry's re-enumeration only re-walks the materialised
                // list for the COPY. (Memory: bounded by the 1,100,000-row cap added in
                // the security/polish step.)
                var rows = ReadRows().ToList();

                await ImportWithRetryAsync(
                    mapping.TargetEntity,
                    columns,
                    rows,
                    batch.Id,
                    cancellationToken,
                    UpdateProgressAsync);

                // Build control totals JSON (FC-1)
                var controlTotals = new
                {
                    RowCount = rowNumber - 1 - skippedCount,
                    ColumnStats = columnStats.Select(s => new
                    {
                        Column = s.ColumnName,
                        Type = s.Type.ToString(),
                        Count = s.Count,
                        Sum = s.Sum,
                        Min = s.Min,
                        Max = s.Max,
                        MinDate = s.MinDate?.ToString("O"),
                        MaxDate = s.MaxDate?.ToString("O")
                    }).ToArray()
                };

                batch.SetControlTotals(JsonSerializer.Serialize(controlTotals));
                batch.Status = ImportStatus.Success;
                batch.ProcessedRows = rowNumber - 1;
                batch.SkippedRows = skippedCount;
                batch.SkippedRowsPreview = skippedRows.Count > 0
                    ? string.Join(", ", skippedRows.Take(10)) + (skippedRows.Count > 10 ? "..." : "")
                    : null;
                // JournalRowsWritten stays at 0 until the post-COPY journal upload
                // catches up. The dashboard uses (Status=Success, JournalRowsWritten
                // < TotalRows) to show the "Journal uploading" pill.
                batch.JournalRowsWritten = 0;

                // Terminal status write uses CancellationToken.None: even if the
                // BackgroundService stoppingToken is already cancelled (Ctrl+C,
                // container stop, deploy), the batch MUST land in Success so the
                // dashboard never reports a stuck Processing batch.
                await _context.SaveChangesAsync(CancellationToken.None);

                // Persist the row-level journal in chunks after the COPY committed,
                // so a failed import leaves zero journal rows (consistent with "no
                // partial data"). A journal-write failure must not turn a successful
                // import into a failure.
                //
                // Chunked to keep EF's change tracker bounded: tracking 1M+ entities
                // in a single SaveChanges stalls the change tracker and generates
                // multi-MB batched INSERTs that can hang the pipeline. 5,000 per
                // chunk keeps each round-trip small (~hundreds of KB of payload) and
                // the change tracker memory bounded.
                //
                // The import is already Success from the user's perspective (target
                // table is committed). The journal uploads in the background; each
                // chunk bumps JournalRowsWritten so the dashboard can show the
                // "Journal uploading" pill progress and hide it when the catch-up
                // is complete.
                const int journalChunkSize = 5_000;
                for (int offset = 0; offset < importRecords.Count; offset += journalChunkSize)
                {
                    var chunkSize = Math.Min(journalChunkSize, importRecords.Count - offset);
                    var chunk = importRecords.GetRange(offset, chunkSize);
                    try
                    {
                        await _context.ImportRecords.AddRangeAsync(chunk, CancellationToken.None);
                        await _context.SaveChangesAsync(CancellationToken.None);
                        batch.JournalRowsWritten = offset + chunkSize;
                        await _context.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: a missing journal is logged but does not roll
                        // back the imported rows. The import is logically Success.
                        // Mark JournalIncomplete so the dashboard surfaces the audit gap
                        // (the remaining rows cannot be re-derived — the source file
                        // is already deleted by the finally block below).
                        _logger.LogWarning(ex, "Failed to write import journal chunk (offset {Offset}, size {ChunkSize}) for batch {BatchId}.", offset, chunkSize, batch.Id);
                        batch.JournalIncomplete = true;
                        try
                        {
                            await _context.SaveChangesAsync(CancellationToken.None);
                        }
                        catch (Exception saveEx)
                        {
                            _logger.LogWarning(saveEx, "Failed to persist JournalIncomplete flag for batch {BatchId}.", batch.Id);
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed for batch {BatchId}.", batch.Id);
            batch.Status = ImportStatus.Failed;
            // Same reason as above — the batch MUST land in Failed even on shutdown.
            // Last-resort: if even the terminal save fails (e.g. DB is gone), log
            // loudly so the stale-batch sweep on next startup can take over.
            try
            {
                await _context.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist Failed status for batch {BatchId}; the next start-up sweep will reconcile.", batch.Id);
            }
            throw;
        }
        finally
        {
            if (File.Exists(job.TempPath))
                File.Delete(job.TempPath);
        }
    }

    private async Task ImportWithRetryAsync(
        string tableName,
        IReadOnlyList<ImportColumn> columns,
        IEnumerable<ImportRow> rows,
        int batchId,
        CancellationToken cancellationToken,
        Func<int, Task>? onProgress)
    {
        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(2);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await _importer.ImportAsync(tableName, columns, rows, batchId, cancellationToken, onProgress);
                return;
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < 3)
            {
                var delay = baseDelay * (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "Transient error during import (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms | BatchId={BatchId}",
                    attempt + 1, maxRetries, delay.TotalMilliseconds, batchId);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        DbException dx => dx.Message.Contains("57P01") || dx.Message.Contains("40001"), // admin_shutdown, serialization_failure
        TimeoutException => true,
        TaskCanceledException => true,
        _ => false
    };

    internal static void UpdateStats(ColumnStats stats, object? value)
    {
        if (value == null) return;

        stats.Count++;

        switch (stats.Type)
        {
            case ImportColumnType.Integer:
                if (value is int iv)
                {
                    stats.Sum ??= 0;
                    stats.Sum += iv;
                    stats.Min = stats.Min.HasValue ? Math.Min(stats.Min.Value, iv) : iv;
                    stats.Max = stats.Max.HasValue ? Math.Max(stats.Max.Value, iv) : iv;
                }
                break;
            case ImportColumnType.Decimal:
                if (value is decimal dv)
                {
                    stats.Sum ??= 0;
                    stats.Sum += dv;
                    stats.Min = stats.Min.HasValue ? Math.Min(stats.Min.Value, dv) : dv;
                    stats.Max = stats.Max.HasValue ? Math.Max(stats.Max.Value, dv) : dv;
                }
                break;
            case ImportColumnType.Date:
            case ImportColumnType.Timestamp:
                if (value is DateTime dtv)
                {
                    stats.MinDate = stats.MinDate.HasValue ? (dtv < stats.MinDate.Value ? dtv : stats.MinDate.Value) : dtv;
                    stats.MaxDate = stats.MaxDate.HasValue ? (dtv > stats.MaxDate.Value ? dtv : stats.MaxDate.Value) : dtv;
                }
                break;
        }
    }
}
