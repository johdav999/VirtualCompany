using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class EnforceCanonicalMemoryTypes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE memory_items
                    SET "MemoryType" = CASE REPLACE(REPLACE(LOWER(TRIM("MemoryType")), '-', ''), '_', '')
                        WHEN 'preference' THEN 'preference'
                        WHEN 'decisionpattern' THEN 'decision_pattern'
                        WHEN 'summary' THEN 'summary'
                        WHEN 'rolememory' THEN 'role_memory'
                        WHEN 'companymemory' THEN 'company_memory'
                        ELSE LOWER(TRIM("MemoryType"))
                    END
                    WHERE "MemoryType" IS NOT NULL;

                    ALTER TABLE memory_items
                    DROP CONSTRAINT IF EXISTS "CK_memory_items_memory_type";

                    ALTER TABLE memory_items
                    ADD CONSTRAINT "CK_memory_items_memory_type"
                    CHECK ("MemoryType" IN ('company_memory', 'decision_pattern', 'preference', 'role_memory', 'summary'));
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [memory_items]
                    SET [MemoryType] = CASE REPLACE(REPLACE(LOWER(LTRIM(RTRIM([MemoryType]))), '-', ''), '_', '')
                        WHEN 'preference' THEN 'preference'
                        WHEN 'decisionpattern' THEN 'decision_pattern'
                        WHEN 'summary' THEN 'summary'
                        WHEN 'rolememory' THEN 'role_memory'
                        WHEN 'companymemory' THEN 'company_memory'
                        ELSE LOWER(LTRIM(RTRIM([MemoryType])))
                    END
                    WHERE [MemoryType] IS NOT NULL;

                    IF EXISTS (
                        SELECT 1
                        FROM sys.check_constraints
                        WHERE [name] = N'CK_memory_items_memory_type')
                    BEGIN
                        ALTER TABLE [memory_items]
                        DROP CONSTRAINT [CK_memory_items_memory_type];
                    END

                    ALTER TABLE [memory_items]
                    ADD CONSTRAINT [CK_memory_items_memory_type]
                    CHECK ([MemoryType] IN (N'company_memory', N'decision_pattern', N'preference', N'role_memory', N'summary'));
                    """);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE memory_items
                    DROP CONSTRAINT IF EXISTS "CK_memory_items_memory_type";
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE [memory_items]
                    DROP CONSTRAINT [CK_memory_items_memory_type];
                    """);
            }
        }
    }
}
