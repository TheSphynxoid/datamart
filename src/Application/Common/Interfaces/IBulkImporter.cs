using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Application.Common.Interfaces;

public record ImportColumn(string DestinationColumn, ImportColumnType DbType);

public sealed class ImportRow
{
    public object?[] Values { get; init; } = [];
}
public enum ImportColumnType { Integer, Text, Decimal, Date, Boolean, Timestamp, Time }
public interface IBulkImporter
{
    Task<int> ImportAsync(string tableName, IReadOnlyList<ImportColumn> columns, IEnumerable<ImportRow> rows, int importBatchId, CancellationToken ct, Func<int, Task>? onProgress = null);
}
