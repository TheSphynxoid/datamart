using System.Collections.Concurrent;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Queries.GetImportReport;

namespace DataMart.Infrastructure.Data;

/// <summary>
/// In-memory implementation of <see cref="IValidationProgressStore"/>. Stores
/// snapshots in a <see cref="ConcurrentDictionary{TKey, TValue}"/>. Entries are
/// not auto-evicted — see the interface for the rationale.
/// </summary>
public sealed class InMemoryValidationProgressStore : IValidationProgressStore
{
    private readonly ConcurrentDictionary<Guid, ValidationProgress> _entries = new();

    public void Register(Guid validationId, int totalRows)
    {
        var snapshot = new ValidationProgress { Total = totalRows };
        _entries[validationId] = snapshot;
    }

    public void Update(Guid validationId, int processed)
    {
        if (_entries.TryGetValue(validationId, out var snapshot))
        {
            // Progress<int> callbacks are posted asynchronously and can land after
            // MarkDone/MarkFailed; never let a straggler rewrite a terminal entry
            // (it would drag the percent back from 100 and overwrite the count).
            if (snapshot.Status is ValidationStatus.Done or ValidationStatus.Failed)
                return;

            snapshot.Processed = processed;
        }
    }

    public void MarkDone(Guid validationId, ImportReportDto report)
    {
        if (_entries.TryGetValue(validationId, out var snapshot))
        {
            snapshot.Processed = snapshot.Total;
            snapshot.Status = ValidationStatus.Done;
            snapshot.Report = report;
        }
    }

    public void MarkFailed(Guid validationId, string error)
    {
        if (_entries.TryGetValue(validationId, out var snapshot))
        {
            snapshot.Status = ValidationStatus.Failed;
            snapshot.Error = error;
        }
    }

    public bool TryGet(Guid validationId, out ValidationProgress? progress)
    {
        var found = _entries.TryGetValue(validationId, out var snapshot);
        progress = snapshot;
        return found;
    }
}
