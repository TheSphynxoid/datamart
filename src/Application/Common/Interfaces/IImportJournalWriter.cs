using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Domain.Entities;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// Journal-writer seam (journal framework spec §5.3): the import executor writes
/// all tier-2 row-journal chunks and tier-1 counter/ID-range updates THROUGH this
/// seam instead of inline. Future journal facts (UPSERT actions, per-rule
/// evaluation results, revert compensation records) extend one writer
/// implementation, not the pipeline.
/// </summary>
public interface IImportJournalWriter
{
    /// <summary>
    /// Persists the batch's row journal (tier 2) in bounded chunks after the COPY
    /// committed, advancing <see cref="ImportBatch.JournalRowsWritten"/> per chunk.
    /// A chunk failure marks <see cref="ImportBatch.JournalIncomplete"/> and stops
    /// the upload — it never rolls back the imported rows (the journal is audit
    /// evidence, not the write itself). Stamps <see cref="ImportRecord.Created"/>
    /// explicitly so the stamp does not depend on the SaveChanges interceptor.
    /// Implementations run with <c>CancellationToken.None</c>: like the
    /// terminal-status write, the audit journal must land even while the host is
    /// stopping, so no token is accepted on the contract.
    /// </summary>
    Task WriteJournalChunksAsync(ImportBatch batch, IReadOnlyList<ImportRecord> records);

    /// <summary>
    /// Persists the tier-1 impacted-ID range for the batch from the COPY result
    /// (the range claim of target-table IDs this batch created).
    /// </summary>
    Task UpdateImpactedIdRangeAsync(ImportBatch batch, ImportWriteStats writeStats);
}
