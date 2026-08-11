using Archiva.Application.Meetings.Commands.CreateMeeting;
using Archiva.Application.Meetings.Commands.DeleteMeeting;
using Archiva.Application.Meetings.Commands.UpdateMeeting;
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
        groupBuilder.MapPut(UpdateMeetingHandler, "{id}");
        groupBuilder.MapDelete(DeleteMeetingHandler, "{id}");
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

    [EndpointSummary("Update a meeting")]
    [EndpointDescription(
        "Updates the title, description, date, time, location and tags of an existing meeting. "
            + "Tags are resolved by name — existing tags are reused, "
            + "unrecognised tag names are created automatically. "
            + "Returns 404 if the meeting does not exist or belongs to a different organisation."
    )]
    public static async Task<Ok<MeetingDetailDto>> UpdateMeetingHandler(
        ISender sender,
        int id,
        UpdateMeetingCommand command
    )
    {
        // The id comes from the route, the rest of the command from the request body.
        // We merge them here so the handler has everything it needs.
        var result = await sender.Send(command with { Id = id });
        return TypedResults.Ok(result);
    }

    [EndpointSummary("Delete a meeting")]
    [EndpointDescription(
        "Permanently deletes a meeting and all its associated documents from both "
            + "the database and Azure Blob Storage. "
            + "Returns 404 if the meeting does not exist or belongs to a different organisation."
    )]
    public static async Task<NoContent> DeleteMeetingHandler(ISender sender, int id)
    {
        await sender.Send(new DeleteMeetingCommand { Id = id });
        return TypedResults.NoContent();
    }
}
