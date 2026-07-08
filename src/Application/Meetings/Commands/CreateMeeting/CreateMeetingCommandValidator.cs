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

        RuleFor(x => x.MeetingTime).NotEmpty().WithMessage("Meeting time is required.");

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
}
