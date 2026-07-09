namespace Archiva.Application.Documents.Commands.UploadDocument;

public class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.MeetingId).GreaterThan(0).WithMessage("A valid meeting ID is required.");
        RuleFor(x => x.FileName).NotEmpty().WithMessage("File name is required.");
        RuleFor(x => x.FileSizeInBytes).GreaterThan(0).WithMessage("File cannot be empty");
        RuleFor(x => x.Description)
            .MaximumLength(1000)
            .WithMessage("Description must not exceed 500 characters")
            .When(x => x.Description is not null);
    }
}
