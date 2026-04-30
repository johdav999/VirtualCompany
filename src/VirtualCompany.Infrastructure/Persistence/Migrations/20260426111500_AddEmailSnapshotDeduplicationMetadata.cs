using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddEmailSnapshotDeduplicationMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "candidate_attachment_snapshot_count",
                table: "email_ingestion_runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "deduplicated_attachment_count",
                table: "email_ingestion_runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "non_candidate_message_count",
                table: "email_ingestion_runs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "candidate_decision",
                table: "email_message_snapshots",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "candidate");

            migrationBuilder.AddColumn<string>(
                name: "sender_domain",
                table: "email_message_snapshots",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "canonical_attachment_snapshot_id",
                table: "email_attachment_snapshots",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_duplicate_by_hash",
                table: "email_attachment_snapshots",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint("CK_email_ingestion_runs_candidate_attachment_snapshot_count_nonnegative", "email_ingestion_runs", "candidate_attachment_snapshot_count >= 0");
            migrationBuilder.AddCheckConstraint("CK_email_ingestion_runs_deduplicated_attachment_count_nonnegative", "email_ingestion_runs", "deduplicated_attachment_count >= 0");
            migrationBuilder.AddCheckConstraint("CK_email_ingestion_runs_non_candidate_message_count_nonnegative", "email_ingestion_runs", "non_candidate_message_count >= 0");
            migrationBuilder.AddCheckConstraint("CK_email_message_snapshots_candidate_decision", "email_message_snapshots", "candidate_decision IN ('candidate', 'not_candidate')");

            migrationBuilder.CreateIndex(
                name: "IX_email_message_snapshots_company_id_sender_domain",
                table: "email_message_snapshots",
                columns: new[] { "company_id", "sender_domain" });

            migrationBuilder.CreateIndex(
                name: "IX_email_attachment_snapshots_company_id_content_hash_is_duplicate_by_hash",
                table: "email_attachment_snapshots",
                columns: new[] { "company_id", "content_hash", "is_duplicate_by_hash" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_email_message_snapshots_company_id_sender_domain", table: "email_message_snapshots");
            migrationBuilder.DropIndex(name: "IX_email_attachment_snapshots_company_id_content_hash_is_duplicate_by_hash", table: "email_attachment_snapshots");

            migrationBuilder.DropCheckConstraint("CK_email_ingestion_runs_candidate_attachment_snapshot_count_nonnegative", "email_ingestion_runs");
            migrationBuilder.DropCheckConstraint("CK_email_ingestion_runs_deduplicated_attachment_count_nonnegative", "email_ingestion_runs");
            migrationBuilder.DropCheckConstraint("CK_email_ingestion_runs_non_candidate_message_count_nonnegative", "email_ingestion_runs");
            migrationBuilder.DropCheckConstraint("CK_email_message_snapshots_candidate_decision", "email_message_snapshots");

            migrationBuilder.DropColumn(name: "candidate_attachment_snapshot_count", table: "email_ingestion_runs");
            migrationBuilder.DropColumn(name: "deduplicated_attachment_count", table: "email_ingestion_runs");
            migrationBuilder.DropColumn(name: "non_candidate_message_count", table: "email_ingestion_runs");
            migrationBuilder.DropColumn(name: "candidate_decision", table: "email_message_snapshots");
            migrationBuilder.DropColumn(name: "sender_domain", table: "email_message_snapshots");
            migrationBuilder.DropColumn(name: "canonical_attachment_snapshot_id", table: "email_attachment_snapshots");
            migrationBuilder.DropColumn(name: "is_duplicate_by_hash", table: "email_attachment_snapshots");
        }
    }
}