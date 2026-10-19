using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Application.Common.Interfaces;

public record ImportColumn(string DestinationColumn, ImportColumnType DbType);

public sealed class ImportRow
{
    public object?[] Values { get; init; } = [];
}
public enum ImportColumnType { Integer, Text, Decimal, Date, Boolean, Timestamp, Time, Uuid }

/// <summary>Outcome of a bulk write: the copied row count plus the range of
/// target-table IDs the write created (journal framework tier-1 fact; gaps are
/// acceptable — it is a range claim, not an enumeration). Nulls mean no rows
/// were written or the adapter could not observe the IDs.</summary>
public sealed record ImportWriteStats(int RowsCopied, int? MinImpactedId, int? MaxImpactedId);

public interface IBulkImporter
{
    Task<ImportWriteStats> ImportAsync(string tableName, IReadOnlyList<ImportColumn> columns, IEnumerable<ImportRow> rows, int importBatchId, CancellationToken ct, Func<int, Task>? onProgress = null);
}
