using Archiva.Application.Search.Queries;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Search;

public class SearchDocumentsQueryAdditionalValidatorTests
{
    private readonly SearchDocumentsQueryValidator _validator = new();

    [TestCase(1, true)]
    [TestCase(50, true)]
    [TestCase(0, false)]
    [TestCase(51, false)]
    public void ValidatesPageSizeBounds(int pageSize, bool isValid)
    {
        _validator
            .Validate(new SearchDocumentsQuery { PageSize = pageSize })
            .IsValid.ShouldBe(isValid);
    }

    [TestCase("relevance", true)]
    [TestCase("date_desc", true)]
    [TestCase("date_asc", true)]
    [TestCase("recent", false)]
    public void ValidatesSortOrder(string sortBy, bool isValid)
    {
        _validator.Validate(new SearchDocumentsQuery { SortBy = sortBy }).IsValid.ShouldBe(isValid);
    }

    [Test]
    public void RejectsExcessiveSearchTermAndTagLimits()
    {
        var query = new SearchDocumentsQuery
        {
            SearchTerm = new string('S', 201),
            Tags = Enumerable.Range(1, 21).Select(index => $"Tag {index}").ToList(),
        };

        var result = _validator.Validate(query);

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(SearchDocumentsQuery.SearchTerm)
        );
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(SearchDocumentsQuery.Tags)
        );
    }

    [Test]
    public void RejectsEmptyAndOverlongTagNames()
    {
        var query = new SearchDocumentsQuery { Tags = ["", new string('T', 101)] };

        var properties = _validator
            .Validate(query)
            .Errors.Select(error => error.PropertyName)
            .ToArray();

        properties.ShouldContain("Tags[0]");
        properties.ShouldContain("Tags[1]");
    }

    [Test]
    public void AcceptsInclusiveDateRange()
    {
        var date = new DateOnly(2026, 10, 9);

        _validator
            .Validate(new SearchDocumentsQuery { DateFrom = date, DateTo = date })
            .IsValid.ShouldBeTrue();
    }

    [Test]
    public void RejectsReversedOrUnrepresentableDateRange()
    {
        var reversed = _validator.Validate(
            new SearchDocumentsQuery
            {
                DateFrom = new DateOnly(2026, 10, 10),
                DateTo = new DateOnly(2026, 10, 9),
            }
        );
        var maximum = _validator.Validate(new SearchDocumentsQuery { DateTo = DateOnly.MaxValue });

        reversed.IsValid.ShouldBeFalse();
        maximum.Errors.ShouldContain(error =>
            error.PropertyName == nameof(SearchDocumentsQuery.DateTo)
        );
    }
}
