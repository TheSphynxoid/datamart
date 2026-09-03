using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Application.Common.Interfaces;
using DataMart.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DataMart.Web.Endpoints;

public class EntitySchema : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapGet(GetEntitySchemas);
    }

    [EndpointSummary("Get Entities' Schema")]
    [EndpointDescription("Retrieves all Entities' Schema.")]
    public static async Task<Ok<EntitySchemaDto[]>> GetEntitySchemas(
        ISender sender,
        IAccessLogService accessLog,
        IUser user)
    {
        var dto = await sender.Send(new GetEntityQuery());

        // Log access (FC-4)
        await accessLog.LogAsync(
            user.Id ?? "unknown",
            AccessAction.ViewSchema,
            correlationId: Guid.NewGuid().ToString());

        return TypedResults.Ok(dto);
    }
}
