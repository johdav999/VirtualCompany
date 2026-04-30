using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddEmailBillDetectionSnapshots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_message_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mailbox_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email_ingestion_run_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_message_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    from_address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    from_display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    folder_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    folder_display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    body_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    untrusted_body_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    matched_rules_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "'[]'"),
                    reason_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_message_snapshots", x => x.id);
                    table.UniqueConstraint("AK_email_message_snapshots_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_email_message_snapshots_source_type", "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only')");
                    table.ForeignKey(
                        name: "FK_email_message_snapshots_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_message_snapshots_email_ingestion_runs_email_ingestion_run_id",
                        column: x => x.email_ingestion_run_id,
                        principalTable: "email_ingestion_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_message_snapshots_mailbox_connections_mailbox_connection_id",
                        column: x => x.mailbox_connection_id,
                        principalTable: "mailbox_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "email_attachment_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email_message_snapshot_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    external_attachment_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    file_name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    mime_type = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    content_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    storage_reference = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    untrusted_extracted_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_attachment_snapshots", x => x.id);
                    table.CheckConstraint("CK_email_attachment_snapshots_size_nonnegative", "size_bytes IS NULL OR size_bytes >= 0");
                    table.CheckConstraint("CK_email_attachment_snapshots_source_type", "source_type IN ('pdf_attachment', 'docx_attachment', 'email_body_only')");
                    table.ForeignKey(
                        name: "FK_email_attachment_snapshots_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_email_attachment_snapshots_email_message_snapshots_email_message_snapshot_id",
                        column: x => x.email_message_snapshot_id,
                        principalTable: "email_message_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_company_id", table: "email_message_snapshots", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_company_id_email_ingestion_run_id", table: "email_message_snapshots", columns: new[] { "company_id", "email_ingestion_run_id" });
            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_company_id_mailbox_connection_id_external_message_id", table: "email_message_snapshots", columns: new[] { "company_id", "mailbox_connection_id", "external_message_id" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_company_id_source_type_created_at", table: "email_message_snapshots", columns: new[] { "company_id", "source_type", "created_at" });
            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_email_ingestion_run_id", table: "email_message_snapshots", column: "email_ingestion_run_id");
            migrationBuilder.CreateIndex(name: "IX_email_message_snapshots_mailbox_connection_id", table: "email_message_snapshots", column: "mailbox_connection_id");

            migrationBuilder.CreateIndex(name: "IX_email_attachment_snapshots_company_id", table: "email_attachment_snapshots", column: "company_id");
            migrationBuilder.CreateIndex(name: "IX_email_attachment_snapshots_company_id_content_hash", table: "email_attachment_snapshots", columns: new[] { "company_id", "content_hash" });
            migrationBuilder.CreateIndex(name: "IX_email_attachment_snapshots_company_id_email_message_snapshot_id", table: "email_attachment_snapshots", columns: new[] { "company_id", "email_message_snapshot_id" });
            migrationBuilder.CreateIndex(name: "IX_email_attachment_snapshots_company_id_email_message_snapshot_id_external_attachment_id", table: "email_attachment_snapshots", columns: new[] { "company_id", "email_message_snapshot_id", "external_attachment_id" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_email_attachment_snapshots_email_message_snapshot_id", table: "email_attachment_snapshots", column: "email_message_snapshot_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "email_attachment_snapshots");
            migrationBuilder.DropTable(name: "email_message_snapshots");
        }
    }
}
