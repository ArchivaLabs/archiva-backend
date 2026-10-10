using System.Net;
using System.Net.Http.Json;
using System.Text;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Entities;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Archiva.Application.FunctionalTests.Documents;

public class DocumentEndpointTests : TestBase
{
    [Test]
    public async Task UploadListDetailAndContentStayInsideTheCurrentOrganization()
    {
        var meetingId = await AddMeetingAsync(TestApp.Seed.FirstOrganizationId);
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);
        var bytes = Encoding.UTF8.GetBytes("Private meeting decisions.");
        using var form = Form("minutes.txt", bytes);

        var upload = await client.PostAsync($"/api/meetings/{meetingId}/documents", form);

        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var uploaded = await upload.Content.ReadFromJsonAsync<DocumentDto>();
        uploaded.ShouldNotBeNull();
        uploaded.FileName.ShouldBe("minutes.txt");
        uploaded.BlobUrl.ShouldContain("sig=");

        var list = await client.GetFromJsonAsync<List<DocumentDto>>(
            $"/api/meetings/{meetingId}/documents"
        );
        list.ShouldNotBeNull();
        list.Select(document => document.Id).ShouldContain(uploaded.Id);

        var detail = await client.GetAsync($"/api/documents/{uploaded.Id}");
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await client.GetAsync($"/api/documents/{uploaded.Id}/content");
        content.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await content.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
        content.Content.Headers.ContentType!.MediaType.ShouldBe("text/plain");
        content.Headers.CacheControl!.NoStore.ShouldBeTrue();
        content.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");

        using var otherClient = TestApp.ClientFor(TestSeed.SecondAdmin);
        (await otherClient.GetAsync($"/api/meetings/{meetingId}/documents")).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (await otherClient.GetAsync($"/api/documents/{uploaded.Id}")).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (await otherClient.GetAsync($"/api/documents/{uploaded.Id}/content")).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Test]
    public async Task UploadRejectsForeignMeetingBeforeWritingDocumentOrBlob()
    {
        var foreignMeetingId = await AddMeetingAsync(TestApp.Seed.SecondOrganizationId);
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);
        using var form = Form("private.txt", Encoding.UTF8.GetBytes("sensitive"));

        var response = await client.PostAsync($"/api/meetings/{foreignMeetingId}/documents", form);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var count = await TestApp.WithDbContextAsync(db => db.Documents.CountAsync());
        count.ShouldBe(0);
        await using var scope = FunctionalTestSetup.Factory.Services.CreateAsyncScope();
        var container = scope
            .ServiceProvider.GetRequiredService<BlobServiceClient>()
            .GetBlobContainerClient("documents");
        (await container.ExistsAsync()).Value.ShouldBeFalse();
    }

    [TestCase("minutes.exe", "text/plain", new byte[] { 1 })]
    [TestCase("minutes.txt", "text/plain", new byte[0])]
    public async Task UploadRejectsInvalidFiles(string fileName, string contentType, byte[] bytes)
    {
        var meetingId = await AddMeetingAsync(TestApp.Seed.FirstOrganizationId);
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);
        using var form = Form(fileName, bytes, contentType);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/documents", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TestApp.WithDbContextAsync(db => db.Documents.CountAsync())).ShouldBe(0);
    }

    [Test]
    public async Task UploadRejectsFilesLargerThanTenMegabytes()
    {
        var meetingId = await AddMeetingAsync(TestApp.Seed.FirstOrganizationId);
        using var client = TestApp.ClientFor(TestSeed.FirstAdmin);
        using var form = Form("large.txt", new byte[10 * 1024 * 1024 + 1]);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/documents", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TestApp.WithDbContextAsync(db => db.Documents.CountAsync())).ShouldBe(0);
    }

    [Test]
    public async Task ContentRequiresAuthenticationAndMembership()
    {
        var meetingId = await AddMeetingAsync(TestApp.Seed.FirstOrganizationId);
        var documentId = await TestApp.WithDbContextAsync(async db =>
        {
            var document = new Document
            {
                MeetingId = meetingId,
                OrganizationId = TestApp.Seed.FirstOrganizationId,
                FileName = "private.txt",
                FileType = "TXT",
                BlobName = "private/private.txt",
            };
            db.Documents.Add(document);
            await db.SaveChangesAsync();
            return document.Id;
        });
        using var anonymous = TestApp.AnonymousClient();
        using var outsider = TestApp.ClientFor(TestSeed.Outsider);

        (await anonymous.GetAsync($"/api/documents/{documentId}/content")).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized
        );
        (await outsider.GetAsync($"/api/documents/{documentId}/content")).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized
        );
    }

    private static async Task<int> AddMeetingAsync(int organizationId) =>
        await TestApp.WithDbContextAsync(async db =>
        {
            var meeting = new Meeting
            {
                OrganizationId = organizationId,
                Title = "Board meeting",
                MeetingDate = DateTime.UtcNow.AddDays(2).Date,
                MeetingTime = new TimeSpan(10, 0, 0),
                CreatedById = TestSeed.FirstAdmin.Id,
            };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            return meeting.Id;
        });

    private static MultipartFormDataContent Form(
        string fileName,
        byte[] bytes,
        string contentType = "text/plain"
    )
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent("Board notes"), "description");
        return form;
    }
}
