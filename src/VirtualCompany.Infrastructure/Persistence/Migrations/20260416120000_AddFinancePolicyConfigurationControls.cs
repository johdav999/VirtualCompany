using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddFinancePolicyConfigurationControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "anomaly_detection_lower_bound",
                table: "finance_policy_configurations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: -10000m);

            migrationBuilder.AddColumn<decimal>(
                name: "anomaly_detection_upper_bound",
                table: "finance_policy_configurations",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 10000m);

            migrationBuilder.AddColumn<int>(
                name: "cash_runway_warning_threshold_days",
                table: "finance_policy_configurations",
                type: "int",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<int>(
                name: "cash_runway_critical_threshold_days",
                table: "finance_policy_configurations",
                type: "int",
                nullable: false,
                defaultValue: 30);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anomaly_detection_lower_bound",
                table: "finance_policy_configurations");

            migrationBuilder.DropColumn(
                name: "anomaly_detection_upper_bound",
                table: "finance_policy_configurations");

            migrationBuilder.DropColumn(
                name: "cash_runway_warning_threshold_days",
                table: "finance_policy_configurations");

            migrationBuilder.DropColumn(
                name: "cash_runway_critical_threshold_days",
                table: "finance_policy_configurations");
        }
    }
}