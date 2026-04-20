using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinanceSeedAnomalyRegistry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_seed_anomalies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    anomaly_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    scenario_profile = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    affected_record_ids_json = table.Column<string>(type: "text", nullable: false),
                    expected_detection_metadata_json = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_seed_anomalies", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_seed_anomalies_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_seed_anomalies_company_id_anomaly_type",
                table: "finance_seed_anomalies",
                columns: new[] { "company_id", "anomaly_type" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_seed_anomalies");
        }
    }
}
