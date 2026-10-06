using Archiva.Application.Search.Dtos;
using Archiva.Application.Search.Queries;

namespace Archiva.Application.Common.Interfaces;

public interface IDocumentSearchService
{
    Task<SearchResultsPageDto> SearchAsync(
        SearchDocumentsQuery request,
        int organizationId,
        CancellationToken cancellationToken = default
    );
}
