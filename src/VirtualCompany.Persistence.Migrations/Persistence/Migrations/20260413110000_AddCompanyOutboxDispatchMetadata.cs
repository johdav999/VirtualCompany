using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260413110000_AddCompanyOutboxDispatchMetadata")]
public partial class AddCompanyOutboxDispatchMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        var isPostgres = ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";
        var stringType = isPostgres ? "character varying" : "nvarchar";
        var dateTimeType = isPostgres ? "timestamp with time zone" : "datetime2";
        var maxStringType = isPostgres ? "text" : "nvarchar(max)";

        migrationBuilder.AddColumn<string>(
            name: "CausationId",
            table: "company_outbox_messages",
            type: $"{stringType}(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "HeadersJson",
            table: "company_outbox_messages",
            type: maxStringType,
            maxLength: 4000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_Status_AvailableUtc",
            table: "company_outbox_messages",
            columns: new[] { "Status", "AvailableUtc" });

        migrationBuilder.Sql(isPostgres
            ? """
              UPDATE company_outbox_messages
              SET "CausationId" = COALESCE("CausationId", "CorrelationId")
              WHERE "CausationId" IS NULL
                AND "CorrelationId" IS NOT NULL
              """
            : """
              UPDATE company_outbox_messages
              SET CausationId = COALESCE(CausationId, CorrelationId)
              WHERE CausationId IS NULL
                AND CorrelationId IS NOT NULL
              """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_company_outbox_messages_Status_AvailableUtc",
            table: "company_outbox_messages");

        migrationBuilder.DropColumn(
            name: "CausationId",
            table: "company_outbox_messages");

        migrationBuilder.DropColumn(
            name: "HeadersJson",
            table: "company_outbox_messages");
    }
}