using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentPublicationUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "conflict_remote_version",
                table: "company_document_publication_requests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "expected_remote_version",
                table: "company_document_publication_requests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation_kind",
                table: "company_document_publication_requests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "create");

            migrationBuilder.AddColumn<string>(
                name: "original_content_sha256",
                table: "company_document_publication_requests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_evidence_version",
                table: "company_document_publication_requests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_size_bytes",
                table: "company_document_publication_requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_storage_key",
                table: "company_document_publication_requests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stale_publication_request_id",
                table: "company_document_publication_requests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_item_id",
                table: "company_document_publication_requests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "conflict_remote_version",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "expected_remote_version",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "operation_kind",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "original_content_sha256",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "original_evidence_version",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "original_size_bytes",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "original_storage_key",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "stale_publication_request_id",
                table: "company_document_publication_requests");

            migrationBuilder.DropColumn(
                name: "target_item_id",
                table: "company_document_publication_requests");
        }
    }
}
