using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierApprovalAutomationRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_approval_automation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    supplier_key = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    supplier_name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    supplier_org_number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    stage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    granted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    granted_by_display_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_approval_automation_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_approval_automation_rules_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_supplier_approval_automation_rules_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_approval_automation_rules_company_id_agent_id_is_active",
                table: "supplier_approval_automation_rules",
                columns: new[] { "company_id", "agent_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_approval_automation_rules_company_id_supplier_key_stage",
                table: "supplier_approval_automation_rules",
                columns: new[] { "company_id", "supplier_key", "stage" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_approval_automation_rules");
        }
    }
}
