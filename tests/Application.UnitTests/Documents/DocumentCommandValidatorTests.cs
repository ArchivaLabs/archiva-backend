using Archiva.Application.Documents.Commands.BackfillDocumentAnalysis;
using Archiva.Application.Documents.Commands.RetryDocumentAnalysis;
using Archiva.Application.Documents.Commands.UploadDocument;
using NUnit.Framework;
using Shouldly;

namespace Archiva.Application.UnitTests.Documents;

public class DocumentCommandValidatorTests
{
    private readonly UploadDocumentCommandValidator _uploadValidator = new();
    private readonly RetryDocumentAnalysisCommandValidator _retryValidator = new();
    private readonly BackfillDocumentAnalysisCommandValidator _backfillValidator = new();

    [Test]
    public void UploadAcceptsValidMetadataAndMaximumDescriptionLength()
    {
        var command = ValidUpload() with { Description = new string('D', 1000) };

        _uploadValidator.Validate(command).IsValid.ShouldBeTrue();
    }

    [Test]
    public void UploadRejectsMissingOrInvalidMetadata()
    {
        var result = _uploadValidator.Validate(new UploadDocumentCommand());

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UploadDocumentCommand.MeetingId)
        );
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UploadDocumentCommand.FileName)
        );
        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UploadDocumentCommand.FileSizeInBytes)
        );
    }

    [Test]
    public void UploadRejectsDescriptionOverLimit()
    {
        var result = _uploadValidator.Validate(
            ValidUpload() with
            {
                Description = new string('D', 1001),
            }
        );

        result.Errors.ShouldContain(error =>
            error.PropertyName == nameof(UploadDocumentCommand.Description)
        );
    }

    [TestCase(1, null, null, true)]
    [TestCase(1, 1, 1, true)]
    [TestCase(0, null, null, false)]
    [TestCase(1, 0, null, false)]
    [TestCase(1, null, 0, false)]
    public void RetryValidatesIdAndOptionalPositiveLimits(
        int id,
        int? unitLimit,
        int? summaryLimit,
        bool isValid
    )
    {
        _retryValidator
            .Validate(new RetryDocumentAnalysisCommand(id, unitLimit, summaryLimit))
            .IsValid.ShouldBe(isValid);
    }

    [TestCase(0, 1, true)]
    [TestCase(10, 100, true)]
    [TestCase(-1, 1, false)]
    [TestCase(0, 0, false)]
    [TestCase(0, 101, false)]
    public void BackfillValidatesCursorAndBatchSize(int cursor, int batchSize, bool isValid)
    {
        _backfillValidator
            .Validate(new BackfillDocumentAnalysisCommand(cursor, batchSize))
            .IsValid.ShouldBe(isValid);
    }

    private static UploadDocumentCommand ValidUpload() =>
        new()
        {
            MeetingId = 1,
            FileName = "notes.txt",
            FileSizeInBytes = 1,
        };
}
