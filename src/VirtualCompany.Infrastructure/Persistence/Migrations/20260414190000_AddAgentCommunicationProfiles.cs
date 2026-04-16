using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260414190000_AddAgentCommunicationProfiles")]
public partial class AddAgentCommunicationProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var type = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL"
            ? "jsonb"
            : "nvarchar(max)";

        migrationBuilder.AddColumn<string>(
            name: "communication_profile_json",
            table: "agents",
            type: type,
            nullable: false,
            defaultValueSql: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "'{}'::jsonb" : "N'{}'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "communication_profile_json",
            table: "agents");
    }
}
