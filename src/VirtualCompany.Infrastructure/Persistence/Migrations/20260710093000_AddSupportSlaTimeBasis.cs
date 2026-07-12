using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260710093000_AddSupportSlaTimeBasis")]
public partial class AddSupportSlaTimeBasis : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("time_basis", "support_sla_policies", "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "elapsed");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("time_basis", "support_sla_policies");
}
