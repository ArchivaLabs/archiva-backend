namespace Archiva.Application.Organizations.Commands.CreateOrganization;

public class CreateOrganizationCommandValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationCommandValidator()
    {
        RuleFor(v => v.Name)
            .NotEmpty()
            .WithMessage("Organization Name is Required")
            .MaximumLength(50)
            .WithMessage("Organization Name must not exceed 50 characters");
    }
}
