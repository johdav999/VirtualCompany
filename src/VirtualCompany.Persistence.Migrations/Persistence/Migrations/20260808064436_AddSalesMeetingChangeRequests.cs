using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_meeting_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operation = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    starts_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ends_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    create_online_meeting = table.Column<bool>(type: "bit", nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    execution_attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_change_requests", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_change_requests_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_change_requests_payload", "(operation = 'cancel') OR (starts_at IS NOT NULL AND ends_at IS NOT NULL AND ends_at > starts_at AND time_zone_id IS NOT NULL AND title IS NOT NULL AND description IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_sales_meeting_change_requests_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_change_requests_sales_meeting_invitations_company_id_invitation_id",
                        columns: x => new { x.company_id, x.invitation_id },
                        principalTable: "sales_meeting_invitations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_requests_company_id_approval_request_id",
                table: "sales_meeting_change_requests",
                columns: new[] { "company_id", "approval_request_id" },
                filter: "[approval_request_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_requests_company_id_idempotency_key",
                table: "sales_meeting_change_requests",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_requests_company_id_invitation_id_created_at",
                table: "sales_meeting_change_requests",
                columns: new[] { "company_id", "invitation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_change_requests_company_id_status_updated_at",
                table: "sales_meeting_change_requests",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_change_requests");
        }
    }
}
