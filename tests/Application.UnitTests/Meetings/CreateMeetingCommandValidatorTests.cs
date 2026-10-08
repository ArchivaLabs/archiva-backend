using Archiva.Application.Meetings.Commands.CreateMeeting;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Meetings;

public class CreateMeetingCommandValidatorTests
{
    private readonly CreateMeetingCommandValidator _validator = new();

    [Test]
    public void RejectsPastStartInProvidedTimeZone()
    {
        var start = DateTimeOffset.UtcNow.AddHours(-1).ToOffset(TimeSpan.FromHours(1));

        var result = _validator.Validate(CommandFor(start));

        result.Errors.ShouldContain(error => error.PropertyName == "MeetingDate");
    }

    [Test]
    public void AcceptsFutureStartInProvidedTimeZone()
    {
        var start = DateTimeOffset.UtcNow.AddDays(1).ToOffset(TimeSpan.FromHours(1));

        var result = _validator.Validate(CommandFor(start));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void AcceptsFutureMeetingAtMidnight()
    {
        var start = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(2), TimeSpan.Zero);

        var result = _validator.Validate(CommandFor(start));

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void RejectsInvalidTimeZoneOffset()
    {
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var command = CommandFor(start) with { UtcOffsetMinutes = 900 };

        var result = _validator.Validate(command);

        result.Errors.ShouldContain(error => error.PropertyName == "UtcOffsetMinutes");
    }

    private static CreateMeetingCommand CommandFor(DateTimeOffset start) =>
        new()
        {
            Title = "Future meeting",
            MeetingDate = start.Date,
            MeetingTime = start.TimeOfDay,
            UtcOffsetMinutes = (int)-start.Offset.TotalMinutes,
        };
}
