using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NpgsqlTypes;

namespace DataMart.Infrastructure.Data;

public class BulkImporter : IBulkImporter
{
    private readonly NpgsqlDataSource _dataSource;

    public BulkImporter(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }


    public async Task<int> ImportAsync(
        string tableName,
        IReadOnlyList<ImportColumn> columns,
        IEnumerable<ImportRow> rows,
        int importBatchId,
        CancellationToken ct,
        Func<int, Task>? onProgress = null)
    {
        var colList = string.Join(", ", columns.Select(c => $"\"{c.DestinationColumn}\""));
        var copyCmd = $"COPY \"{tableName}\" ({colList}) FROM STDIN BINARY";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var transaction = await conn.BeginTransactionAsync(ct);

        int rowCount = 0;
        await using (var writer = await conn.BeginBinaryImportAsync(copyCmd, ct))
        {
            foreach (var row in rows)
            {
                await writer.StartRowAsync(ct);
                for (int i = 0; i < columns.Count; i++)
                {
                    if (row.Values[i] is null)
                        await writer.WriteNullAsync(ct);
                    else
                        await writer.WriteAsync(row.Values[i], MapType(columns[i].DbType), ct);
                }
                rowCount++;
                if (rowCount % 5000 == 0 && onProgress is not null)
                    await onProgress(rowCount);
            }

            await writer.CompleteAsync(ct);   // commits the COPY (throws if not called)
        }

        // Npgsql keeps the connection in 'Copy' state until the binary importer is
        // disposed — disposing it above (scope exit) before committing the transaction
        // avoids NpgsqlOperationInProgressException ("connection is already in state 'Copy'").
        await transaction.CommitAsync(ct);

        return rowCount;
    }

    static NpgsqlDbType MapType(ImportColumnType type) => type switch
    {
        ImportColumnType.Integer => NpgsqlDbType.Integer,
        ImportColumnType.Text => NpgsqlDbType.Text,
        ImportColumnType.Decimal => NpgsqlDbType.Numeric,
        ImportColumnType.Date => NpgsqlDbType.Date,
        ImportColumnType.Time => NpgsqlDbType.Time,
        ImportColumnType.Boolean => NpgsqlDbType.Boolean,
        ImportColumnType.Timestamp => NpgsqlDbType.Timestamp,
        _ => NpgsqlDbType.Text
    };
}
