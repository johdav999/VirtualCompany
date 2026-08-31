using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceAgentDelegationAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "finance_agent_delegation_authorities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    delegated_actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    issued_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    originating_workflow_instance_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    capability = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    allowed_action_classes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    allowed_scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    issued_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    revoked_utc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    revocation_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_agent_delegation_authorities", x => x.id);
                    table.ForeignKey(
                        name: "FK_finance_agent_delegation_authorities_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_delegation_authorities_company_id_agent_id_expires_utc",
                table: "finance_agent_delegation_authorities",
                columns: new[] { "company_id", "agent_id", "expires_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_agent_delegation_authorities_company_id_originating_workflow_instance_id",
                table: "finance_agent_delegation_authorities",
                columns: new[] { "company_id", "originating_workflow_instance_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "finance_agent_delegation_authorities");
        }
    }
}
