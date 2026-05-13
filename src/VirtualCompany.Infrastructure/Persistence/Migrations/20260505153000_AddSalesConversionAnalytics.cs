using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505153000_AddSalesConversionAnalytics")]
public partial class AddSalesConversionAnalytics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sales_message_performances",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                message_key = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                sequence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                sequence_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                sequence_execution_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                provider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                provider_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                provider_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                internet_message_id = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                variant_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                step_order = table.Column<int>(type: "int", nullable: true),
                sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                delivered_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                bounced_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                opened_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                replied_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                deal_created_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                converted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                expected_revenue_amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                expected_revenue_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                expected_close_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                pipeline_risk_score = table.Column<decimal>(type: "decimal(6,4)", nullable: true),
                last_risk_calculated_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sales_message_performances", x => x.id);
                table.UniqueConstraint("AK_sales_message_performances_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_sales_message_performances_pipeline_risk_score_range", "pipeline_risk_score IS NULL OR (pipeline_risk_score >= 0 AND pipeline_risk_score <= 1)");
                table.CheckConstraint("CK_sales_message_performances_step_order_positive", "step_order IS NULL OR step_order > 0");
                table.ForeignKey(
                    name: "FK_sales_message_performances_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_sales_message_performances_contacts_company_id_contact_id",
                    columns: x => new { x.company_id, x.contact_id },
                    principalTable: "contacts",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_message_performances_deals_company_id_deal_id",
                    columns: x => new { x.company_id, x.deal_id },
                    principalTable: "deals",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_message_performances_sales_campaigns_company_id_campaign_id",
                    columns: x => new { x.company_id, x.campaign_id },
                    principalTable: "sales_campaigns",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_message_performances_sales_sequence_execution_steps_company_id_sequence_execution_step_id",
                    columns: x => new { x.company_id, x.sequence_execution_step_id },
                    principalTable: "sales_sequence_execution_steps",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_message_performances_sales_sequence_steps_company_id_sequence_step_id",
                    columns: x => new { x.company_id, x.sequence_step_id },
                    principalTable: "sales_sequence_steps",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_sales_message_performances_sales_sequences_company_id_sequence_id",
                    columns: x => new { x.company_id, x.sequence_id },
                    principalTable: "sales_sequences",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_campaign_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "campaign_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_campaign_id_sequence_id_sequence_step_id_variant_key",
            table: "sales_message_performances",
            columns: new[] { "company_id", "campaign_id", "sequence_id", "sequence_step_id", "variant_key" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_contact_id_updated_at",
            table: "sales_message_performances",
            columns: new[] { "company_id", "contact_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_deal_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "deal_id" },
            filter: "[deal_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_expected_close_at",
            table: "sales_message_performances",
            columns: new[] { "company_id", "expected_close_at" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_internet_message_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "internet_message_id" },
            filter: "[internet_message_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_message_key",
            table: "sales_message_performances",
            columns: new[] { "company_id", "message_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_pipeline_risk_score",
            table: "sales_message_performances",
            columns: new[] { "company_id", "pipeline_risk_score" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_provider_message_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "provider_message_id" },
            filter: "[provider_message_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_provider_thread_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "provider_thread_id" },
            filter: "[provider_thread_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_sequence_execution_step_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "sequence_execution_step_id" },
            filter: "[sequence_execution_step_id] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_sequence_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "sequence_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sales_message_performances_company_id_sequence_step_id",
            table: "sales_message_performances",
            columns: new[] { "company_id", "sequence_step_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sales_message_performances");
    }
}
