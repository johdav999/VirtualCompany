using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260505083000_HardenWebsiteLeadCapture")]
    public partial class HardenWebsiteLeadCapture : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>("follow_up_sequence_id", "website_lead_submissions", "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<Guid>("sequence_execution_id", "website_lead_submissions", "uniqueidentifier", nullable: true);
            migrationBuilder.AddColumn<string>("phone", "website_lead_submissions", "nvarchar(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>("external_submission_id", "website_lead_submissions", "nvarchar(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>("source_metadata_json", "website_lead_submissions", "nvarchar(max)", nullable: true);
            migrationBuilder.AddColumn<string>("deduplication_decision", "website_lead_submissions", "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "new");
            migrationBuilder.AddColumn<string>("sequence_enrollment_status", "website_lead_submissions", "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "pending");

            migrationBuilder.CreateIndex(
                name: "IX_website_lead_submissions_company_id_external_submission_id",
                table: "website_lead_submissions",
                columns: new[] { "company_id", "external_submission_id" },
                unique: true,
                filter: "[external_submission_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_website_lead_submissions_company_id_follow_up_sequence_id_sequence_enrollment_status",
                table: "website_lead_submissions",
                columns: new[] { "company_id", "follow_up_sequence_id", "sequence_enrollment_status" });

            migrationBuilder.CreateIndex(
                name: "IX_website_lead_submissions_company_id_sequence_execution_id",
                table: "website_lead_submissions",
                columns: new[] { "company_id", "sequence_execution_id" },
                filter: "[sequence_execution_id] IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_website_lead_submissions_company_id_external_submission_id",
                table: "website_lead_submissions");

            migrationBuilder.DropIndex(
                name: "IX_website_lead_submissions_company_id_follow_up_sequence_id_sequence_enrollment_status",
                table: "website_lead_submissions");

            migrationBuilder.DropIndex(
                name: "IX_website_lead_submissions_company_id_sequence_execution_id",
                table: "website_lead_submissions");

            migrationBuilder.DropColumn("follow_up_sequence_id", "website_lead_submissions");
            migrationBuilder.DropColumn("sequence_execution_id", "website_lead_submissions");
            migrationBuilder.DropColumn("phone", "website_lead_submissions");
            migrationBuilder.DropColumn("external_submission_id", "website_lead_submissions");
            migrationBuilder.DropColumn("source_metadata_json", "website_lead_submissions");
            migrationBuilder.DropColumn("deduplication_decision", "website_lead_submissions");
            migrationBuilder.DropColumn("sequence_enrollment_status", "website_lead_submissions");
        }
    }
}
