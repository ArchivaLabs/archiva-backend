using Archiva.Application.Search.Queries;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Search;

public class SearchDocumentsQueryValidatorTests
{
    private readonly SearchDocumentsQueryValidator _validator = new();

    [TestCase(1, true)]
    [TestCase(100_000, true)]
    [TestCase(0, false)]
    [TestCase(100_001, false)]
    public void ValidatesPageBounds(int page, bool expectedIsValid)
    {
        var result = _validator.Validate(new SearchDocumentsQuery { Page = page });

        result.IsValid.ShouldBe(expectedIsValid);
    }
}
