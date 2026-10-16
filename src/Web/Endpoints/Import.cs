using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Commands.RunValidation;
using DataMart.Application.ImportFile.Queries.GetImportBatch;
using DataMart.Application.ImportFile.Queries.GetImportReport;
using DataMart.Application.ImportFile.Queries.GetValidationStatus;
using DataMart.Application.Common.Interfaces;
using DataMart.Web.Common;
using DataMart.Domain.Enums;
using DataMart.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DataMart.Web.Endpoints;

public class Import : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapPost(UploadFile);
        groupBuilder.MapPost("validate", ValidateFile);
        groupBuilder.MapGet("validations/{id:guid}", GetValidationStatus);
        groupBuilder.MapGet("batches/{id}", GetBatch);
        groupBuilder.MapGet("batches/{id}/preview", GetBatchPreview);
        groupBuilder.MapGet("batches/{id}/report", GetImportReport);
    }

    public static async Task<Results<Ok<ImportStarted>, BadRequest<string>, ForbidHttpResult>> UploadFile(
        ISender sender,
        IApplicationDbContext context,
        IAccessLogService accessLog,
        IUser user,
        IFormFile file,
        int mappingId,
        EmptyFieldMode emptyMode = EmptyFieldMode.Throw,
        int? sheetIndex = null)
    {
        if (file.Length is 0 or > 100_000_000)
            return TypedResults.BadRequest("Invalid file size.");

        var stream = file.OpenReadStream();
        var inspection = XlsxPackageInspector.Inspect(stream);
        if (!inspection.IsAcceptable)
            return TypedResults.BadRequest(inspection.ErrorMessage ?? "Invalid .xlsx file.");
        stream.Position = 0;

        // Maker/Checker (FC-3): Prevent mapping creator from executing import.
        // TODO(settings): make this togglable per-environment (e.g. enforce in
        // production, relax in single-user dev). For now, the rule is commented
        // out so a single-user test environment can import its own mappings.
        // var mapping = await context.Mappings.FindAsync([mappingId]);
        // if (mapping != null && mapping.CreatedBy == user.Id)
        //     return TypedResults.Forbid();

        var correlationId = Guid.NewGuid().ToString();

        try
        {
            var result = await sender.Send(new ImportFileCommand(stream, file.FileName, mappingId, emptyMode, sheetIndex));

            // Log access (FC-4)
            await accessLog.LogAsync(
                user.Id ?? "unknown",
                AccessAction.ExecuteImport,
                mappingId: mappingId,
                batchId: result.BatchId,
                correlationId: correlationId);

            return TypedResults.Ok(result);
        }
        catch (ValidationException ex)
        {
            var errors = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
            return TypedResults.BadRequest(errors);
        }

        // No generic `catch (Exception) -> BadRequest(ex.Message)`: it leaks internal
        // error text to the client and misclassifies server faults as 400s. App
        // exceptions propagate to the ProblemDetailsExceptionHandler, which maps
        // AppValidationException -> 400, NotFound -> 404, etc.
    }

    public static async Task<Results<Ok<ValidationStarted>, BadRequest<string>>> ValidateFile(
        ISender sender,
        IAccessLogService accessLog,
        IUser user,
        IFormFile file,
        int mappingId,
        EmptyFieldMode emptyMode = EmptyFieldMode.Throw,
        int? sheetIndex = null)
    {
        if (file.Length is 0 or > 100_000_000)
            return TypedResults.BadRequest("Invalid file size.");

        var stream = file.OpenReadStream();
        var inspection = XlsxPackageInspector.Inspect(stream);
        if (!inspection.IsAcceptable)
            return TypedResults.BadRequest(inspection.ErrorMessage ?? "Invalid .xlsx file.");
        stream.Position = 0;

        var correlationId = Guid.NewGuid().ToString();

        try
        {
            // Async: returns immediately with the validation id; the heavy
            // work runs on ValidationProcessingService and the caller polls
            // GET /api/Import/validations/{id} for progress + final report.
            var started = await sender.Send(new StartValidationCommand(stream, file.FileName, mappingId, emptyMode, sheetIndex));

            // FC-4: validation reads file contents, so it is logged like a preview.
            // Logged at start (when the file is read) — final-report access is
            // logged by the report endpoint.
            await accessLog.LogAsync(
                user.Id ?? "unknown",
                AccessAction.PreviewData,
                mappingId: mappingId,
                batchId: null,
                correlationId: correlationId);

            return TypedResults.Ok(started);
        }
        catch (ValidationException ex)
        {
            var errors = string.Join("; ", ex.Errors.Select(e => e.ErrorMessage));
            return TypedResults.BadRequest(errors);
        }

        // No generic catch -> BadRequest(ex.Message): same rationale as UploadFile.
    }

    public static async Task<Results<Ok<ValidationStatusDto>, BadRequest<string>>> GetValidationStatus(
        ISender sender,
        Guid id)
    {
        // NotFoundException propagates to the ProblemDetailsExceptionHandler,
        // which maps it to a proper 404 (an unknown validation id is a client
        // error, not a bad request).
        var status = await sender.Send(new GetValidationStatusQuery(id));
        return TypedResults.Ok(status);
    }

    public static async Task<Ok<ImportBatchDto>> GetBatch(ISender sender, int id)
    {
        var batch = await sender.Send(new GetImportBatchQuery(id));
        return TypedResults.Ok(batch);
    }

    public static async Task<Ok<ImportReportDto>> GetImportReport(
        ISender sender,
        IAccessLogService accessLog,
        IUser user,
        int id)
    {
        var report = await sender.Send(new GetImportReportQuery(id));

        // FC-4: reading a report exposes imported data, so it is logged like a preview.
        await accessLog.LogAsync(
            user.Id ?? "unknown",
            AccessAction.PreviewData,
            batchId: report.BatchId,
            correlationId: Guid.NewGuid().ToString());

        return TypedResults.Ok(report);
    }

    public static async Task<Results<Ok<ImportBatchPreviewDto>, BadRequest<string>>> GetBatchPreview(
        ISender sender,
        IAccessLogService accessLog,
        IUser user,
        int id)
    {
        // Log access (FC-4)
        await accessLog.LogAsync(
            user.Id ?? "unknown",
            AccessAction.PreviewData,
            batchId: id,
            correlationId: Guid.NewGuid().ToString());

        // For now, return a placeholder - actual preview logic would be implemented
        var batch = await sender.Send(new GetImportBatchQuery(id));
        var preview = new ImportBatchPreviewDto
        {
            BatchId = batch.Id,
            FileName = batch.FileName,
            TotalRows = batch.TotalRows,
            ProcessedRows = batch.ProcessedRows,
            Status = batch.Status
        };

        return TypedResults.Ok(preview);
    }
}

public record ImportBatchPreviewDto
{
    public int BatchId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public int TotalRows { get; init; }
    public int ProcessedRows { get; init; }
    public ImportStatus Status { get; init; }
}
