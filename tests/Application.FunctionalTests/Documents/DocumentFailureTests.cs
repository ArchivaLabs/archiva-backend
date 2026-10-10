using Archiva.Application.Common.Interfaces;
using Archiva.Application.Common.Models;
using Archiva.Application.Documents.Commands.UploadDocument;
using Archiva.Application.Meetings.Commands.DeleteMeeting;
using Archiva.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Archiva.Application.FunctionalTests.Documents;

public class DocumentFailureTests : TestBase
{
    [Test]
    public async Task QueuePublicationFailureLeavesARecoverablePendingDocument()
    {
        var meetingId = await AddMeetingAsync();
        var storage = new Mock<IStorageService>();
        storage
            .Setup(service =>
                service.UploadAsync(
                    It.IsAny<Stream>(),
                    "minutes.txt",
                    "text/plain",
                    It.IsAny<CancellationToken>()
                )
            )
            .ReturnsAsync("blob/minutes.txt");
        storage
            .Setup(service =>
                service.GetReadUrlAsync("blob/minutes.txt", It.IsAny<CancellationToken>())
            )
            .ReturnsAsync("https://example.invalid/read?sig=test");
        var queue = new Mock<IDocumentAnalysisQueue>();
        queue
            .Setup(service => service.EnqueueAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Queue temporarily unavailable"));

        await TestApp.WithDbContextAsync(async db =>
        {
            var handler = new UploadDocumentCommandHandler(
                db,
                storage.Object,
                CurrentUser(),
                queue.Object,
                Options.Create(new DocumentAnalysisOptions()),
                NullLogger<UploadDocumentCommandHandler>.Instance
            );
            using var stream = new MemoryStream([1, 2, 3]);

            var result = await handler.Handle(
                new UploadDocumentCommand
                {
                    MeetingId = meetingId,
                    FileStream = stream,
                    FileName = "minutes.txt",
                    ContentType = "text/plain",
                    FileSizeInBytes = 3,
                },
                CancellationToken.None
            );

            result.Id.ShouldBeGreaterThan(0);
        });

        var persisted = await TestApp.WithDbContextAsync(db => db.Documents.SingleAsync());
        persisted.OrganizationId.ShouldBe(TestApp.Seed.FirstOrganizationId);
        persisted.AnalysisStatus.ShouldBe(Archiva.Domain.Enums.DocumentAnalysisStatus.Pending);
        queue.Verify(service => service.EnqueueAsync(persisted.Id, It.IsAny<CancellationToken>()));
    }

    [Test]
    public async Task BlobDeletionFailureKeepsMeetingAndDocumentForRetry()
    {
        var meetingId = await AddMeetingAsync();
        await TestApp.WithDbContextAsync(async db =>
        {
            db.Documents.Add(
                new Document
                {
                    MeetingId = meetingId,
                    OrganizationId = TestApp.Seed.FirstOrganizationId,
                    FileName = "minutes.txt",
                    FileType = "TXT",
                    BlobName = "blob/minutes.txt",
                }
            );
            await db.SaveChangesAsync();
        });
        var storage = new Mock<IStorageService>();
        storage
            .Setup(service =>
                service.DeleteAsync("blob/minutes.txt", It.IsAny<CancellationToken>())
            )
            .ThrowsAsync(new IOException("Blob temporarily unavailable"));

        await Should.ThrowAsync<IOException>(() =>
            TestApp.WithDbContextAsync(async db =>
            {
                var handler = new DeleteMeetingCommandHandler(db, storage.Object, CurrentUser());
                await handler.Handle(
                    new DeleteMeetingCommand { Id = meetingId },
                    CancellationToken.None
                );
            })
        );

        var persisted = await TestApp.WithDbContextAsync(async db => new
        {
            Meeting = await db.Meetings.AnyAsync(meeting => meeting.Id == meetingId),
            Document = await db.Documents.AnyAsync(document => document.MeetingId == meetingId),
        });
        persisted.Meeting.ShouldBeTrue();
        persisted.Document.ShouldBeTrue();
    }

    private static IUser CurrentUser()
    {
        var currentUser = new Mock<IUser>();
        currentUser.SetupGet(user => user.Id).Returns(TestSeed.FirstAdmin.Id);
        currentUser.SetupGet(user => user.Name).Returns(TestSeed.FirstAdmin.Name);
        return currentUser.Object;
    }

    private static async Task<int> AddMeetingAsync() =>
        await TestApp.WithDbContextAsync(async db =>
        {
            var meeting = new Meeting
            {
                OrganizationId = TestApp.Seed.FirstOrganizationId,
                Title = "Meeting with document",
                MeetingDate = DateTime.UtcNow.AddDays(2).Date,
                MeetingTime = TimeSpan.FromHours(10),
                CreatedById = TestSeed.FirstAdmin.Id,
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            return meeting.Id;
        });
}
