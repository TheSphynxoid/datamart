using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Common;
using DataMart.Application.EntityRules.Common;
using DataMart.Domain.Entities;
using DataMart.Domain.Enums;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DataMart.Application.ImportFile.Common;

/// <summary>
/// Result of a pre-flight validation pass over an Excel file against a saved mapping.
/// The records are built in memory and are NOT persisted; they are returned so the
/// caller (e.g. <c>RunValidationCommand</c>) can produce a report and let the user
/// review the error list before committing to an import.
/// </summary>
public record ImportValidationResult(
    List<ImportRecord> Records,
    int ImportedCount,
    int RejectedCount);

/// <summary>
/// Pre-flight validation: opens the file, advances to the selected sheet, applies
/// the same cell-parsing + mandatory-field rules as the import executor (via
/// <see cref="ImportCellParser"/>), and produces an in-memory <see cref="ImportRecord"/>
/// per row with the outcome. No rows are written to the target table; nothing is
/// persisted; the temp file is the caller's responsibility.
/// </summary>
public class ImportFileValidator
{
    private readonly IApplicationDbContext _context;
    private readonly IEntitySchemaService _entitySchemaService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportFileValidator> _logger;

    public ImportFileValidator(
        IApplicationDbContext context,
        IEntitySchemaService entitySchemaService,
        TimeProvider timeProvider,
        ILogger<ImportFileValidator> logger)
    {
        _context = context;
        _entitySchemaService = entitySchemaService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ImportValidationResult> ValidateAsync(
        string tempPath,
        int mappingId,
        int? sheetIndex,
        EmptyFieldMode emptyMode,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null,
        int totalRows = 0)
    {
        var mapping = await _context.Mappings.FindAsync([mappingId], cancellationToken);
        if (mapping is null)
            throw new NotFoundException($"Mapping {mappingId} not found.", mappingId.ToString());

        var activeMappings = mapping.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.TargetColName))
            .ToList();

        var schema = _entitySchemaService.GetEntityList();
        var entitySchema = schema.FirstOrDefault(e => e.Name == mapping.TargetEntity)
            ?? throw new AppValidationException($"Entity '{mapping.TargetEntity}' not found.");

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

        var records = new List<ImportRecord>();
        int importedCount = 0;
        int rejectedCount = 0;
        int rowNumber = 1;

        // Throttled progress: report every ~1% of totalRows (or every 1000 rows
        // for very small files) so the poller sees regular updates without us
        // paying the cost of a callback per row. The store holds Total; we just
        // report the running count.
        var reportEvery = totalRows > 0
            ? Math.Max(1, totalRows / 100)
            : 1000;
        var nextReportAt = reportEvery;

        using var readStream = new FileStream(
            tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var reader = ExcelReaderFactory.CreateOpenXmlReader(readStream);

        ImportFileExecutor.AdvanceToSheet(reader, sheetIndex);

        if (!reader.Read())
            throw new AppValidationException("Empty worksheet.");

        var excelHeaders = new string[reader.FieldCount];
        for (int i = 0; i < reader.FieldCount; i++)
            excelHeaders[i] = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;

        var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < excelHeaders.Length; i++)
            columnIndex[excelHeaders[i]] = i;

        // Header validation: every mapping column must exist in the file.
        var excelHeaderSet = new HashSet<string>(excelHeaders, StringComparer.OrdinalIgnoreCase);
        foreach (var col in activeMappings)
        {
            if (!excelHeaderSet.Contains(col.ColumnName))
                throw new AppValidationException(
                    $"Column '{col.ColumnName}' from the mapping was not found in the Excel file.");
        }

        // Build a per-column import type map once (mirrors the executor's setup).
        // Unknown target = schema drift; fail loudly instead of silently parsing
        // the column as Text (executor does the same).
        var columnImportType = new Dictionary<string, ImportColumnType>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in activeMappings)
        {
            if (!fieldTypes.TryGetValue(col.TargetColName, out var t))
                throw new AppValidationException(
                    $"Column '{col.TargetColName}' does not exist on entity '{mapping.TargetEntity}'.");
            columnImportType[col.ColumnName] = ImportCellParser.MapType(t);
        }

        // Scan every row and build a per-row ImportRecord.
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rowNumber > ImportFileCommand.MaxRows)
                throw new AppValidationException(
                    $"File contains more than {ImportFileCommand.MaxRows:N0} data rows; validation rejected.");


            var values = new object?[activeMappings.Count];
            bool skipRow = false;
            string? rowError = null;

            for (int i = 0; i < activeMappings.Count; i++)
            {
                var col = activeMappings[i];
                int excelIdx = columnIndex[col.ColumnName];
                var cell = reader.IsDBNull(excelIdx) ? null : reader.GetValue(excelIdx);

                try
                {
                    values[i] = ImportCellParser.Parse(cell, col, columnImportType[col.ColumnName]);
                }
                catch (AppValidationException ex)
                {
                    rowError ??= $"Row {rowNumber}: {ex.Message}";
                    skipRow = true;
                    // Leave values[i] null; the record will be marked Rejected below.
                    continue;
                }

                if (ImportCellParser.IsEmpty(values[i]) && requiredColumns.Contains(col.TargetColName))
                {
                    switch (emptyMode)
                    {
                        case EmptyFieldMode.Throw:
                            // Divergence from the executor: under Throw we do NOT abort the
                            // pre-flight scan. We record the error and continue so the user
                            // gets a complete error list. The real import would abort.
                            rowError ??= $"Row {rowNumber}: required field '{col.TargetColName}' is empty";
                            skipRow = true;
                            break;
                        case EmptyFieldMode.SkipRow:
                            rowError ??= $"Row {rowNumber}: required field '{col.TargetColName}' is empty";
                            skipRow = true;
                            break;
                        case EmptyFieldMode.Default:
                            values[i] = ImportCellParser.GetDefault(columnImportType[col.ColumnName]);
                            break;
                    }
                }
            }

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

            records.Add(new ImportRecord
            {
                ImportBatchId = 0, // Validation does not persist; placeholder for the in-memory record.
                SourceKey = rowNumber.ToString(),
                // Typed 1-based source-file row (journal framework tier 2) — keeps the
                // dry-run report on the same typed fact as the real import's journal.
                RowNumber = rowNumber,
                Status = skipRow ? ImportRowStatus.Rejected : ImportRowStatus.Imported,
                // Same truncation as the executor so both paths report identical
                // messages for the same row (the column is capped at 2000 in EF).
                ErrorMessage = ImportFileExecutor.Truncate(rowError, ImportRecord.MaxErrorMessageLength),
                Payload = JsonSerializer.Serialize(payload),
            });

            if (skipRow) rejectedCount++; else importedCount++;
            rowNumber++;

            if (progress is not null && rowNumber >= nextReportAt)
            {
                progress.Report(rowNumber);
                nextReportAt = rowNumber + reportEvery;
            }
        }

        progress?.Report(rowNumber - 1);

        _logger.LogInformation(
            "Validation completed for mapping {MappingId}: {Imported} imported, {Rejected} rejected.",
            mappingId, importedCount, rejectedCount);

        return new ImportValidationResult(records, importedCount, rejectedCount);
    }
}
