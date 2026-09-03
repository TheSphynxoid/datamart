using DataMart.Application.ImportFile.Queries.GetImportReport;

namespace DataMart.Application.Common.Interfaces;

/// <summary>
/// Status of an async validation job. Mirrors the lifecycle
/// (queued → running → done) without overlapping the import-side
/// <c>ImportStatus</c> enum (which is audit-trail data).
/// </summary>
public enum ValidationStatus
{
    Running = 0,
    Done = 1,
    Failed = 2,
}

/// <summary>
/// Snapshot of an in-flight validation job. Lives in memory only —
/// the store is wiped on process restart, so any validation in flight
/// at the time of a crash is lost (acceptable for a dry-run).
/// </summary>
public sealed class ValidationProgress
{
    public required int Total { get; init; }
    public int Processed { get; set; }
    public ValidationStatus Status { get; set; } = ValidationStatus.Running;
    public ImportReportDto? Report { get; set; }
    public string? Error { get; set; }

    public int Percent => Total <= 0 ? 0 : (int)Math.Round(Processed * 100.0 / Total);
}

/// <summary>
/// In-memory store of async validation jobs. Singleton — one entry per
/// active validation; entries are NOT evicted on completion (clients can
/// still poll for the report), so the store will grow over the process
/// lifetime. For a single-user dev environment this is fine; a cap or
/// LRU eviction would be the next step before scale-out.
/// </summary>
public interface IValidationProgressStore
{
    /// <summary>Register a new validation job. Called by StartValidation before enqueueing.</summary>
    void Register(Guid validationId, int totalRows);

    /// <summary>Update the processed row count for an in-flight validation.</summary>
    void Update(Guid validationId, int processed);

    /// <summary>Mark a validation as completed and store its report.</summary>
    void MarkDone(Guid validationId, ImportReportDto report);

    /// <summary>Mark a validation as failed and store the error message.</summary>
    void MarkFailed(Guid validationId, string error);

    /// <summary>Get the current snapshot for a validation. Returns false if the id is unknown.</summary>
    bool TryGet(Guid validationId, out ValidationProgress? progress);
}
