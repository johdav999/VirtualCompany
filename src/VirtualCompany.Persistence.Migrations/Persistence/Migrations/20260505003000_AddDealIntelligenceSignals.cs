using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505003000_AddDealIntelligenceSignals")]
public partial class AddDealIntelligenceSignals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "deal_intelligence_signals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                sequence_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                sequence_step_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                signal_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                signal_state = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                confidence_score = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                source_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                source_message_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                source_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                source_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                detected_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                source_window_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                source_window_ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_deal_intelligence_signals", x => x.id);
                table.UniqueConstraint("AK_deal_intelligence_signals_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_deal_intelligence_signals_confidence_score_range", "confidence_score >= 0 AND confidence_score <= 1");
                table.CheckConstraint("CK_deal_intelligence_signals_explanation_required", "LEN(LTRIM(RTRIM(explanation))) > 0");
                table.ForeignKey(
                    name: "FK_deal_intelligence_signals_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_deal_intelligence_signals_deals_company_id_deal_id",
                    columns: x => new { x.company_id, x.deal_id },
                    principalTable: "deals",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_company_id", table: "deal_intelligence_signals", column: "company_id");
        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_deal_id", table: "deal_intelligence_signals", column: "deal_id");
        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_conversation_id", table: "deal_intelligence_signals", column: "conversation_id");
        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_message_id", table: "deal_intelligence_signals", column: "message_id");
        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_signal_type", table: "deal_intelligence_signals", column: "signal_type");
        migrationBuilder.CreateIndex(name: "IX_deal_intelligence_signals_detected_at", table: "deal_intelligence_signals", column: "detected_at");
        migrationBuilder.CreateIndex(
            name: "IX_deal_intelligence_signals_company_id_source_type_source_message_id_signal_type",
            table: "deal_intelligence_signals",
            columns: new[] { "company_id", "source_type", "source_message_id", "signal_type" },
            unique: true,
            filter: "[source_message_id] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_deal_intelligence_signals_company_id_deal_id_source_thread_id_signal_type",
            table: "deal_intelligence_signals",
            columns: new[] { "company_id", "deal_id", "source_thread_id", "signal_type" },
            filter: "[deal_id] IS NOT NULL AND [source_thread_id] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "deal_intelligence_signals");
    }
}
