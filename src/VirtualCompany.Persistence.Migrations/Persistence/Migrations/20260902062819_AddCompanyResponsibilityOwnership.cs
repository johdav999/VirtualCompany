using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyResponsibilityOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "size_band",
                table: "companies",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unspecified");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_company_memberships_company_id_id",
                table: "company_memberships",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateTable(
                name: "company_responsibility_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    responsibility_area = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    assignment_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    assigned_membership_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    primary_agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    authority_level = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    approval_policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    escalation_membership_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_responsibility_assignments", x => x.Id);
                    table.UniqueConstraint("AK_company_responsibility_assignments_CompanyId_Id", x => new { x.CompanyId, x.Id });
                    table.ForeignKey(
                        name: "FK_company_responsibility_assignments_agents_CompanyId_primary_agent_id",
                        columns: x => new { x.CompanyId, x.primary_agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_company_responsibility_assignments_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_responsibility_assignments_company_memberships_CompanyId_assigned_membership_id",
                        columns: x => new { x.CompanyId, x.assigned_membership_id },
                        principalTable: "company_memberships",
                        principalColumns: new[] { "CompanyId", "Id" });
                    table.ForeignKey(
                        name: "FK_company_responsibility_assignments_company_memberships_CompanyId_escalation_membership_id",
                        columns: x => new { x.CompanyId, x.escalation_membership_id },
                        principalTable: "company_memberships",
                        principalColumns: new[] { "CompanyId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_companies_size_band",
                table: "companies",
                column: "size_band");

            migrationBuilder.CreateIndex(
                name: "IX_company_responsibility_assignments_CompanyId_assigned_membership_id",
                table: "company_responsibility_assignments",
                columns: new[] { "CompanyId", "assigned_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_responsibility_assignments_CompanyId_escalation_membership_id",
                table: "company_responsibility_assignments",
                columns: new[] { "CompanyId", "escalation_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_responsibility_assignments_CompanyId_primary_agent_id",
                table: "company_responsibility_assignments",
                columns: new[] { "CompanyId", "primary_agent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_responsibility_assignments_CompanyId_responsibility_area_assignment_kind",
                table: "company_responsibility_assignments",
                columns: new[] { "CompanyId", "responsibility_area", "assignment_kind" });

            migrationBuilder.CreateIndex(
                name: "UX_company_responsibility_primary",
                table: "company_responsibility_assignments",
                columns: new[] { "CompanyId", "responsibility_area" },
                unique: true,
                filter: "[assignment_kind] = N'primary'");

            migrationBuilder.Sql(
                """
                DECLARE @eligible_companies TABLE
                (
                    company_id uniqueidentifier NOT NULL PRIMARY KEY,
                    owner_membership_id uniqueidentifier NOT NULL
                );

                INSERT INTO @eligible_companies (company_id, owner_membership_id)
                SELECT m.CompanyId, MIN(m.Id)
                FROM company_memberships AS m
                WHERE m.Role = N'owner' AND m.Status = N'active'
                  AND NOT EXISTS
                  (
                      SELECT 1 FROM company_responsibility_assignments AS existing
                      WHERE existing.CompanyId = m.CompanyId
                  )
                GROUP BY m.CompanyId
                HAVING COUNT_BIG(*) = 1;

                UPDATE c SET size_band = N'micro'
                FROM companies AS c
                INNER JOIN @eligible_companies AS eligible ON eligible.company_id = c.Id
                WHERE c.size_band = N'unspecified';

                INSERT INTO company_responsibility_assignments
                (
                    Id, CompanyId, responsibility_area, assignment_kind, assigned_membership_id,
                    primary_agent_id, authority_level, approval_policy_id, escalation_membership_id,
                    version, created_utc, updated_utc
                )
                SELECT NEWID(), eligible.company_id, areas.responsibility_area, N'primary',
                       eligible.owner_membership_id,
                       CASE WHEN agent_matches.match_count = 1 THEN agent_matches.agent_id END,
                       N'level_1', NULL, NULL, 0, SYSUTCDATETIME(), SYSUTCDATETIME()
                FROM @eligible_companies AS eligible
                CROSS JOIN
                (
                    VALUES
                        (N'company_performance'),
                        (N'cash_and_accounting'),
                        (N'sales'),
                        (N'marketing'),
                        (N'customer_support'),
                        (N'compliance')
                ) AS areas(responsibility_area)
                OUTER APPLY
                (
                    SELECT COUNT_BIG(*) AS match_count, MAX(a.Id) AS agent_id
                    FROM agents AS a
                    WHERE a.CompanyId = eligible.company_id AND a.Status = N'active'
                      AND
                      (
                          (areas.responsibility_area = N'cash_and_accounting' AND LOWER(a.Department) IN (N'finance', N'accounting')) OR
                          (areas.responsibility_area = N'compliance' AND LOWER(a.Department) IN (N'finance', N'accounting', N'compliance', N'legal')) OR
                          (areas.responsibility_area = N'sales' AND LOWER(a.Department) = N'sales') OR
                          (areas.responsibility_area = N'marketing' AND LOWER(a.Department) = N'marketing') OR
                          (areas.responsibility_area = N'customer_support' AND LOWER(a.Department) IN (N'support', N'customer support', N'customer success')) OR
                          (areas.responsibility_area = N'company_performance' AND LOWER(a.Department) IN (N'operations', N'executive', N'leadership'))
                      )
                ) AS agent_matches;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_responsibility_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_company_memberships_company_id_id",
                table: "company_memberships");

            migrationBuilder.DropIndex(
                name: "IX_companies_size_band",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "size_band",
                table: "companies");
        }
    }
}
