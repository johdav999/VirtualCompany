using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanyBrandingAndSettingsJsonb : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "branding_json",
            table: "companies",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'{}'::jsonb");

        migrationBuilder.AddColumn<string>(
            name: "settings_json",
            table: "companies",
            type: "jsonb",
            nullable: false,
            defaultValueSql: "'{}'::jsonb");

        migrationBuilder.Sql(
            """
            UPDATE companies
            SET settings_json =
                CASE
                    WHEN "OnboardingStateJson" IS NULL OR btrim("OnboardingStateJson") = '' THEN settings_json
                    ELSE jsonb_strip_nulls(
                        jsonb_build_object(
                            'templateId', "OnboardingTemplateId",
                            'onboarding',
                            (
                                ("OnboardingStateJson")::jsonb ||
                                jsonb_build_object('selectedTemplateId', COALESCE(("OnboardingStateJson")::jsonb ->> 'selectedTemplateId', "OnboardingTemplateId"))
                            )
                        )
                    )
                END
            WHERE settings_json = '{}'::jsonb;
            """);

        migrationBuilder.Sql(
            """
            UPDATE companies
            SET settings_json = jsonb_set(
                settings_json,
                '{onboarding,isCompleted}',
                to_jsonb(COALESCE("OnboardingCompletedUtc" IS NOT NULL, false)),
                true)
            WHERE "OnboardingCompletedUtc" IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "branding_json",
            table: "companies");

        migrationBuilder.DropColumn(
            name: "settings_json",
            table: "companies");
    }
}
