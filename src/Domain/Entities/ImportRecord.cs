using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Domain.Enums;

namespace DataMart.Domain.Entities;

/// <summary>
/// Row-level import journal (FC-4): one record per processed row carrying a JSON
/// snapshot of its mapped values and the outcome. This is the queryable audit trail
/// that the import report (and a future dashboard) read. It is NOT a business target
/// entity, so it is excluded from the importable-entity list.
/// </summary>
[Exclude("Internal row-level import journal / audit (FC-4)")]
public class ImportRecord : BaseAuditableEntity
{
    /// <summary>Max length of <see cref="ErrorMessage"/>; must stay in sync with the
    /// EF configuration. Writers must truncate to this so an over-long rule-failure
    /// concatenation cannot fail the journal chunk write.</summary>
    public const int MaxErrorMessageLength = 2000;

    /// <summary>Max length of <see cref="ErrorCode"/>; must stay in sync with the
    /// EF configuration. Writers must truncate to this.</summary>
    public const int MaxErrorCodeLength = 200;

    private string? _sourceKey;
    private int? _importBatchId;

    /// <summary>Explicit 1-based source-file row number: the typed replacement for
    /// parsing <see cref="SourceKey"/> (spec §4). Null on pre-framework rows — the
    /// additive migration backfills nothing, and null ("not recorded") is the
    /// honest audit value for them, not a sentinel 0. Indexed with
    /// <see cref="ImportBatchId"/> for the drill-down's stable sort/filter.</summary>
    public int? RowNumber { get; set; }

    /// <summary>Stable issue key for similarity grouping in the journal drill-down
    /// ("same issue = same box"): <c>required.{field}</c>, <c>parse.{field}.{type}</c>,
    /// <c>rule.{ruleId}</c>. Unlike <see cref="ErrorMessage"/> it carries no row or
    /// value detail. Null on pre-framework rows. The first issue on a row wins
    /// (mirrors how <see cref="ErrorMessage"/> accumulates).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>JSON snapshot of the row's mapped values: { targetColumn: value }.</summary>
    public string? Payload { get; set; }

    /// <summary>Why the row was rejected; null when <see cref="Status"/> is Imported.</summary>
    public string? ErrorMessage { get; set; }

    public ImportRowStatus Status { get; set; }

    public ImportBatch? Batch { get; set; }

    public new string? SourceKey
    {
        get => _sourceKey;
        set
        {
            if (_sourceKey != null)
                throw new InvalidOperationException("SourceKey is immutable after insert.");
            _sourceKey = value;
        }
    }

    public new int? ImportBatchId
    {
        get => _importBatchId;
        set
        {
            if (_importBatchId != null)
                throw new InvalidOperationException("ImportBatchId is immutable after insert.");
            _importBatchId = value;
        }
    }
}
