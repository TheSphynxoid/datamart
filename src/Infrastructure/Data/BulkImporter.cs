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


    public async Task<ImportWriteStats> ImportAsync(
        string tableName,
        IReadOnlyList<ImportColumn> columns,
        IEnumerable<ImportRow> rows,
        int importBatchId,
        CancellationToken ct,
        Func<int, Task>? onProgress = null)
    {
        // SQL identifiers (table + column names) are double-quoted; an embedded
        // double quote must be doubled, or it breaks out of the identifier. The
        // mapping validators already restrict these names — this is the last line
        // of defense (defense in depth), so it must not trust them.
        static string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"")}\"";

        var colList = string.Join(", ", columns.Select(c => QuoteIdentifier(c.DestinationColumn)));
        var copyCmd = $"COPY {QuoteIdentifier(tableName)} ({colList}) FROM STDIN BINARY";

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

        // Journal framework tier-1 fact: bracket the target-table IDs this COPY
        // created. Every row written by this COPY carries ImportBatchId, so a
        // MIN/MAX over that filter is exactly this batch's range (gaps acceptable
        // — a range claim for audit/revert scoping, not an enumeration). Runs on
        // the already-open connection, right after the commit.
        int? minImpactedId = null;
        int? maxImpactedId = null;
        await using (var rangeCmd = conn.CreateCommand())
        {
            rangeCmd.CommandText = $"SELECT MIN(\"Id\"), MAX(\"Id\") FROM {QuoteIdentifier(tableName)} WHERE \"ImportBatchId\" = @batchId";
            rangeCmd.Parameters.Add(new NpgsqlParameter<int>("batchId", importBatchId));
            await using var rangeReader = await rangeCmd.ExecuteReaderAsync(ct);
            if (await rangeReader.ReadAsync(ct))
            {
                minImpactedId = rangeReader.IsDBNull(0) ? null : rangeReader.GetInt32(0);
                maxImpactedId = rangeReader.IsDBNull(1) ? null : rangeReader.GetInt32(1);
            }
        }

        return new ImportWriteStats(rowCount, minImpactedId, maxImpactedId);
    }

    static NpgsqlDbType MapType(ImportColumnType type) => type switch
    {
        ImportColumnType.Integer => NpgsqlDbType.Integer,
        ImportColumnType.Uuid => NpgsqlDbType.Uuid,
        ImportColumnType.Text => NpgsqlDbType.Text,
        ImportColumnType.Decimal => NpgsqlDbType.Numeric,
        ImportColumnType.Date => NpgsqlDbType.Date,
        ImportColumnType.Time => NpgsqlDbType.Time,
        ImportColumnType.Boolean => NpgsqlDbType.Boolean,
        ImportColumnType.Timestamp => NpgsqlDbType.Timestamp,
        _ => NpgsqlDbType.Text
    };
}
