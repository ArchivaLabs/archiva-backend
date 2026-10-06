using System.Data;
using System.Text.Json;
using Archiva.Application.Common.Interfaces;
using Archiva.Application.Search.Dtos;
using Archiva.Application.Search.Queries;
using Archiva.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Archiva.Infrastructure.Search;

public sealed class SqlDocumentSearchService : IDocumentSearchService
{
    private const int SnippetLength = 260;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<SqlDocumentSearchService> _logger;

    public SqlDocumentSearchService(
        ApplicationDbContext context,
        ILogger<SqlDocumentSearchService> logger
    )
    {
        _context = context;
        _logger = logger;
    }

    public async Task<SearchResultsPageDto> SearchAsync(
        SearchDocumentsQuery request,
        int organizationId,
        CancellationToken cancellationToken = default
    )
    {
        var connection = (SqlConnection)_context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var supportsFullText = await HasFullTextIndexesAsync(connection, cancellationToken);
            if (!supportsFullText && !string.IsNullOrWhiteSpace(request.SearchTerm))
            {
                _logger.LogInformation(
                    "SQL full-text search is unavailable; using the development LIKE fallback"
                );
            }

            var branches = BuildBranches(request, supportsFullText);
            if (branches.Count == 0)
            {
                return new SearchResultsPageDto
                {
                    Page = request.Page,
                    PageSize = request.PageSize,
                };
            }

            var sql = BuildQuery(branches, request.SortBy);
            var parameters = CreateParameters(request, organizationId);
            int totalCount;

            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = sql.CountSql;
                countCommand.Parameters.AddRange(parameters);
                totalCount = Convert.ToInt32(
                    await countCommand.ExecuteScalarAsync(cancellationToken)
                );
            }

            var results = new List<SearchResultDto>();
            await using (var pageCommand = connection.CreateCommand())
            {
                pageCommand.CommandText = sql.PageSql;
                pageCommand.Parameters.AddRange(CreateParameters(request, organizationId));
                pageCommand.Parameters.Add(
                    new SqlParameter("@Offset", SqlDbType.Int)
                    {
                        Value = (request.Page - 1) * request.PageSize,
                    }
                );
                pageCommand.Parameters.Add(
                    new SqlParameter("@PageSize", SqlDbType.Int) { Value = request.PageSize }
                );

                await using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(
                        new SearchResultDto
                        {
                            Id = reader.GetInt32(0),
                            Type = reader.GetString(1),
                            MeetingId = reader.GetInt32(2),
                            MeetingDate = reader.GetDateTime(3),
                            Title = reader.GetString(4),
                            Snippet = reader.GetString(5),
                            Source = reader.GetString(6),
                            FileType = reader.IsDBNull(7) ? null : reader.GetString(7),
                        }
                    );
                }
            }

            if (results.Count > 0)
            {
                var meetingIds = results.Select(result => result.MeetingId).Distinct().ToArray();
                var tags = await _context
                    .MeetingTags.Where(meetingTag =>
                        meetingIds.Contains(meetingTag.MeetingId)
                        && meetingTag.Meeting.OrganizationId == organizationId
                    )
                    .Select(meetingTag => new { meetingTag.MeetingId, meetingTag.Tag.Name })
                    .ToListAsync(cancellationToken);

                var tagsByMeeting = tags.GroupBy(tag => tag.MeetingId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(tag => tag.Name).ToList()
                    );

                results = results
                    .Select(result =>
                        result with
                        {
                            Tags = tagsByMeeting.GetValueOrDefault(result.MeetingId) ?? [],
                        }
                    )
                    .ToList();
            }

            return new SearchResultsPageDto
            {
                Results = results,
                TotalCount = totalCount,
                Page = request.Page,
                PageSize = request.PageSize,
            };
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    private static List<string> BuildBranches(SearchDocumentsQuery request, bool supportsFullText)
    {
        var branches = new List<string>();
        var hasTerm = !string.IsNullOrWhiteSpace(request.SearchTerm);
        var searchTerm = request.SearchTerm.Trim();

        if (request.IncludeMeetings)
        {
            if (!hasTerm)
            {
                branches.Add(BuildMeetingBranch("CAST(0 AS int)", string.Empty));
            }
            else
            {
                if (request.SearchTitles)
                {
                    branches.Add(
                        supportsFullText
                            ? BuildMeetingBranch(
                                "matches.[RANK]",
                                "INNER JOIN FREETEXTTABLE(dbo.Meetings, Title, @SearchTerm) AS matches ON matches.[KEY] = m.Id"
                            )
                            : BuildMeetingBranch(
                                "CAST(0 AS int)",
                                string.Empty,
                                "AND m.Title LIKE N'%' + @SearchTerm + N'%'"
                            )
                    );
                }

                if (request.SearchContent)
                {
                    branches.Add(
                        supportsFullText
                            ? BuildMeetingBranch(
                                "matches.[RANK]",
                                "INNER JOIN FREETEXTTABLE(dbo.Meetings, Description, @SearchTerm) AS matches ON matches.[KEY] = m.Id"
                            )
                            : BuildMeetingBranch(
                                "CAST(0 AS int)",
                                string.Empty,
                                "AND m.Description LIKE N'%' + @SearchTerm + N'%'"
                            )
                    );
                }
            }
        }

        if (request.IncludeDocuments)
        {
            if (!hasTerm)
            {
                branches.Add(BuildDocumentBranch("CAST(0 AS int)", string.Empty));
            }
            else
            {
                if (request.SearchTitles)
                {
                    branches.Add(
                        supportsFullText
                            ? BuildDocumentBranch(
                                "matches.[RANK]",
                                "INNER JOIN FREETEXTTABLE(dbo.Documents, FileName, @SearchTerm) AS matches ON matches.[KEY] = d.Id"
                            )
                            : BuildDocumentBranch(
                                "CAST(0 AS int)",
                                string.Empty,
                                "AND d.FileName LIKE N'%' + @SearchTerm + N'%'"
                            )
                    );
                }

                if (request.SearchContent)
                {
                    branches.Add(
                        supportsFullText
                            ? BuildDocumentBranch(
                                "matches.[RANK]",
                                "INNER JOIN FREETEXTTABLE(dbo.Documents, (ExtractedText, Summary), @SearchTerm) AS matches ON matches.[KEY] = d.Id"
                            )
                            : BuildDocumentBranch(
                                "CAST(0 AS int)",
                                string.Empty,
                                "AND (d.ExtractedText LIKE N'%' + @SearchTerm + N'%' OR d.Summary LIKE N'%' + @SearchTerm + N'%')"
                            )
                    );
                }
            }
        }

        return branches;
    }

    private static string BuildMeetingBranch(
        string rank,
        string fullTextJoin,
        string searchPredicate = ""
    ) =>
        $"""
            SELECT N'meeting' AS ResultType,
                   m.Id,
                   m.Id AS MeetingId,
                   m.MeetingDate,
                   COALESCE(m.Title, N'') AS Title,
                   LEFT(COALESCE(m.Description, N''), {SnippetLength}) AS Snippet,
                   COALESCE(m.Title, N'Meeting') AS Source,
                   CAST(NULL AS nvarchar(20)) AS FileType,
                   {rank} AS SearchRank
            FROM dbo.Meetings AS m
            {fullTextJoin}
            WHERE m.OrganizationId = @OrganizationId
              AND (@DateFrom IS NULL OR m.MeetingDate >= @DateFrom)
              AND (@DateToExclusive IS NULL OR m.MeetingDate < @DateToExclusive)
              AND (@HasTagFilter = 0 OR EXISTS (
                  SELECT 1
                  FROM OPENJSON(@TagsJson) AS requested
                  INNER JOIN dbo.MeetingTags AS mt ON mt.MeetingId = m.Id
                  INNER JOIN dbo.Tags AS t ON t.Id = mt.TagId AND t.OrganizationId = @OrganizationId
                  WHERE t.Name = requested.[value]
              ))
              {searchPredicate}
            """;

    private static string BuildDocumentBranch(
        string rank,
        string fullTextJoin,
        string searchPredicate = ""
    ) =>
        $"""
            SELECT N'document' AS ResultType,
                   d.Id,
                   m.Id AS MeetingId,
                   m.MeetingDate,
                   d.FileName AS Title,
                   LEFT(COALESCE(NULLIF(d.ExtractedText, N''), NULLIF(d.Summary, N''), NULLIF(d.Description, N''), N''), {SnippetLength}) AS Snippet,
                   COALESCE(m.Title, N'Meeting') AS Source,
                   d.FileType,
                   {rank} AS SearchRank
            FROM dbo.Documents AS d
            INNER JOIN dbo.Meetings AS m ON m.Id = d.MeetingId
            {fullTextJoin}
            WHERE d.OrganizationId = @OrganizationId
              AND m.OrganizationId = @OrganizationId
              AND (@DateFrom IS NULL OR m.MeetingDate >= @DateFrom)
              AND (@DateToExclusive IS NULL OR m.MeetingDate < @DateToExclusive)
              AND (@HasTagFilter = 0 OR EXISTS (
                  SELECT 1
                  FROM OPENJSON(@TagsJson) AS requested
                  INNER JOIN dbo.MeetingTags AS mt ON mt.MeetingId = m.Id
                  INNER JOIN dbo.Tags AS t ON t.Id = mt.TagId AND t.OrganizationId = @OrganizationId
                  WHERE t.Name = requested.[value]
              ))
              {searchPredicate}
            """;

    private static async Task<bool> HasFullTextIndexesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE
                WHEN FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
                 AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Meetings'))
                 AND EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Documents'))
                THEN 1
                ELSE 0
            END;
            """;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private static (string CountSql, string PageSql) BuildQuery(
        List<string> branches,
        string sortBy
    )
    {
        var candidateSql = string.Join("\nUNION ALL\n", branches);
        var orderBy = sortBy switch
        {
            "date_asc" => "MeetingDate ASC, ResultType, Id",
            "date_desc" => "MeetingDate DESC, ResultType, Id",
            _ => "SearchRank DESC, MeetingDate DESC, ResultType, Id",
        };
        var common = $"""
            WITH Candidates AS (
                {candidateSql}
            ), Ranked AS (
                SELECT ResultType, Id, MeetingId, MeetingDate, Title, Snippet, Source, FileType,
                       SearchRank,
                       ROW_NUMBER() OVER (PARTITION BY ResultType, Id ORDER BY SearchRank DESC) AS RowNumber
                FROM Candidates
            ), Filtered AS (
                SELECT ResultType, Id, MeetingId, MeetingDate, Title, Snippet, Source, FileType, SearchRank
                FROM Ranked
                WHERE RowNumber = 1
            )
            """;

        return (
            common + " SELECT COUNT_BIG(*) FROM Filtered;",
            common
                + $" SELECT Id, ResultType, MeetingId, MeetingDate, Title, Snippet, Source, FileType FROM Filtered ORDER BY {orderBy} OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;"
        );
    }

    private static SqlParameter[] CreateParameters(SearchDocumentsQuery request, int organizationId)
    {
        var dateToExclusive = request.DateTo?.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var searchTerm = string.IsNullOrWhiteSpace(request.SearchTerm)
            ? string.Empty
            : request.SearchTerm.Trim();

        return
        [
            new SqlParameter("@OrganizationId", SqlDbType.Int) { Value = organizationId },
            new SqlParameter("@SearchTerm", SqlDbType.NVarChar, 200) { Value = searchTerm },
            new SqlParameter("@DateFrom", SqlDbType.DateTime2)
            {
                Value = request.DateFrom?.ToDateTime(TimeOnly.MinValue) is { } from
                    ? from
                    : DBNull.Value,
            },
            new SqlParameter("@DateToExclusive", SqlDbType.DateTime2)
            {
                Value = dateToExclusive is { } to ? to : DBNull.Value,
            },
            new SqlParameter("@HasTagFilter", SqlDbType.Bit) { Value = request.Tags.Count > 0 },
            new SqlParameter("@TagsJson", SqlDbType.NVarChar, -1)
            {
                Value = JsonSerializer.Serialize(request.Tags),
            },
        ];
    }
}
