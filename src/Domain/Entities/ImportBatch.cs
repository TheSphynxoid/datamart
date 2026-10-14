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

    /// <summary>Human-readable failure motif recorded when the batch lands in
    /// Failed (post-COPY failure, PLAYBACK abort or startup reconciliation).
    /// Null while Processing and on Success. Failed batches have no row journal
    /// (the journal is written only after a successful COPY), so this field is
    /// what the wizard and the dashboard expose as the failure cause.</summary>
    public string? FailedReason { get; set; }

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
}
