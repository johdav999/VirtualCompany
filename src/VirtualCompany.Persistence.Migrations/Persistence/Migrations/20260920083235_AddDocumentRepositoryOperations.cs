using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentRepositoryOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "retrieval_paused_at",
                table: "company_document_repository_connections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "synchronization_paused_at",
                table: "company_document_repository_connections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "writes_paused_at",
                table: "company_document_repository_connections",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "local_artifacts_purged_at",
                table: "company_document_publication_requests",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retrieval_paused_at",
                table: "company_document_repository_connections");

            migrationBuilder.DropColumn(
                name: "synchronization_paused_at",
                table: "company_document_repository_connections");

            migrationBuilder.DropColumn(
                name: "writes_paused_at",
                table: "company_document_repository_connections");

            migrationBuilder.DropColumn(
                name: "local_artifacts_purged_at",
                table: "company_document_publication_requests");
        }
    }
}
