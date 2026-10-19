using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DataMart.Application.ImportFile.Common;

/// <summary>
/// Default <see cref="IImportJournalWriter"/>: a behavior-preserving move of V1's
/// inline journal-chunk logic (spec §5.3). Chunked to keep EF's change tracker
/// bounded: tracking 1M+ entities in a single SaveChanges stalls the change
/// tracker and generates multi-MB batched INSERTs that can hang the pipeline.
/// 5,000 per chunk keeps each round-trip small (~hundreds of KB of payload) and
/// the change tracker memory bounded.
/// </summary>
public class ImportJournalWriter : IImportJournalWriter
{
    /// <summary>Journal rows per SaveChanges round-trip (V1's proven chunk size).</summary>
    public const int JournalChunkSize = 5_000;

    private readonly IApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportJournalWriter> _logger;

    public ImportJournalWriter(IApplicationDbContext context, TimeProvider timeProvider, ILogger<ImportJournalWriter> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task WriteJournalChunksAsync(ImportBatch batch, IReadOnlyList<ImportRecord> records)
    {
        for (int offset = 0; offset < records.Count; offset += JournalChunkSize)
        {
            var chunkSize = Math.Min(JournalChunkSize, records.Count - offset);
            var chunk = new List<ImportRecord>(chunkSize);
            for (int i = offset; i < offset + chunkSize; i++)
                chunk.Add(records[i]);
            try
            {
                // Explicit write-time stamp (spec §4): do not rely on the
                // SaveChanges interceptor for the chunk-write path.
                var now = _timeProvider.GetUtcNow();
                foreach (var record in chunk)
                    record.Created = now;

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
                // is already deleted by the executor's finally block).
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

    public async Task UpdateImpactedIdRangeAsync(ImportBatch batch, ImportWriteStats writeStats)
    {
        batch.SetImpactedIdRange(writeStats.MinImpactedId, writeStats.MaxImpactedId);
        // CancellationToken.None: the ID range is an audit fact (FC-1/FC-4); like
        // the terminal-status write it must land even while the host is stopping.
        await _context.SaveChangesAsync(CancellationToken.None);
    }
}
