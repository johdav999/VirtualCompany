using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingEventTaskLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "related_task_id",
                table: "marketing_event_triggers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_event_triggers_company_id_related_task_id",
                table: "marketing_event_triggers",
                columns: new[] { "company_id", "related_task_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_event_triggers_company_id_related_task_id",
                table: "marketing_event_triggers");

            migrationBuilder.DropColumn(
                name: "related_task_id",
                table: "marketing_event_triggers");
        }
    }
}
