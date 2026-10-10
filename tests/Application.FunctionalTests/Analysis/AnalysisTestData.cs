using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Commands.ProcessDocumentAnalysis;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Archiva.Application.FunctionalTests.Analysis;

internal static class AnalysisTestData
{
    public static async Task<int> AddDocumentAsync(
        int organizationId,
        DocumentAnalysisStatus status = DocumentAnalysisStatus.Pending,
        Action<Document>? configure = null
    ) =>
        await TestApp.WithDbContextAsync(async db =>
        {
            var meeting = new Meeting
            {
                OrganizationId = organizationId,
                Title = "Analysis meeting",
                MeetingDate = DateTime.UtcNow.Date.AddDays(1),
                MeetingTime = TimeSpan.FromHours(10),
                CreatedById = TestSeed.FirstAdmin.Id,
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            var document = new Document
            {
                OrganizationId = organizationId,
                MeetingId = meeting.Id,
                FileName = "minutes.txt",
                FileType = "TXT",
                FileSizeInBytes = 4,
                BlobName = $"{Guid.NewGuid():N}/minutes.txt",
                BlobUrl = "unused",
                AnalysisStatus = status,
            };
            configure?.Invoke(document);
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            return document.Id;
        });

    public static Task<Document> GetDocumentAsync(int id) =>
        TestApp.WithDbContextAsync(db =>
            db.Documents.AsNoTracking().SingleAsync(document => document.Id == id)
        );
}

internal sealed class ProcessFakes
{
    public Mock<IStorageService> Storage { get; } = new();
    public Mock<IDocumentTextExtractor> Extractor { get; } = new();
    public Mock<IDocumentSummarizer> Summarizer { get; } = new();
    public Mock<IDocumentAnalysisBudget> Budget { get; } = new();
    public TimeProvider Clock { get; set; } = TimeProvider.System;
    public DocumentAnalysisOptions Options { get; } = new();

    public ProcessFakes()
    {
        Storage
            .Setup(storage =>
                storage.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())
            )
            .ReturnsAsync([1, 2, 3, 4]);
        Extractor
            .Setup(extractor => extractor.Estimate(It.IsAny<byte[]>(), It.IsAny<string>()))
            .Returns(new DocumentAnalysisEstimate(1, 4));
        Extractor
            .Setup(extractor =>
                extractor.ExtractAsync(
                    It.IsAny<byte[]>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync("Meeting notes");
        Summarizer.SetupGet(summarizer => summarizer.IsConfigured).Returns(true);
        Summarizer
            .Setup(summarizer =>
                summarizer.SummarizeAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync("Meeting summary");
        Budget
            .Setup(budget =>
                budget.TryReserveBillableUnitsAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
        Budget
            .Setup(budget =>
                budget.TryReserveSummaryCharactersAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync(true);
    }

    public Task ProcessAsync(int id) =>
        TestApp.WithDbContextAsync(async db =>
        {
            var handler = new ProcessDocumentAnalysisCommandHandler(
                db,
                Storage.Object,
                Extractor.Object,
                Summarizer.Object,
                Budget.Object,
                Clock,
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLogger<ProcessDocumentAnalysisCommandHandler>.Instance
            );
            await handler.Handle(new ProcessDocumentAnalysisCommand(id), CancellationToken.None);
        });
}
