using DataMart.Application.EntityRules;
using DataMart.Application.EntityRules.Commands.CreateEntityRule;
using DataMart.Application.EntityRules.Commands.UpdateEntityRule;
using DataMart.Application.EntityRules.Queries.GetEntityRules;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DataMart.Web.Endpoints;

public class EntityRuleEndpoints : IEndpointGroup
{
    public static string? RoutePrefix => "/api/entity-rules";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        // No GET-by-id: the admin page edits from the listed rows, which carry
        // every field. No DELETE either: rules are deactivated via IsActive so
        // old batch reports keep their explanation.
        groupBuilder.MapGet(GetEntityRules);
        groupBuilder.MapPost(CreateEntityRule);
        groupBuilder.MapPut(UpdateEntityRule, "{id}");
    }

    public static async Task<Ok<List<EntityValidationRuleDto>>> GetEntityRules(
        ISender sender, string targetEntity, bool activeOnly = true)
    {
        var result = await sender.Send(new GetEntityRulesQuery(targetEntity, activeOnly));

        return TypedResults.Ok(result);
    }

    public static async Task<Created<int>> CreateEntityRule(ISender sender, CreateEntityRuleCommand command)
    {
        var id = await sender.Send(command);

        return TypedResults.Created($"/entity-rules/{id}/", id);
    }

    public static async Task<Results<NoContent, BadRequest>> UpdateEntityRule(
        ISender sender, int id, UpdateEntityRuleCommand command)
    {
        if (id != command.Id)
            return TypedResults.BadRequest();

        await sender.Send(command);

        return TypedResults.NoContent();
    }
}
