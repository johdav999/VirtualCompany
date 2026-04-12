using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddMemoryLifecycleControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedUtc",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "timestamp with time zone" : "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByActorType",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByActorId",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(512)" : "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpiredByActorType",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(64)" : "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpiredByActorId",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "uuid" : "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpirationReason",
                table: "memory_items",
                type: ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "character varying(512)" : "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_memory_items_CompanyId_DeletedUtc_ValidToUtc",
                table: "memory_items",
                columns: new[] { "CompanyId", "DeletedUtc", "ValidToUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_memory_items_CompanyId_DeletedUtc_ValidToUtc",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "DeletedUtc",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "DeletedByActorType",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "DeletedByActorId",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "ExpiredByActorType",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "ExpiredByActorId",
                table: "memory_items");

            migrationBuilder.DropColumn(
                name: "ExpirationReason",
                table: "memory_items");
        }
    }
}
