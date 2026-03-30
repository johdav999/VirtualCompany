using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddBusinessAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Outcome = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RationaleSummary = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                DataSources = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_audit_events_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "OccurredUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_CompanyId_TargetType_TargetId_OccurredUtc",
            table: "audit_events",
            columns: new[] { "CompanyId", "TargetType", "TargetId", "OccurredUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_events");
    }
}
