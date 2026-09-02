using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations;

public partial class ImplementLeasedFinanceAutonomyExecutorAndRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "business_idempotency_key",
            table: "finance_autonomy_run_steps",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "reconciliation_reference",
            table: "finance_autonomy_run_steps",
            type: "nvarchar(240)",
            maxLength: 240,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE [finance_autonomy_run_steps]
            SET [business_idempotency_key] = CONCAT(
                'finance-autonomy:',
                LOWER(REPLACE(CONVERT(nvarchar(36), [company_id]), '-', '')), ':',
                LOWER(REPLACE(CONVERT(nvarchar(36), [run_id]), '-', '')), ':',
                LOWER(REPLACE(CONVERT(nvarchar(36), [id]), '-', '')))
            WHERE [business_idempotency_key] = '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "business_idempotency_key", table: "finance_autonomy_run_steps");
        migrationBuilder.DropColumn(name: "reconciliation_reference", table: "finance_autonomy_run_steps");
    }
}
