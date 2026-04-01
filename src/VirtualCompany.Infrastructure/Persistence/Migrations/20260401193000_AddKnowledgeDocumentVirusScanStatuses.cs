using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddKnowledgeDocumentVirusScanStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
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
                    CHECK ("IngestionStatus" IN ('uploaded', 'pending_scan', 'scan_clean', 'processing', 'processed', 'blocked', 'failed'));
                    """);

                return;
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
                    CHECK ([IngestionStatus] IN ('uploaded', 'pending_scan', 'scan_clean', 'processing', 'processed', 'blocked', 'failed'));
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
                    CHECK ("IngestionStatus" IN ('uploaded', 'processing', 'processed', 'failed'));
                    """);

                return;
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
                    CHECK ([IngestionStatus] IN ('uploaded', 'processing', 'processed', 'failed'));
                    """);
            }
        }
    }
}