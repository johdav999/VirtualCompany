using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_meeting_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    meeting_goal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    intended_audience = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    planned_duration_minutes = table.Column<int>(type: "int", nullable: false),
                    demo_scenario = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    provider_meeting_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    current_slide_index = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    current_talking_point_index = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    resume_marker = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    consent_status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    consent_recorded_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    consent_recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    retention_policy = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    retention_days = table.Column<int>(type: "int", nullable: false),
                    retention_starts_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    retention_until_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    status_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_sessions", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_sessions_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_sessions_duration", "planned_duration_minutes >= 5 AND planned_duration_minutes <= 480");
                    table.CheckConstraint("CK_sales_meeting_sessions_retention", "retention_days >= 1 AND retention_days <= 3650");
                    table.CheckConstraint("CK_sales_meeting_sessions_slide", "current_slide_index >= 0 AND current_talking_point_index >= 0");
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_contacts_company_id_contact_id",
                        columns: x => new { x.company_id, x.contact_id },
                        principalTable: "contacts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_customer_companies_company_id_customer_company_id",
                        columns: x => new { x.company_id, x.customer_company_id },
                        principalTable: "customer_companies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_deals_company_id_deal_id",
                        columns: x => new { x.company_id, x.deal_id },
                        principalTable: "deals",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_leads_company_id_lead_id",
                        columns: x => new { x.company_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_sessions_sales_meeting_invitations_company_id_invitation_id",
                        columns: x => new { x.company_id, x.invitation_id },
                        principalTable: "sales_meeting_invitations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_contact_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_customer_company_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "customer_company_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_deal_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_invitation_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "invitation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_lead_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "lead_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_provider_meeting_id",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "provider_meeting_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_retention_until_at",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "retention_until_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_sessions_company_id_status_updated_at",
                table: "sales_meeting_sessions",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_sessions");
        }
    }
}
