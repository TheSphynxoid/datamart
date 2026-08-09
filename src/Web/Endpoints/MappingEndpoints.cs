using DataMart.Application.Common.Models;
using DataMart.Application.Mappings;
using DataMart.Application.Mappings.Commands.CreateMapping;
using DataMart.Application.Mappings.Commands.DeleteMapping;
using DataMart.Application.Mappings.Commands.UpdateMapping;
using DataMart.Application.Mappings.Queries.GetMapping;
using DataMart.Application.Mappings.Queries.GetMappings;
using DataMart.Application.TodoItems.Commands.DeleteTodoItem;
using DataMart.Application.TodoLists.Queries.GetTodos;
using DataMart.Domain.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DataMart.Web.Endpoints;

public class MappingEndpoints : IEndpointGroup
{

    public static string? RoutePrefix => "/api/mappings";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        

        groupBuilder.MapGet(GetMappings);
        groupBuilder.MapGet(GetMapping, "{id}");
        groupBuilder.MapPost(CreateMapping);
        groupBuilder.MapPut(UpdateMapping, "{id}");
        groupBuilder.MapDelete(DeleteMapping, "{id}");
    }

    public static async Task<Ok<List<MappingDto>>> GetMappings(ISender sender, string? targetEntity, string? search)
    {
        var result = await sender.Send(new GetMappingsQuery(targetEntity, search));

        return TypedResults.Ok(result);
    }

    public static async Task<Results<Ok<MappingDto>, NotFound>> GetMapping(ISender sender, int id)
    {
        var result = await sender.Send(new GetMappingQuery(id));
        return result is not null
            ? TypedResults.Ok(result)
            : TypedResults.NotFound();
    }

    public static async Task<Created<int>> CreateMapping(ISender sender, CreateMappingCommand command)
    {
        var id = await sender.Send(command);

        return TypedResults.Created($"/mappings/{id}/", id);
    }

    public static async Task<Results<NoContent, BadRequest>> UpdateMapping(ISender sender, int id, UpdateMappingCommand command)
    {
        if (id != command.Id)
            return TypedResults.BadRequest();

        await sender.Send(command);

        return TypedResults.NoContent();
    }

    public static async Task<NoContent> DeleteMapping(ISender sender, int id)
    {
        await sender.Send(new DeleteMappingCommand(id));

        return TypedResults.NoContent();
    }
}
