using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class EnrichContextRetrievalSourceReferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Locator",
                table: "context_retrieval_sources",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentSourceEntityId",
                table: "context_retrieval_sources",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentSourceType",
                table: "context_retrieval_sources",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentTitle",
                table: "context_retrieval_sources",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SectionId",
                table: "context_retrieval_sources",
                maxLength: 64,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<int>(
                name: "SectionRank",
                table: "context_retrieval_sources",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "SectionTitle",
                table: "context_retrieval_sources",
                maxLength: 128,
                nullable: false,
                defaultValue: "Unknown");

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE context_retrieval_sources
                    SET "SectionId" = COALESCE(NULLIF("metadata_json"->>'retrievalSection', ''), 'unknown'),
                        "SectionTitle" = COALESCE(NULLIF("metadata_json"->>'retrievalSectionTitle', ''), 'Unknown'),
                        "SectionRank" = COALESCE(CAST(NULLIF("metadata_json"->>'retrievalSectionRank', '') AS integer), "Rank"),
                        "ParentSourceType" = CASE
                            WHEN "SourceType" = 'knowledge_chunk' THEN 'knowledge_document'
                            ELSE NULLIF("metadata_json"->>'parentSourceType', '')
                        END,
                        "ParentSourceEntityId" = CASE
                            WHEN "SourceType" = 'knowledge_chunk' THEN NULLIF("metadata_json"->>'documentId', '')
                            ELSE NULLIF("metadata_json"->>'parentSourceId', '')
                        END,
                        "ParentTitle" = COALESCE(NULLIF("metadata_json"->>'parentTitle', ''), CASE WHEN "SourceType" = 'knowledge_chunk' THEN "Title" ELSE NULL END),
                        "Locator" = COALESCE(NULLIF("metadata_json"->>'locator', ''), "Title");
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [context_retrieval_sources]
                    SET [SectionId] = COALESCE(NULLIF(JSON_VALUE([metadata_json], '$.retrievalSection'), N''), N'unknown'),
                        [SectionTitle] = COALESCE(NULLIF(JSON_VALUE([metadata_json], '$.retrievalSectionTitle'), N''), N'Unknown'),
                        [SectionRank] = COALESCE(TRY_CAST(JSON_VALUE([metadata_json], '$.retrievalSectionRank') AS int), [Rank]),
                        [ParentSourceType] = CASE
                            WHEN [SourceType] = N'knowledge_chunk' THEN N'knowledge_document'
                            ELSE NULLIF(JSON_VALUE([metadata_json], '$.parentSourceType'), N'')
                        END,
                        [ParentSourceEntityId] = CASE
                            WHEN [SourceType] = N'knowledge_chunk' THEN NULLIF(JSON_VALUE([metadata_json], '$.documentId'), N'')
                            ELSE NULLIF(JSON_VALUE([metadata_json], '$.parentSourceId'), N'')
                        END,
                        [ParentTitle] = COALESCE(NULLIF(JSON_VALUE([metadata_json], '$.parentTitle'), N''), CASE WHEN [SourceType] = N'knowledge_chunk' THEN [Title] ELSE NULL END),
                        [Locator] = COALESCE(NULLIF(JSON_VALUE([metadata_json], '$.locator'), N''), [Title]);
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_context_retrieval_sources_CompanyId_ParentSourceType_ParentSo~",
                table: "context_retrieval_sources",
                columns: new[] { "CompanyId", "ParentSourceType", "ParentSourceEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_context_retrieval_sources_CompanyId_RetrievalId_SectionId_Sect~",
                table: "context_retrieval_sources",
                columns: new[] { "CompanyId", "RetrievalId", "SectionId", "SectionRank" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_context_retrieval_sources_CompanyId_ParentSourceType_ParentSo~",
                table: "context_retrieval_sources");

            migrationBuilder.DropIndex(
                name: "IX_context_retrieval_sources_CompanyId_RetrievalId_SectionId_Sect~",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "Locator",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "ParentSourceEntityId",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "ParentSourceType",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "ParentTitle",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "SectionId",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "SectionRank",
                table: "context_retrieval_sources");

            migrationBuilder.DropColumn(
                name: "SectionTitle",
                table: "context_retrieval_sources");
        }
    }
}
