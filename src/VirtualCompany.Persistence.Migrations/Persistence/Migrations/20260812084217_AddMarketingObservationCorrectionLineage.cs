using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingObservationCorrectionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "correction_of_observation_id",
                table: "marketing_channel_observations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_superseded",
                table: "marketing_channel_observations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_marketing_channel_observations_company_id_correction_of_observation_id",
                table: "marketing_channel_observations",
                columns: new[] { "company_id", "correction_of_observation_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_marketing_channel_observations_company_id_correction_of_observation_id",
                table: "marketing_channel_observations");

            migrationBuilder.DropColumn(
                name: "correction_of_observation_id",
                table: "marketing_channel_observations");

            migrationBuilder.DropColumn(
                name: "is_superseded",
                table: "marketing_channel_observations");
        }
    }
}
