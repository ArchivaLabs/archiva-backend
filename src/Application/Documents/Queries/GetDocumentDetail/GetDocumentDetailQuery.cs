using Archiva.Application.Common.Interfaces;
using Archiva.Application.Documents.Dtos;
using Archiva.Domain.Entities;

namespace Archiva.Application.Documents.Queries.GetDocumentDetail;

public record GetDocumentDetailQuery(int DocumentId) : IRequest<DocumentDetailDto>;

public sealed class GetDocumentDetailQueryHandler
    : IRequestHandler<GetDocumentDetailQuery, DocumentDetailDto>
{
    private readonly IApplicationDbContext _context;
    private readonly IStorageService _storage;
    private readonly IUser _currentUser;

    public GetDocumentDetailQueryHandler(
        IApplicationDbContext context,
        IStorageService storage,
        IUser currentUser
    )
    {
        _context = context;
        _storage = storage;
        _currentUser = currentUser;
    }

    public async Task<DocumentDetailDto> Handle(
        GetDocumentDetailQuery request,
        CancellationToken cancellationToken
    )
    {
        var member =
            await _context.OrganizationUsers.FirstOrDefaultAsync(
                user => user.UserId == _currentUser.Id,
                cancellationToken
            ) ?? throw new UnauthorizedAccessException("User is not a member of any organization.");

        var document =
            await _context
                .Documents.Where(item =>
                    item.Id == request.DocumentId
                    && item.OrganizationId == member.OrganizationId
                    && item.Meeting.OrganizationId == member.OrganizationId
                )
                .Select(item => new
                {
                    item.Id,
                    item.FileName,
                    item.FileType,
                    item.FileSizeInBytes,
                    item.BlobName,
                    item.Description,
                    UploadedBy = item.CreatedBy,
                    item.Created,
                    item.MeetingId,
                    MeetingTitle = item.Meeting.Title,
                    item.Meeting.MeetingDate,
                    Tags = item.Meeting.Tags.Select(tag => tag.Tag.Name).ToList(),
                    item.AnalysisStatus,
                    item.AnalysisErrorCode,
                    item.Summary,
                    item.BillableUnitCount,
                    item.SummaryInputCharacters,
                    item.AnalysisCompletedAt,
                })
                .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(request.DocumentId.ToString(), nameof(Document));

        return new DocumentDetailDto
        {
            Id = document.Id,
            FileName = document.FileName,
            FileType = document.FileType,
            FileSizeInBytes = document.FileSizeInBytes,
            BlobUrl = await _storage.GetReadUrlAsync(document.BlobName, cancellationToken),
            Description = document.Description,
            UploadedBy = document.UploadedBy,
            Created = document.Created,
            MeetingId = document.MeetingId,
            MeetingTitle = document.MeetingTitle ?? string.Empty,
            MeetingDate = document.MeetingDate,
            Tags = document.Tags,
            AnalysisStatus = document.AnalysisStatus,
            AnalysisErrorCode = document.AnalysisErrorCode,
            Summary = document.Summary,
            BillableUnitCount = document.BillableUnitCount,
            SummaryInputCharacters = document.SummaryInputCharacters,
            AnalysisCompletedAt = document.AnalysisCompletedAt,
        };
    }
}
