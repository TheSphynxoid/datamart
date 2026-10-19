using DataMart.Application.Dashboard.Queries.GetBatchDetail;
using DataMart.Application.Dashboard.Queries.GetImportBatches;
using DataMart.Application.Dashboard.Queries.GetImportJournal;
using DataMart.Application.Dashboard.Queries.GetImportStatistics;
using DataMart.Domain.Enums;
using DataMart.Web.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace DataMart.Web.Endpoints;

public class Dashboard : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapGet("statistics", GetStatistics);
        groupBuilder.MapGet("batches", GetBatches);
        groupBuilder.MapGet("batches/{id:int}/detail", GetBatchDetail);
        groupBuilder.MapGet("batches/{id:int}/journal", GetJournal);
    }

    public static async Task<Ok<ImportStatisticsDto>> GetStatistics(ISender sender)
    {
        var stats = await sender.Send(new GetImportStatisticsQuery());
        return TypedResults.Ok(stats);
    }

    public static async Task<Ok<ImportBatchesPage>> GetBatches(
        ISender sender,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ImportStatus? status = null,
        [FromQuery] string? affectedTable = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? search = null)
    {
        var result = await sender.Send(new GetImportBatchesQuery(page, pageSize, status, affectedTable, from, to, search));
        return TypedResults.Ok(result);
    }

    public static async Task<Ok<BatchDetailDto>> GetBatchDetail(ISender sender, int id)
    {
        // The detail query returns the tier-1 record including the snapshot
        // payloads (audit view of the exact config the import ran on); the
        // handler throws NotFound for unknown ids -> 404 via ProblemDetails.
        var result = await sender.Send(new GetBatchDetailQuery(id));
        return TypedResults.Ok(result);
    }

    public static async Task<Ok<ImportJournalPage>> GetJournal(
        ISender sender,
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] ImportRowStatus? status = null)
    {
        // The journal handler checks batch existence and throws NotFoundException if
        // the batch id doesn't exist; the global exception handler maps that to 404.
        var result = await sender.Send(new GetImportJournalQuery(id, page, pageSize, status));
        return TypedResults.Ok(result);
    }
}
