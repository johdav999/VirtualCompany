using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260710090000_AddSupportRefundExecutionLifecycle")]
public partial class AddSupportRefundExecutionLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("provider_write_request_id", "support_refund_requests", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("provider_approval_request_id", "support_refund_requests", "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("last_failure_summary", "support_refund_requests", "nvarchar(1000)", maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<DateTime>("execution_requested_at", "support_refund_requests", "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("completed_at", "support_refund_requests", "datetime2", nullable: true);
        migrationBuilder.CreateIndex(
            "IX_support_refund_requests_company_id_provider_write_request_id",
            "support_refund_requests",
            new[] { "company_id", "provider_write_request_id" },
            unique: true,
            filter: "provider_write_request_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_support_refund_requests_company_id_provider_write_request_id", "support_refund_requests");
        migrationBuilder.DropColumn("provider_write_request_id", "support_refund_requests");
        migrationBuilder.DropColumn("provider_approval_request_id", "support_refund_requests");
        migrationBuilder.DropColumn("last_failure_summary", "support_refund_requests");
        migrationBuilder.DropColumn("execution_requested_at", "support_refund_requests");
        migrationBuilder.DropColumn("completed_at", "support_refund_requests");
    }
}
