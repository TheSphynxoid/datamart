using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace DataMart.Domain.Entities;

[Exclude]
public class ImportBatch : BaseAuditableEntity
{
    private string _fileName = string.Empty;
    private string _affectedTable = string.Empty;
    private string _sourceChecksum = string.Empty;

    public required string FileName
    {
        get => _fileName;
        set
        {
            ThrowIfImmutable();
            _fileName = value;
        }
    }

    private ImportStatus _status = ImportStatus.Processing;
    public ImportStatus Status
    {
        get => _status;
        set
        {
            // Allow no-op (setting same value)
            if (_status == value) return;
            
            if (_status is ImportStatus.Success or ImportStatus.Failed or ImportStatus.Reverted)
                throw new InvalidOperationException($"Cannot change status from {_status} to {value}. Status is immutable after completion.");
            
            // Valid transitions: Processing -> Success/Failed; Processing/Success/Failed -> Reverted (via Revert method)
            if (_status == ImportStatus.Processing && value is not ImportStatus.Success and not ImportStatus.Failed)
                throw new InvalidOperationException($"Invalid status transition from {_status} to {value}.");
            
            _status = value;
        }
    }

    public required string AffectedTable
    {
        get => _affectedTable;
        set
        {
            ThrowIfImmutable();
            _affectedTable = value;
        }
    }

    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int SkippedRows { get; set; }
    public string? SkippedRowsPreview { get; set; }

    /// <summary>Number of <see cref="ImportRecord"/> rows persisted to the journal so far.
    /// Starts at 0 after the target-table COPY commits and advances as the post-COPY
    /// journal upload progresses. When this equals <see cref="TotalRows"/> the journal
    /// is fully uploaded; the dashboard uses this to show/hide the "Journal uploading"
    /// pill on Success batches.</summary>
    public int JournalRowsWritten { get; set; }

    /// <summary>True when the journal upload did not finish (chunk failure or process
    /// death mid-upload). The remaining rows cannot be recovered because the source
    /// Excel file is deleted after the import; this flag is the audit-visible marker
    /// that the journal is partial. Reconciled to true on startup for any
    /// Status=Success batch where <see cref="JournalRowsWritten"/> &lt;
    /// <see cref="TotalRows"/>.</summary>
    public bool JournalIncomplete { get; set; }

    /// <summary>What the executor is currently doing with the batch while it is
    /// Processing: <see cref="ImportPhase.Validating"/> = the full parse/transform/
    /// validate row pass (no DB writes yet — the wizard's progress bar labels this
    /// so it doesn't look stuck), <see cref="ImportPhase.Copying"/> = rows are being
    /// COPY'd to the target table. Null on pre-framework batches and once the batch
    /// is terminal (Complete/Fail clear it).</summary>
    public ImportPhase? Phase { get; set; }

    /// <summary>Human-readable failure motif recorded when the batch lands in
    /// Failed (post-COPY failure, PLAYBACK abort or startup reconciliation).
    /// Null while Processing and on Success. Failed batches have no row journal
    /// (the journal is written only after a successful COPY), so this field is
    /// what the wizard and the dashboard expose as the failure cause.</summary>
    public string? FailedReason { get; set; }

    // Journal framework (V2 build item 1) — tier-1 facts. Spec: docs/specs/journal-framework.md §3.

    /// <summary>Hard cap for <see cref="MappingSnapshotJson"/> and
    /// <see cref="RulesSnapshotJson"/> (spec §9.1): a snapshot is stored complete
    /// or the batch fails before processing — never truncated, so the audit
    /// value stays binary (complete copy or no import). 1 MB of UTF-8 bytes is
    /// far above any realistic mapping/rule set (tens of KB). The cap counts
    /// bytes, not chars: accented names and currency symbols are exactly the
    /// content this tool imports, and they cost up to 4 bytes per char.</summary>
    public const int MaxSnapshotJsonBytes = 1_000_000;

    /// <summary>Processing start (the executor begins). Distinct from
    /// <see cref="BaseAuditableEntity.Created"/> (batch-row creation: upload accept time).</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Set when the batch reaches a terminal status (Success/Failed); null while
    /// Processing. Revert is stamped by <see cref="RevertedAt"/> instead.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Full copy of the effective mapping (column mappings + format options)
    /// at import time. A copy, not an FK — mappings are editable; the snapshot makes
    /// "import always ran on the config the user saw validated" provable after the fact.
    /// Null on pre-framework batches.</summary>
    public string? MappingSnapshotJson { get; private set; }

    /// <summary>Copy of the active <see cref="EntityValidationRule"/> set for the target
    /// entity, as evaluated at import time (same rationale as the mapping snapshot).
    /// Null on pre-framework batches.</summary>
    public string? RulesSnapshotJson { get; private set; }

    /// <summary>Range of target-table IDs the batch created (fed from the COPY step).
    /// Gaps are acceptable — a range claim for audit/revert scoping, not an enumeration.
    /// Null on pre-framework batches or empty imports.</summary>
    public int? MinImpactedId { get; private set; }
    public int? MaxImpactedId { get; private set; }

    /// <summary>Extensible bag for future per-import facts without schema churn.
    /// Convention: writers write whole-object JSON (camelCase keys); readers
    /// tolerate missing keys.</summary>
    public string? JournalExtrasJson { get; private set; }

    public ICollection<ImportRecord> Records { get; set; } = [];

    // Reconciliation & Audit (FC-1, FC-2)
    public string? ControlTotalsJson { get; private set; }
    
    public required string SourceChecksum
    {
        get => _sourceChecksum;
        set
        {
            ThrowIfImmutable();
            _sourceChecksum = value;
        }
    }

    public string? ExecutedBy { get; private set; }
    public bool LegalHold { get; private set; }
    public DateTimeOffset? RetentionExpiresAt { get; private set; }
    public DateTimeOffset? RevertedAt { get; private set; }
    public string? RevertedBy { get; private set; }

    public void SetControlTotals(string controlTotalsJson)
    {
        ThrowIfImmutable();
        ControlTotalsJson = controlTotalsJson;
    }

    public void SetExecutedBy(string userId)
    {
        ThrowIfImmutable();
        ExecutedBy = userId;
    }

    public void SetLegalHold(bool hold) => LegalHold = hold; // Legal hold can be toggled anytime
    public void SetRetentionExpiresAt(DateTimeOffset? expiresAt) => RetentionExpiresAt = expiresAt;

    /// <summary>Marks the start of processing and stores the import-time config
    /// snapshots. Set-once: a second call throws (the executor calls it exactly
    /// once). Enforces the snapshot size cap — over the cap throws and stores
    /// nothing, so the executor's failure path lands the batch in Failed with
    /// the cap as the reason instead of persisting a truncated snapshot.</summary>
    public void MarkStarted(string? mappingSnapshotJson, string? rulesSnapshotJson)
    {
        ThrowIfImmutable();
        if (StartedAt is not null)
            throw new InvalidOperationException("MarkStarted has already been called for this batch; snapshots are set once at start.");

        ThrowIfSnapshotOverCap(mappingSnapshotJson, "Mapping");
        ThrowIfSnapshotOverCap(rulesSnapshotJson, "Rules");

        MappingSnapshotJson = mappingSnapshotJson;
        RulesSnapshotJson = rulesSnapshotJson;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Processing → Success; stamps <see cref="CompletedAt"/>.</summary>
    public void Complete()
    {
        EnsureProcessing(nameof(Complete));
        _status = ImportStatus.Success;
        CompletedAt = DateTimeOffset.UtcNow;
        Phase = null;
    }

    /// <summary>Processing → Failed; stamps <see cref="CompletedAt"/> and the failure motif.</summary>
    public void Fail(string reason)
    {
        EnsureProcessing(nameof(Fail));
        _status = ImportStatus.Failed;
        FailedReason = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        Phase = null;
    }

    /// <summary>Records the range of target-table IDs created by the COPY step.
    /// Audit/revert scoping claim (gaps acceptable), immutable after a terminal status.</summary>
    public void SetImpactedIdRange(int? minImpactedId, int? maxImpactedId)
    {
        ThrowIfImmutable();
        MinImpactedId = minImpactedId;
        MaxImpactedId = maxImpactedId;
    }

    /// <summary>Replaces the journal extras bag with a whole-object JSON payload.</summary>
    public void SetJournalExtras(string journalExtrasJson)
    {
        ThrowIfImmutable();
        JournalExtrasJson = journalExtrasJson;
    }

    public void Revert(string userId)
    {
        if (_status == ImportStatus.Reverted)
            throw new InvalidOperationException("Batch is already reverted.");
        
        RevertedAt = DateTimeOffset.UtcNow;
        RevertedBy = userId;
        _status = ImportStatus.Reverted;
    }

    private void ThrowIfImmutable()
    {
        if (_status is ImportStatus.Success or ImportStatus.Failed or ImportStatus.Reverted)
            throw new InvalidOperationException($"Cannot modify audit fields after batch status is {_status}.");
    }

    private void EnsureProcessing(string operation)
    {
        if (_status != ImportStatus.Processing)
            throw new InvalidOperationException($"{operation} is only valid while Processing; batch is {_status}.");
    }

    private static void ThrowIfSnapshotOverCap(string? snapshotJson, string snapshotName)
    {
        if (snapshotJson is not null && Encoding.UTF8.GetByteCount(snapshotJson) > MaxSnapshotJsonBytes)
            throw new InvalidOperationException(
                $"{snapshotName} snapshot exceeds the {MaxSnapshotJsonBytes:N0}-byte cap; refusing to store a truncated snapshot.");
    }
}
