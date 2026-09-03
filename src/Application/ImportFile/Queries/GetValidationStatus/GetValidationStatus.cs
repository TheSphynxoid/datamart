using System;
using System.Threading;
using System.Threading.Tasks;
using DataMart.Application.Common.Exceptions;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Queries.GetImportReport;

namespace DataMart.Application.ImportFile.Queries.GetValidationStatus;

/// <summary>
/// Snapshot of an async validation: either still running (with row counts)
/// or finished (with the report). DTOs are flattened from the in-memory
/// <see cref="ValidationProgress"/> so the API surface stays stable across
/// store changes.
/// </summary>
public record ValidationStatusDto(
    Guid ValidationId,
    int Total,
    int Processed,
    int Percent,
    string Status,
    ImportReportDto? Report,
    string? Error);

public record class GetValidationStatusQuery(Guid ValidationId) : IRequest<ValidationStatusDto>;

public class GetValidationStatusQueryHandler : IRequestHandler<GetValidationStatusQuery, ValidationStatusDto>
{
    private readonly IValidationProgressStore _store;

    public GetValidationStatusQueryHandler(IValidationProgressStore store)
    {
        _store = store;
    }

    public Task<ValidationStatusDto> Handle(GetValidationStatusQuery request, CancellationToken cancellationToken)
    {
        if (!_store.TryGet(request.ValidationId, out var progress) || progress is null)
            throw new NotFoundException($"Validation {request.ValidationId} not found.",
                request.ValidationId.ToString());

        return Task.FromResult(new ValidationStatusDto(
            request.ValidationId,
            progress.Total,
            progress.Processed,
            progress.Percent,
            progress.Status.ToString(),
            progress.Report,
            progress.Error));
    }
}
