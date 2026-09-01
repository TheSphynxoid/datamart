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
        [FromQuery] DateTimeOffset? to = null)
    {
        var result = await sender.Send(new GetImportBatchesQuery(page, pageSize, status, affectedTable, from, to));
        return TypedResults.Ok(result);
    }

    public static async Task<Ok<ImportJournalPage>> GetJournal(
        ISender sender,
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        // The journal handler checks batch existence and throws NotFoundException if
        // the batch id doesn't exist; the global exception handler maps that to 404.
        var result = await sender.Send(new GetImportJournalQuery(id, page, pageSize));
        return TypedResults.Ok(result);
    }
}
