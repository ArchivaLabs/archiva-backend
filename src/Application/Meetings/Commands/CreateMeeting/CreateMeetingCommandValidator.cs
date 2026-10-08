using FluentValidation;

namespace Archiva.Application.Meetings.Commands.CreateMeeting;

public class CreateMeetingCommandValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingCommandValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Meeting title is required.")
            .MaximumLength(200)
            .WithMessage("Title must not exceed 200 characters.");

        RuleFor(x => x.MeetingDate).NotEmpty().WithMessage("Meeting date is required.");

        RuleFor(x => x.MeetingTime)
            .Must(time => time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            .WithMessage("Meeting time is invalid.");

        RuleFor(x => x.UtcOffsetMinutes)
            .InclusiveBetween(-840, 720)
            .WithMessage("The time zone offset is invalid.");

        RuleFor(x => x.MeetingDate)
            .Must((command, _) => StartsInFuture(command))
            .WithMessage("Meeting date and time must be in the future.");

        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Description must not exceed 1000 characters.")
            .When(x => x.Description is not null);

        RuleFor(x => x.Location)
            .MaximumLength(500)
            .WithMessage("Loacation must not exceed 500 characters.");

        RuleFor(x => x.Tags)
            .Must(tags => tags.Count <= 10)
            .WithMessage("A meeting cannot have more than 10 tags.");

        RuleForEach(x => x.Tags)
            .NotEmpty()
            .WithMessage("Tag names cannot be empty.")
            .MaximumLength(50)
            .WithMessage("Tag name must not exceed 50 characters.");
    }

    private static bool StartsInFuture(CreateMeetingCommand command)
    {
        if (
            command.MeetingDate == default
            || command.MeetingTime < TimeSpan.Zero
            || command.MeetingTime >= TimeSpan.FromDays(1)
            || command.UtcOffsetMinutes is < -840 or > 720
        )
            return true;

        try
        {
            var localStart = DateTime.SpecifyKind(
                command.MeetingDate.Date + command.MeetingTime,
                DateTimeKind.Unspecified
            );
            var start = new DateTimeOffset(
                localStart,
                TimeSpan.FromMinutes(-command.UtcOffsetMinutes)
            );
            return start > DateTimeOffset.UtcNow;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
