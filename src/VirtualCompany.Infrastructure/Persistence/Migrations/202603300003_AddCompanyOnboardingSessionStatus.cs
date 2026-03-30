using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanyOnboardingSessionStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "OnboardingAbandonedUtc",
            table: "companies",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OnboardingStatus",
            table: "companies",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "not_started");

        migrationBuilder.Sql("""
            UPDATE companies
            SET OnboardingStatus =
                CASE
                    WHEN OnboardingCompletedUtc IS NOT NULL THEN 'completed'
                    WHEN OnboardingCurrentStep IS NOT NULL OR OnboardingLastSavedUtc IS NOT NULL THEN 'in_progress'
                    ELSE 'not_started'
                END
            """);

        migrationBuilder.CreateIndex(
            name: "IX_companies_OnboardingStatus",
            table: "companies",
            column: "OnboardingStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_companies_OnboardingStatus",
            table: "companies");

        migrationBuilder.DropColumn(name: "OnboardingAbandonedUtc", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingStatus", table: "companies");
    }
}