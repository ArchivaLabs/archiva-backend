using Archiva.Application.Organizations.Commands.CreateOrganization;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Organizations;

public class CreateOrganizationCommandValidatorTests
{
    private readonly CreateOrganizationCommandValidator _validator = new();

    [TestCase(0, false)]
    [TestCase(50, true)]
    [TestCase(51, false)]
    public void ValidatesOrganizationNameLength(int length, bool isValid)
    {
        var result = _validator.Validate(
            new CreateOrganizationCommand { Name = new string('A', length) }
        );

        result.IsValid.ShouldBe(isValid);
    }

    [Test]
    public void RejectsWhitespaceName()
    {
        var result = _validator.Validate(new CreateOrganizationCommand { Name = "   " });

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(CreateOrganizationCommand.Name)
        );
    }
}
