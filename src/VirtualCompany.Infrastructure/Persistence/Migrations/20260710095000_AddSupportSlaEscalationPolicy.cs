using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260710095000_AddSupportSlaEscalationPolicy")]
public partial class AddSupportSlaEscalationPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("risk_threshold_minutes", "support_sla_policies", "int", nullable: false, defaultValue: 240);
        migrationBuilder.AddColumn<string>("escalation_recipient_role", "support_sla_policies", "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: "support_supervisor");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("risk_threshold_minutes", "support_sla_policies");
        migrationBuilder.DropColumn("escalation_recipient_role", "support_sla_policies");
    }
}
