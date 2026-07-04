using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Microsoft.EntityFrameworkCore.Migrations.Migration("20260402210000_EnrichContextRetrievalSourceReferences")]
    public partial class EnrichContextRetrievalSourceReferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    IF OBJECT_ID(N'[context_retrievals]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [context_retrievals] (
                            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_context_retrievals] PRIMARY KEY,
                            [CompanyId] uniqueidentifier NOT NULL,
                            [AgentId] uniqueidentifier NOT NULL,
                            [ActorUserId] uniqueidentifier NULL,
                            [TaskId] uniqueidentifier NULL,
                            [QueryText] nvarchar(4000) NOT NULL,
                            [QueryHash] nvarchar(128) NOT NULL,
                            [CorrelationId] nvarchar(128) NULL,
                            [RetrievalPurpose] nvarchar(256) NULL,
                            [CreatedUtc] datetime2 NOT NULL,
                            CONSTRAINT [FK_context_retrievals_companies_CompanyId]
                                FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE CASCADE
                        );

                        CREATE INDEX [IX_context_retrievals_CompanyId_AgentId_CreatedUtc]
                            ON [context_retrievals] ([CompanyId], [AgentId], [CreatedUtc]);

                        CREATE INDEX [IX_context_retrievals_CompanyId_TaskId_CreatedUtc]
                            ON [context_retrievals] ([CompanyId], [TaskId], [CreatedUtc]);
                    END;

                    IF OBJECT_ID(N'[context_retrieval_sources]', N'U') IS NULL
                    BEGIN
                        CREATE TABLE [context_retrieval_sources] (
                            [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_context_retrieval_sources] PRIMARY KEY,
                            [CompanyId] uniqueidentifier NOT NULL,
                            [RetrievalId] uniqueidentifier NOT NULL,
                            [SourceType] nvarchar(64) NOT NULL,
                            [SourceEntityId] nvarchar(128) NOT NULL,
                            [Title] nvarchar(256) NOT NULL,
                            [Snippet] nvarchar(4000) NOT NULL,
                            [Rank] int NOT NULL,
                            [Score] decimal(18,6) NULL,
                            [TimestampUtc] datetime2 NULL,
                            [CreatedUtc] datetime2 NOT NULL,
                            [metadata_json] nvarchar(max) NOT NULL CONSTRAINT [DF_context_retrieval_sources_metadata_json_startup] DEFAULT (N'{}'),
                            CONSTRAINT [FK_context_retrieval_sources_companies_CompanyId]
                                FOREIGN KEY ([CompanyId]) REFERENCES [companies] ([Id]) ON DELETE NO ACTION,
                            CONSTRAINT [FK_context_retrieval_sources_context_retrievals_RetrievalId]
                                FOREIGN KEY ([RetrievalId]) REFERENCES [context_retrievals] ([Id]) ON DELETE CASCADE
                        );

                        CREATE UNIQUE INDEX [IX_context_retrieval_sources_CompanyId_RetrievalId_Rank]
                            ON [context_retrieval_sources] ([CompanyId], [RetrievalId], [Rank]);

                        CREATE INDEX [IX_context_retrieval_sources_CompanyId_SourceType_SourceEntityId]
                            ON [context_retrieval_sources] ([CompanyId], [SourceType], [SourceEntityId]);
                    END;
                    """);
            }

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
