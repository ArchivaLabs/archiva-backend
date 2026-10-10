using Archiva.Application.Meetings.Commands.UpdateMeeting;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Meetings;

public class UpdateMeetingCommandValidatorTests
{
    private readonly UpdateMeetingCommandValidator _validator = new();

    [Test]
    public void AcceptsValidMeetingWithMaximumTenTags()
    {
        var command = ValidCommand() with
        {
            Tags = Enumerable.Range(1, 10).Select(i => $"Tag {i}").ToList(),
        };

        _validator.Validate(command).IsValid.ShouldBeTrue();
    }

    [TestCase(0, nameof(UpdateMeetingCommand.Id))]
    [TestCase(-1, nameof(UpdateMeetingCommand.Id))]
    public void RejectsNonPositiveMeetingId(int id, string property)
    {
        var result = _validator.Validate(ValidCommand() with { Id = id });

        result.Errors.ShouldContain(error => error.PropertyName == property);
    }

    [Test]
    public void RejectsMissingRequiredFields()
    {
        var result = _validator.Validate(new UpdateMeetingCommand());

        result.Errors.ShouldContain(error => error.PropertyName == nameof(UpdateMeetingCommand.Id));
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UpdateMeetingCommand.Title)
        );
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UpdateMeetingCommand.MeetingDate)
        );
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UpdateMeetingCommand.MeetingTime)
        );
    }

    [Test]
    public void RejectsExcessiveFieldAndTagLengths()
    {
        var command = ValidCommand() with
        {
            Title = new string('T', 201),
            Description = new string('D', 1001),
            Location = new string('L', 201),
            Tags = [new string('A', 51), ""],
        };

        var properties = _validator
            .Validate(command)
            .Errors.Select(error => error.PropertyName)
            .ToArray();

        properties.ShouldContain(nameof(UpdateMeetingCommand.Title));
        properties.ShouldContain(nameof(UpdateMeetingCommand.Description));
        properties.ShouldContain(nameof(UpdateMeetingCommand.Location));
        properties.ShouldContain("Tags[0]");
        properties.ShouldContain("Tags[1]");
    }

    [Test]
    public void RejectsMoreThanTenTags()
    {
        var command = ValidCommand() with
        {
            Tags = Enumerable.Range(1, 11).Select(i => $"Tag {i}").ToList(),
        };

        _validator
            .Validate(command)
            .Errors.ShouldContain(error => error.PropertyName == nameof(UpdateMeetingCommand.Tags));
    }

    private static UpdateMeetingCommand ValidCommand() =>
        new()
        {
            Id = 1,
            Title = "Council meeting",
            MeetingDate = DateTime.UtcNow.Date.AddDays(1),
            MeetingTime = TimeSpan.FromHours(9),
        };
}
