using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddKnowledgeDocumentFailureActionsAndProcessingStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureAction",
                table: "knowledge_documents",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureTechnicalDetail",
                table: "knowledge_documents",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanRetry",
                table: "knowledge_documents",
                nullable: false,
                defaultValue: false);

            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    UPDATE knowledge_documents
                    SET "FailureAction" = COALESCE(NULLIF("FailureAction", ''), 'Re-save or export the file to PDF or DOCX and upload it again.')
                    WHERE "IngestionStatus" = 'failed';
                    """);

                migrationBuilder.Sql("""
                    ALTER TABLE knowledge_documents
                    DROP CONSTRAINT IF EXISTS "CK_knowledge_documents_ingestion_status";
                    """);

                migrationBuilder.Sql("""
                    ALTER TABLE knowledge_documents
                    ADD CONSTRAINT "CK_knowledge_documents_ingestion_status"
                    CHECK ("IngestionStatus" IN ('uploaded', 'processing', 'processed', 'failed'));
                    """);

                return;
            }

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    UPDATE [knowledge_documents]
                    SET [FailureAction] = COALESCE(NULLIF([FailureAction], ''), 'Re-save or export the file to PDF or DOCX and upload it again.')
                    WHERE [IngestionStatus] = 'failed';
                    """);

                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    DROP CONSTRAINT [CK_knowledge_documents_ingestion_status];
                    """);

                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    ADD CONSTRAINT [CK_knowledge_documents_ingestion_status]
                    CHECK ([IngestionStatus] IN ('uploaded', 'processing', 'processed', 'failed'));
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

                migrationBuilder.Sql("""
                    ALTER TABLE knowledge_documents
                    ADD CONSTRAINT "CK_knowledge_documents_ingestion_status"
                    CHECK ("IngestionStatus" IN ('uploaded', 'processed', 'failed'));
                    """);
            }

            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    DROP CONSTRAINT [CK_knowledge_documents_ingestion_status];
                    """);

                migrationBuilder.Sql("""
                    ALTER TABLE [knowledge_documents]
                    ADD CONSTRAINT [CK_knowledge_documents_ingestion_status]
                    CHECK ([IngestionStatus] IN ('uploaded', 'processed', 'failed'));
                    """);
            }

            migrationBuilder.DropColumn(
                name: "FailureAction",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "FailureTechnicalDetail",
                table: "knowledge_documents");

            migrationBuilder.DropColumn(
                name: "CanRetry",
                table: "knowledge_documents");
        }
    }
}