using DataMart.Application.EntityList.Queries.GetEntityList;
using DataMart.Application.TodoLists.Queries.GetTodos;
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
    public static async Task<Ok<EntitySchemaDto[]>> GetEntitySchemas(ISender sender)
    {
        var dto = await sender.Send(new GetEntityQuery());

        return TypedResults.Ok(dto);
    }
}
