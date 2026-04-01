using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class NormalizeKnowledgeDocumentIngestionStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    UPDATE knowledge_documents
                    SET "FailureCode" = COALESCE(NULLIF("FailureCode", ''), 'unsupported_document'),
                        "FailureMessage" = COALESCE(NULLIF("FailureMessage", ''), 'The document could not be ingested because the file type or contents are unsupported.'),
                        "FailedUtc" = COALESCE("FailedUtc", "UpdatedUtc", "CreatedUtc"),
                        "IngestionStatus" = 'failed'
                    WHERE "IngestionStatus" = 'unsupported';
                    """);

                migrationBuilder.Sql("""
                    UPDATE knowledge_documents
                    SET "FailureCode" = COALESCE(NULLIF("FailureCode", ''), 'processing_abandoned'),
                        "FailureMessage" = COALESCE(NULLIF("FailureMessage", ''), 'The document did not complete ingestion and must be retried.'),
                        "FailedUtc" = COALESCE("FailedUtc", "UpdatedUtc", "CreatedUtc"),
                        "IngestionStatus" = 'failed'
                    WHERE "IngestionStatus" = 'processing';
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "IngestionStatus",
                    table: "knowledge_documents",
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "uploaded",
                    oldClrType: typeof(string),
                    oldType: "character varying(32)",
                    oldMaxLength: 32);

                migrationBuilder.Sql("""
                    ALTER TABLE knowledge_documents
                    ADD CONSTRAINT "CK_knowledge_documents_ingestion_status"
                    CHECK ("IngestionStatus" IN ('uploaded', 'processed', 'failed'));
                    """);

                return;
            }

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    UPDATE [knowledge_documents]
                    SET [FailureCode] = COALESCE(NULLIF([FailureCode], ''), 'unsupported_document'),
                        [FailureMessage] = COALESCE(NULLIF([FailureMessage], ''), 'The document could not be ingested because the file type or contents are unsupported.'),
                        [FailedUtc] = COALESCE([FailedUtc], [UpdatedUtc], [CreatedUtc]),
                        [IngestionStatus] = 'failed'
                    WHERE [IngestionStatus] = 'unsupported';
                    """);

                migrationBuilder.Sql("""
                    UPDATE [knowledge_documents]
                    SET [FailureCode] = COALESCE(NULLIF([FailureCode], ''), 'processing_abandoned'),
                        [FailureMessage] = COALESCE(NULLIF([FailureMessage], ''), 'The document did not complete ingestion and must be retried.'),
                        [FailedUtc] = COALESCE([FailedUtc], [UpdatedUtc], [CreatedUtc]),
                        [IngestionStatus] = 'failed'
                    WHERE [IngestionStatus] = 'processing';
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "IngestionStatus",
                    table: "knowledge_documents",
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false,
                    defaultValue: "uploaded",
                    oldClrType: typeof(string),
                    oldType: "nvarchar(32)",
                    oldMaxLength: 32);

                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    ADD CONSTRAINT [CK_knowledge_documents_ingestion_status]
                    CHECK ([IngestionStatus] IN ('uploaded', 'processed', 'failed'));
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    ALTER TABLE knowledge_documents
                    DROP CONSTRAINT IF EXISTS "CK_knowledge_documents_ingestion_status";
                    """);

                migrationBuilder.AlterColumn<string>(
                    name: "IngestionStatus",
                    table: "knowledge_documents",
                    type: "character varying(32)",
                    maxLength: 32,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "character varying(32)",
                    oldMaxLength: 32,
                    oldDefaultValue: "uploaded");

                return;
            }

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    DROP CONSTRAINT [CK_knowledge_documents_ingestion_status];
                    """);
            }
        }
    }
}
