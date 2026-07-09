using Archiva.Application.Meetings.Commands.CreateMeeting;
using Archiva.Application.Meetings.Queries;
using Archiva.Application.Meetings.Queries.GetMeetings;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Archiva.Web.Endpoints.Meetings;

public class Meetings : IEndpointGroup
{
    public static string? RoutePrefix => "api/meetings";

    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();
        groupBuilder.MapPost(CreateMeetingHandler);
        groupBuilder.MapGet(GetMeetingsHandler, "/");
        groupBuilder.MapGet(GetMeetingByIdHandler, "{id}");
    }

    [EndpointSummary("Create a new meeting")]
    [EndpointDescription(
        "Creates a new meeting for the authenticated user's organisation. "
            + "Tags are resolved by name — existing tags are reused, "
            + "unrecognised tag names are created automatically."
    )]
    public static async Task<Ok<CreateMeetingResult>> CreateMeetingHandler(
        ISender sender,
        CreateMeetingCommand command
    )
    {
        var result = await sender.Send(command);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get meetings")]
    [EndpointDescription(
        "Returns a paginated list of meetings for the authenticated user's organisation, "
            + "ordered by meeting date descending. Default page size is 10, maximum is 50."
    )]
    public static async Task<Ok<GetMeetingsResult>> GetMeetingsHandler(
        ISender sender,
        [AsParameters] GetMeetingsQuery query
    )
    {
        var result = await sender.Send(query);
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Get meeting by ID")]
    [EndpointDescription(
        "Returns the full details of a meeting including its documents and tags. "
            + "Returns 404 if the meeting does not exist or belongs to a different organisation."
    )]
    public static async Task<Ok<MeetingDetailDto>> GetMeetingByIdHandler(ISender sender, int id)
    {
        var result = await sender.Send(new GetMeetingByIdQuery { Id = id });
        return TypedResults.Ok(result);
    }
}
