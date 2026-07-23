using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VirtualCompany.Infrastructure.Persistence;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260412223000_AddApprovalRequestDecisionChains")]
public partial class AddApprovalRequestDecisionChains : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            migrationBuilder.AddColumn<string>(
                name: "decision_chain_json",
                table: "approval_requests",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }
        else
        {
            migrationBuilder.AddColumn<string>(
                name: "decision_chain_json",
                table: "approval_requests",
                type: "nvarchar(max)",
                nullable: false,
                defaultValueSql: "N'{}'");
        }

        migrationBuilder.CreateIndex(
            name: "IX_approval_requests_CompanyId_ApprovalTarget_Status_CreatedUtc",
            table: "approval_requests",
            columns: new[] { "CompanyId", "ApprovalTarget", "Status", "CreatedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_approval_requests_CompanyId_ApprovalTarget_Status_CreatedUtc",
            table: "approval_requests");

        migrationBuilder.DropColumn(
            name: "decision_chain_json",
            table: "approval_requests");
    }
}
