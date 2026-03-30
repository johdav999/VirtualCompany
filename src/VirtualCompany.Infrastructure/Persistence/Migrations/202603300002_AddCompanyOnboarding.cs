using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanyOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BusinessType",
            table: "companies",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ComplianceRegion",
            table: "companies",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Currency",
            table: "companies",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Industry",
            table: "companies",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Language",
            table: "companies",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "OnboardingCurrentStep",
            table: "companies",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "OnboardingCompletedUtc",
            table: "companies",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "OnboardingLastSavedUtc",
            table: "companies",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OnboardingStateJson",
            table: "companies",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "OnboardingTemplateId",
            table: "companies",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Timezone",
            table: "companies",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_companies_OnboardingCompletedUtc",
            table: "companies",
            column: "OnboardingCompletedUtc");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_companies_OnboardingCompletedUtc", table: "companies");
        migrationBuilder.DropColumn(name: "BusinessType", table: "companies");
        migrationBuilder.DropColumn(name: "ComplianceRegion", table: "companies");
        migrationBuilder.DropColumn(name: "Currency", table: "companies");
        migrationBuilder.DropColumn(name: "Industry", table: "companies");
        migrationBuilder.DropColumn(name: "Language", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingCurrentStep", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingCompletedUtc", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingLastSavedUtc", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingStateJson", table: "companies");
        migrationBuilder.DropColumn(name: "OnboardingTemplateId", table: "companies");
        migrationBuilder.DropColumn(name: "Timezone", table: "companies");
    }
}