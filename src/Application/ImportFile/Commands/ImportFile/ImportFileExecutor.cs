using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Diagnostics;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Common;
using DataMart.Application.EntityRules.Common;
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
        private readonly ITransientErrorDetector _transientErrorDetector;
        private readonly IImportJournalWriter _journalWriter;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<ImportFileExecutor> _logger;

        // Journal JSON is audit-read (dashboards, downloads): camelCase keys and
        // string enums keep snapshots human-readable without a decoder ring.
        private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public ImportFileExecutor(IBulkImporter importer, IApplicationDbContext context, IEntitySchemaService entitySchemaService, ITransientErrorDetector transientErrorDetector, IImportJournalWriter journalWriter, TimeProvider timeProvider, ILogger<ImportFileExecutor> logger)
        {
            _importer = importer;
            _context = context;
            _entitySchemaService = entitySchemaService;
            _transientErrorDetector = transientErrorDetector;
            _journalWriter = journalWriter;
            _timeProvider = timeProvider;
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
                    throw new AppValidationException("Empty worksheet.");

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

                // Per-entity validation rules: loaded once per run so a rule edited
                // mid-flight cannot change the verdict halfway through.
                var rules = await _context.EntityValidationRules
                    .AsNoTracking()
                    .Where(r => r.TargetEntity == mapping.TargetEntity && r.IsActive)
                    .ToListAsync(cancellationToken);

                var nowUtc = _timeProvider.GetUtcNow();

                // Journal framework tier-1 (spec §3): snapshot the effective config
                // at import time — a copy, not an FK — so "import always ran on the
                // config the user saw validated" is provable after the fact. MarkStarted
                // is set-once and enforces the snapshot size cap: over the cap it throws
                // and nothing is stored, and the catch below lands the batch in Failed
                // with the cap as the reason (snapshots are never stored truncated).
                // Calling it before column resolution keeps the snapshot even when
                // schema drift fails the run — exactly the config that caused it.
                var mappingSnapshotJson = JsonSerializer.Serialize(new
                {
                    mapping.Id,
                    mapping.Name,
                    mapping.TargetEntity,
                    Columns = mapping.Columns
                }, JournalJsonOptions);
                var rulesSnapshotJson = JsonSerializer.Serialize(rules.Select(r => new
                {
                    r.Id,
                    r.TargetEntity,
                    r.Field,
                    r.Operator,
                    r.ValueSource,
                    r.Value,
                    r.Values,
                    r.Function,
                    r.FunctionArg,
                    r.Message,
                    r.IsActive
                }), JournalJsonOptions);
                batch.MarkStarted(mappingSnapshotJson, rulesSnapshotJson);

                var columns = new List<ImportColumn>();
                var columnStats = new List<ColumnStats>();

                foreach (var col in activeMappings)
                {
                    // Unknown target = schema drift between the saved mapping and the
                    // current entity. Fail loudly (both paths identically) instead of
                    // silently writing the column as Text.
                    if (!fieldTypes.TryGetValue(col.TargetColName, out var t))
                        throw new AppValidationException(
                            $"Column '{col.TargetColName}' does not exist on entity '{mapping.TargetEntity}'.");
                    var importType = ImportCellParser.MapType(t);
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
                        cancellationToken.ThrowIfCancellationRequested();

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
                                // Row is rejected; skip the stats/required checks for this
                                // column but keep parsing the remaining ones, exactly like
                                // the pre-flight validator — the payload snapshots and error
                                // lists of both paths must stay comparable.
                                continue;
                            }

                            // Accumulate stats for reconciliation (FC-1).
                            // UpdateStats is the single writer for Count — do not
                            // increment it here as well (the double-count bug).
                            var stats = columnStats[i];
                            if (values[i] != null)
                            {
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

                         // Row-level journal (FC-4): JSON snapshot of the mapped values plus
                        // the outcome, so the import report / a future dashboard can present it.
                        var payload = new Dictionary<string, object?>();
                        for (int i = 0; i < activeMappings.Count; i++)
                            payload[activeMappings[i].TargetColName] = values[i];

                        foreach (var failure in RuleEvaluator.ApplyRules(rules, fieldTypes, payload, mapping.TargetEntity, nowUtc))
                        {
                            rowError = rowError is null
                                ? $"Row {rowNumber}: {failure.Message}"
                                : $"{rowError}; {failure.Message}";
                            skipRow = true;
                        }

                        importRecords.Add(new ImportRecord
                        {
                            ImportBatchId = batch.Id,
                            SourceKey = rowNumber.ToString(),
                            // Typed 1-based source-file row (journal framework tier 2);
                            // SourceKey stays as the write-once provenance string.
                            RowNumber = rowNumber,
                            Status = skipRow ? ImportRowStatus.Rejected : ImportRowStatus.Imported,
                            // The column is capped at 2000 (EF configuration); rule
                            // failures concatenate unbounded, so an over-long message
                            // would fail the journal chunk write and flag the whole
                            // batch JournalIncomplete for a cosmetic reason.
                            ErrorMessage = Truncate(rowError, ImportRecord.MaxErrorMessageLength),
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

                var writeStats = await ImportWithRetryAsync(
                    mapping.TargetEntity,
                    columns,
                    rows,
                    batch.Id,
                    cancellationToken,
                    UpdateProgressAsync);

                // Tier-1 fact through the writer seam: bracket the target-table IDs
                // this COPY created (audit/revert scoping — raw material for the
                // future revert/compensation seam).
                await _journalWriter.UpdateImpactedIdRangeAsync(batch, writeStats);

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
                batch.Complete();
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
                // The import is already Success from the user's perspective (target
                // table is committed). The journal uploads in the background; each
                // chunk bumps JournalRowsWritten so the dashboard can show the
                // "Journal uploading" pill progress and hide it when the catch-up
                // is complete. All of that lives behind the IImportJournalWriter
                // seam (journal framework spec §5.3): future journal facts extend
                // the writer, not this pipeline.
                await _journalWriter.WriteJournalChunksAsync(batch, importRecords);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import failed for batch {BatchId}.", batch.Id);
            // Failed batches have no row journal (the journal is written only
            // after a successful COPY), so the failure motif is stored on the
            // batch itself: what the wizard and the dashboard surface.
            batch.Fail(_transientErrorDetector.DescribeFailure(ex));
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

    private async Task<ImportWriteStats> ImportWithRetryAsync(
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
                return await _importer.ImportAsync(tableName, columns, rows, batchId, cancellationToken, onProgress);
            }
            catch (Exception ex) when (_transientErrorDetector.IsTransient(ex) && attempt < 3)
            {
                var delay = baseDelay * (int)Math.Pow(2, attempt);
                _logger.LogWarning(ex, "Transient error during import (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms | BatchId={BatchId}",
                    attempt + 1, maxRetries, delay.TotalMilliseconds, batchId);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    internal static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

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
