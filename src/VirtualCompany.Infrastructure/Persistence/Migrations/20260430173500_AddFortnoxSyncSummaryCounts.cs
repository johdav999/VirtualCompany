using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [Migration("20260430173500_AddFortnoxSyncSummaryCounts")]
    [DbContext(typeof(VirtualCompanyDbContext))]
    public partial class AddFortnoxSyncSummaryCounts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            AddColumnIfMissing(migrationBuilder, "created_count");
            AddColumnIfMissing(migrationBuilder, "updated_count");
            AddColumnIfMissing(migrationBuilder, "skipped_count");
            AddColumnIfMissing(migrationBuilder, "error_count");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
            {
                return;
            }

            DropColumnIfExists(migrationBuilder, "created_count");
            DropColumnIfExists(migrationBuilder, "updated_count");
            DropColumnIfExists(migrationBuilder, "skipped_count");
            DropColumnIfExists(migrationBuilder, "error_count");
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($"""
                IF COL_LENGTH(N'dbo.finance_integration_audit_events', N'{columnName}') IS NULL
                BEGIN
                    ALTER TABLE [finance_integration_audit_events] ADD [{columnName}] int NOT NULL DEFAULT 0;
                END
                """);
        }

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string columnName)
        {
            migrationBuilder.Sql($"""
                IF COL_LENGTH(N'dbo.finance_integration_audit_events', N'{columnName}') IS NOT NULL
                BEGIN
                    ALTER TABLE [finance_integration_audit_events] DROP COLUMN [{columnName}];
                END
                """);
        }
    }
}
