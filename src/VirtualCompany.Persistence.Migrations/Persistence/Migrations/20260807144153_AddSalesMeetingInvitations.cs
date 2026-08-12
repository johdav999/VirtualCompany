using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesMeetingInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales_meeting_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    mailbox_connection_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    calendar_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    organizer_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    attendee_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    attendee_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    starts_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ends_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    time_zone_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    create_online_meeting = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    approval_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    idempotency_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    external_event_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    external_ical_uid = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    provider_web_url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    online_meeting_url = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    execution_attempt_count = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    last_error_code = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    last_error_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_meeting_invitations", x => x.id);
                    table.UniqueConstraint("AK_sales_meeting_invitations_company_id_id", x => new { x.company_id, x.id });
                    table.CheckConstraint("CK_sales_meeting_invitations_time_range", "ends_at > starts_at");
                    table.ForeignKey(
                        name: "FK_sales_meeting_invitations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sales_meeting_invitations_contacts_company_id_contact_id",
                        columns: x => new { x.company_id, x.contact_id },
                        principalTable: "contacts",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_invitations_deals_company_id_deal_id",
                        columns: x => new { x.company_id, x.deal_id },
                        principalTable: "deals",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_invitations_leads_company_id_lead_id",
                        columns: x => new { x.company_id, x.lead_id },
                        principalTable: "leads",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sales_meeting_invitations_mailbox_connections_company_id_mailbox_connection_id",
                        columns: x => new { x.company_id, x.mailbox_connection_id },
                        principalTable: "mailbox_connections",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_approval_request_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "approval_request_id" },
                filter: "[approval_request_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_contact_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_deal_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "deal_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_idempotency_key",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_lead_id_created_at",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "lead_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_mailbox_connection_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "mailbox_connection_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_provider_external_event_id",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "provider", "external_event_id" },
                filter: "[external_event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_meeting_invitations_company_id_status_updated_at",
                table: "sales_meeting_invitations",
                columns: new[] { "company_id", "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sales_meeting_invitations");
        }
    }
}
