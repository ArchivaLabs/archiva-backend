using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Archiva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DocumentAnalysisAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisAttemptCount') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisAttemptCount int NOT NULL DEFAULT 0;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisCompletedAt') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisCompletedAt datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisErrorCode') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisErrorCode nvarchar(max) NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisLastAttemptAt') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisLastAttemptAt datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisLeaseUntil') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisLeaseUntil datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisQueuedAt') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisQueuedAt datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisStartedAt') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisStartedAt datetimeoffset NULL;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisStatus') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisStatus int NOT NULL DEFAULT 1;
                IF COL_LENGTH(N'dbo.Documents', N'AnalysisUnitLimit') IS NULL
                    ALTER TABLE dbo.Documents ADD AnalysisUnitLimit int NOT NULL DEFAULT 20;
                IF COL_LENGTH(N'dbo.Documents', N'BillableUnitCount') IS NULL
                    ALTER TABLE dbo.Documents ADD BillableUnitCount int NOT NULL DEFAULT 0;
                IF COL_LENGTH(N'dbo.Documents', N'Summary') IS NULL
                    ALTER TABLE dbo.Documents ADD Summary nvarchar(max) NULL;
                IF COL_LENGTH(N'dbo.Documents', N'SummaryInputCharacterLimit') IS NULL
                    ALTER TABLE dbo.Documents ADD SummaryInputCharacterLimit int NOT NULL DEFAULT 100000;
                IF COL_LENGTH(N'dbo.Documents', N'SummaryInputCharacters') IS NULL
                    ALTER TABLE dbo.Documents ADD SummaryInputCharacters int NOT NULL DEFAULT 0;

                IF OBJECT_ID(N'dbo.DocumentAnalysisUsages', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.DocumentAnalysisUsages (
                        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_DocumentAnalysisUsages PRIMARY KEY,
                        DocumentId int NOT NULL,
                        MonthStartUtc datetime2 NOT NULL,
                        BillableUnits int NOT NULL,
                        SummaryInputCharacters int NOT NULL
                    );
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_DocumentAnalysisUsages_DocumentId_MonthStartUtc'
                      AND object_id = OBJECT_ID(N'dbo.DocumentAnalysisUsages')
                )
                    CREATE UNIQUE INDEX IX_DocumentAnalysisUsages_DocumentId_MonthStartUtc
                        ON dbo.DocumentAnalysisUsages (DocumentId, MonthStartUtc);

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = N'IX_DocumentAnalysisUsages_MonthStartUtc'
                      AND object_id = OBJECT_ID(N'dbo.DocumentAnalysisUsages')
                )
                    CREATE INDEX IX_DocumentAnalysisUsages_MonthStartUtc
                        ON dbo.DocumentAnalysisUsages (MonthStartUtc);

                IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ArchivaFullTextCatalog'
                    )
                        EXEC(N'CREATE FULLTEXT CATALOG [ArchivaFullTextCatalog] AS DEFAULT;');

                    IF NOT EXISTS (
                        SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Meetings')
                    )
                        EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Meetings]
                            ([Title] LANGUAGE 1033, [Description] LANGUAGE 1033)
                            KEY INDEX [PK_Meetings]
                            ON [ArchivaFullTextCatalog]
                            WITH CHANGE_TRACKING AUTO;');

                    IF NOT EXISTS (
                        SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Documents')
                    )
                        EXEC(N'CREATE FULLTEXT INDEX ON [dbo].[Documents]
                            ([FileName] LANGUAGE 1033, [ExtractedText] LANGUAGE 1033, [Summary] LANGUAGE 1033)
                            KEY INDEX [PK_Documents]
                            ON [ArchivaFullTextCatalog]
                            WITH CHANGE_TRACKING AUTO;');
                END;
                """,
                suppressTransaction: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Documents'))
                    DROP FULLTEXT INDEX ON [dbo].[Documents];
                IF EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.Meetings'))
                    DROP FULLTEXT INDEX ON [dbo].[Meetings];
                IF EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'ArchivaFullTextCatalog')
                    DROP FULLTEXT CATALOG [ArchivaFullTextCatalog];
                """,
                suppressTransaction: true
            );

            migrationBuilder.DropTable(name: "DocumentAnalysisUsages");

            migrationBuilder.DropColumn(name: "AnalysisAttemptCount", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisCompletedAt", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisErrorCode", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisLastAttemptAt", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisLeaseUntil", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisQueuedAt", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisStartedAt", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisStatus", table: "Documents");

            migrationBuilder.DropColumn(name: "AnalysisUnitLimit", table: "Documents");

            migrationBuilder.DropColumn(name: "BillableUnitCount", table: "Documents");

            migrationBuilder.DropColumn(name: "Summary", table: "Documents");

            migrationBuilder.DropColumn(name: "SummaryInputCharacterLimit", table: "Documents");

            migrationBuilder.DropColumn(name: "SummaryInputCharacters", table: "Documents");
        }
    }
}
