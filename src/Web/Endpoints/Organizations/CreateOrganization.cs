using Archiva.Application.Organizations.Commands.CreateOrganization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Organizations;

public class CreateOrganization : IEndpointGroup
{
    public static string? RoutePrefix => "api/organizations";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapPost(CreateOrganizationHandler);
    }

    [EndpointSummary("Create a new organisation")]
    [EndpointDescription(
        "Called during onboarding when a new user creates their organisation. "
            + "The calling user is automatically assigned the Admin role."
    )]
    public static async Task<Ok<CreateOrganizationResult>> CreateOrganizationHandler(
        ISender sender,
        CreateOrganizationCommand command
    )
    {
        var result = await sender.Send(command);
        return TypedResults.Ok(result);
    }
}
