using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260414133000_AddEscalationLifecycleVersions")]
    public partial class AddEscalationLifecycleVersions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source_lifecycle_version",
                table: "tasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source_lifecycle_version",
                table: "alerts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.DropIndex(
                name: "IX_escalations_company_id_policy_id_source_entity_id_escalation_level_lifecycle_version",
                table: "escalations");

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_policy_id_source_entity_type_source_entity_id_escalation_level_lifecycle_version",
                table: "escalations",
                columns: new[] { "company_id", "policy_id", "source_entity_type", "source_entity_id", "escalation_level", "lifecycle_version" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_escalations_company_id_policy_id_source_entity_type_source_entity_id_escalation_level_lifecycle_version",
                table: "escalations");

            migrationBuilder.CreateIndex(
                name: "IX_escalations_company_id_policy_id_source_entity_id_escalation_level_lifecycle_version",
                table: "escalations",
                columns: new[] { "company_id", "policy_id", "source_entity_id", "escalation_level", "lifecycle_version" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "source_lifecycle_version",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "source_lifecycle_version",
                table: "alerts");
        }
    }
}
