using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddPendingInvitedMemberships : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_company_memberships_users_UserId",
            table: "company_memberships");

        migrationBuilder.DropIndex(
            name: "IX_company_memberships_CompanyId_UserId",
            table: "company_memberships");

        migrationBuilder.DropIndex(
            name: "IX_company_memberships_UserId_Status",
            table: "company_memberships");

        migrationBuilder.AddColumn<string>(
            name: "InvitedEmail",
            table: "company_memberships",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "UserId",
            table: "company_memberships",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        migrationBuilder.CreateIndex(
            name: "IX_company_memberships_CompanyId_InvitedEmail",
            table: "company_memberships",
            columns: new[] { "CompanyId", "InvitedEmail" },
            unique: true,
            filter: "\"InvitedEmail\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_company_memberships_CompanyId_UserId",
            table: "company_memberships",
            columns: new[] { "CompanyId", "UserId" },
            unique: true,
            filter: "\"UserId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_company_memberships_UserId_Status",
            table: "company_memberships",
            columns: new[] { "UserId", "Status" },
            filter: "\"UserId\" IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_company_memberships_users_UserId",
            table: "company_memberships",
            column: "UserId",
            principalTable: "users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_company_memberships_users_UserId",
            table: "company_memberships");

        migrationBuilder.DropIndex(
            name: "IX_company_memberships_CompanyId_InvitedEmail",
            table: "company_memberships");

        migrationBuilder.DropIndex(
            name: "IX_company_memberships_CompanyId_UserId",
            table: "company_memberships");

        migrationBuilder.DropIndex(
            name: "IX_company_memberships_UserId_Status",
            table: "company_memberships");

        migrationBuilder.DropColumn(
            name: "InvitedEmail",
            table: "company_memberships");

        migrationBuilder.AlterColumn<Guid>(
            name: "UserId",
            table: "company_memberships",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(name: "IX_company_memberships_CompanyId_UserId", table: "company_memberships", columns: new[] { "CompanyId", "UserId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_company_memberships_UserId_Status", table: "company_memberships", columns: new[] { "UserId", "Status" });
        migrationBuilder.AddForeignKey(name: "FK_company_memberships_users_UserId", table: "company_memberships", column: "UserId", principalTable: "users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
    }
}
