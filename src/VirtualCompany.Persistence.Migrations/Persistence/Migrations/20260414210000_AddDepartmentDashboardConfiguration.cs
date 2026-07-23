using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414210000_AddDepartmentDashboardConfiguration")]
public partial class AddDepartmentDashboardConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "dashboard_department_configs",
            columns: table => new
            {
                id = table.Column<Guid>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier", nullable: false),
                department = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                display_name = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)", maxLength: 128, nullable: false),
                display_order = table.Column<int>(type: "int", nullable: false),
                is_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                icon = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: true),
                navigation_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                visibility_roles_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                empty_state_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                created_at = table.Column<DateTime>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dashboard_department_configs", x => x.id);
                table.ForeignKey(
                    name: "FK_dashboard_department_configs_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "dashboard_widget_configs",
            columns: table => new
            {
                id = table.Column<Guid>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier", nullable: false),
                department_config_id = table.Column<Guid>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier", nullable: false),
                widget_key = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)", maxLength: 128, nullable: false),
                title = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(160)" : "nvarchar(160)", maxLength: 160, nullable: false),
                widget_type = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)", maxLength: 64, nullable: false),
                display_order = table.Column<int>(type: "int", nullable: false),
                is_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                summary_binding = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(128)" : "nvarchar(128)", maxLength: 128, nullable: false),
                navigation_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                visibility_roles_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                empty_state_json = table.Column<string>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "jsonb" : "nvarchar(max)", nullable: false, defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'"),
                created_at = table.Column<DateTime>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dashboard_widget_configs", x => x.id);
                table.ForeignKey(
                    name: "FK_dashboard_widget_configs_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
                table.ForeignKey(
                    name: "FK_dashboard_widget_configs_dashboard_department_configs_department_config_id",
                    column: x => x.department_config_id,
                    principalTable: "dashboard_department_configs",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_department_configs_company_id",
            table: "dashboard_department_configs",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_department_configs_company_id_department",
            table: "dashboard_department_configs",
            columns: new[] { "company_id", "department" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_department_configs_company_id_display_order_department",
            table: "dashboard_department_configs",
            columns: new[] { "company_id", "display_order", "department" });

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_widget_configs_company_id",
            table: "dashboard_widget_configs",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_widget_configs_department_config_id",
            table: "dashboard_widget_configs",
            column: "department_config_id");

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_widget_configs_company_id_department_config_id_display_order_widget_key",
            table: "dashboard_widget_configs",
            columns: new[] { "company_id", "department_config_id", "display_order", "widget_key" });

        migrationBuilder.CreateIndex(
            name: "IX_dashboard_widget_configs_company_id_department_config_id_widget_key",
            table: "dashboard_widget_configs",
            columns: new[] { "company_id", "department_config_id", "widget_key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "dashboard_widget_configs");
        migrationBuilder.DropTable(name: "dashboard_department_configs");
    }
}