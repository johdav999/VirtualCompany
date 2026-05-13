using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505020000_AddSalesOutboundCampaignPersistenceFoundation")]
public partial class AddSalesOutboundCampaignPersistenceFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sales_sequences",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "draft"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_sequences", x => x.id);
                table.UniqueConstraint("AK_sales_sequences_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_sales_sequences_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_campaigns",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_sequence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                audience_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "draft"),
                launch_requested_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                paused_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                stopped_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_campaigns", x => x.id);
                table.UniqueConstraint("AK_sales_campaigns_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_sales_campaigns_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_sales_campaigns_sales_sequences_company_id_sales_sequence_id",
                    columns: x => new { x.company_id, x.sales_sequence_id },
                    principalTable: "sales_sequences",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "sales_sequence_steps",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_sequence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                step_order = table.Column<int>(type: "int", nullable: false),
                delay_days = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "email"),
                template_subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                template_content = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                ai_personalization_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_sequence_steps", x => x.id);
                table.UniqueConstraint("AK_sales_sequence_steps_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_sales_sequence_steps_delay_days_non_negative", "delay_days >= 0");
                table.CheckConstraint("CK_sales_sequence_steps_step_order_positive", "step_order > 0");
                table.ForeignKey(
                    name: "FK_sales_sequence_steps_sales_sequences_company_id_sales_sequence_id",
                    columns: x => new { x.company_id, x.sales_sequence_id },
                    principalTable: "sales_sequences",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sales_campaign_contacts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                sales_campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                current_step_order = table.Column<int>(type: "int", nullable: true),
                enrolled_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                last_scheduled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                last_sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_campaign_contacts", x => x.id);
                table.UniqueConstraint("AK_sales_campaign_contacts_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_sales_campaign_contacts_current_step_order_positive", "current_step_order IS NULL OR current_step_order > 0");
                table.ForeignKey(
                    name: "FK_sales_campaign_contacts_contacts_company_id_contact_id",
                    columns: x => new { x.company_id, x.contact_id },
                    principalTable: "contacts",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_campaign_contacts_sales_campaigns_company_id_sales_campaign_id",
                    columns: x => new { x.company_id, x.sales_campaign_id },
                    principalTable: "sales_campaigns",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequences_company_id",
            table: "sales_sequences",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequences_company_id_status_name",
            table: "sales_sequences",
            columns: new[] { "company_id", "status", "name" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequences_company_id_updated_at",
            table: "sales_sequences",
            columns: new[] { "company_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaigns_company_id",
            table: "sales_campaigns",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaigns_company_id_sales_sequence_id",
            table: "sales_campaigns",
            columns: new[] { "company_id", "sales_sequence_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaigns_company_id_status_created_at",
            table: "sales_campaigns",
            columns: new[] { "company_id", "status", "created_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaigns_company_id_status_updated_at",
            table: "sales_campaigns",
            columns: new[] { "company_id", "status", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_steps_company_id",
            table: "sales_sequence_steps",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_sequence_steps_company_id_sales_sequence_id_step_order",
            table: "sales_sequence_steps",
            columns: new[] { "company_id", "sales_sequence_id", "step_order" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaign_contacts_company_id",
            table: "sales_campaign_contacts",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaign_contacts_company_id_contact_id_status",
            table: "sales_campaign_contacts",
            columns: new[] { "company_id", "contact_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaign_contacts_company_id_sales_campaign_id_contact_id",
            table: "sales_campaign_contacts",
            columns: new[] { "company_id", "sales_campaign_id", "contact_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaign_contacts_company_id_sales_campaign_id_status",
            table: "sales_campaign_contacts",
            columns: new[] { "company_id", "sales_campaign_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_campaign_contacts_company_id_status_last_scheduled_at",
            table: "sales_campaign_contacts",
            columns: new[] { "company_id", "status", "last_scheduled_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("sales_campaign_contacts");
        migrationBuilder.DropTable("sales_sequence_steps");
        migrationBuilder.DropTable("sales_campaigns");
        migrationBuilder.DropTable("sales_sequences");
    }
}
