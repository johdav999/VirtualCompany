using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(VirtualCompanyDbContext))]
    [Migration("20260504223000_AddWebsiteLeadFormKey")]
    public partial class AddWebsiteLeadFormKey : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "website_lead_form_key",
                table: "sales_automation_policies",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql("UPDATE sales_automation_policies SET website_lead_form_key = CONCAT('wlf_', REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')) WHERE website_lead_form_key IS NULL OR website_lead_form_key = ''");

            migrationBuilder.AlterColumn<string>(
                name: "website_lead_form_key",
                table: "sales_automation_policies",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.CreateIndex("IX_sales_automation_policies_website_lead_form_key", "sales_automation_policies", "website_lead_form_key", unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_sales_automation_policies_website_lead_form_key", "sales_automation_policies");
            migrationBuilder.DropColumn("website_lead_form_key", "sales_automation_policies");
        }
    }
}
