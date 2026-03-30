using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

public partial class AddCompanyInvitationsAndOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "company_invitations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                AcceptedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastSentUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AcceptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_invitations", x => x.Id);
                table.ForeignKey(
                    name: "FK_company_invitations_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_company_invitations_users_AcceptedByUserId",
                    column: x => x.AcceptedByUserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_company_invitations_users_InvitedByUserId",
                    column: x => x.InvitedByUserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "company_outbox_messages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                Topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_company_outbox_messages", x => x.Id);
                table.ForeignKey(
                    name: "FK_company_outbox_messages_companies_CompanyId",
                    column: x => x.CompanyId,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_AcceptedByUserId",
            table: "company_invitations",
            column: "AcceptedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_CompanyId_Email",
            table: "company_invitations",
            columns: new[] { "CompanyId", "Email" },
            unique: true,
            filter: "\"Status\" = 'pending'");

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_CompanyId_Status_ExpiresAtUtc",
            table: "company_invitations",
            columns: new[] { "CompanyId", "Status", "ExpiresAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_InvitedByUserId",
            table: "company_invitations",
            column: "InvitedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_company_invitations_TokenHash",
            table: "company_invitations",
            column: "TokenHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_CompanyId_CreatedUtc",
            table: "company_outbox_messages",
            columns: new[] { "CompanyId", "CreatedUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_company_outbox_messages_CompanyId_ProcessedUtc",
            table: "company_outbox_messages",
            columns: new[] { "CompanyId", "ProcessedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "company_invitations");
        migrationBuilder.DropTable(name: "company_outbox_messages");
    }
}
