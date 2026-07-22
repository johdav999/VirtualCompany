using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableMailboxOAuthState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mailbox_oauth_authorization_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    nonce_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mailbox_oauth_authorization_states", x => x.id);
                    table.CheckConstraint("CK_mailbox_oauth_authorization_states_expiry", "expires_at > created_at");
                    table.CheckConstraint("CK_mailbox_oauth_authorization_states_provider", "provider IN ('gmail', 'microsoft365', 'standard_email')");
                    table.CheckConstraint("CK_mailbox_oauth_authorization_states_purpose", "purpose IN ('finance', 'sales', 'support')");
                    table.ForeignKey(
                        name: "FK_mailbox_oauth_authorization_states_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mailbox_oauth_authorization_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_oauth_authorization_states_company_id_consumed_at",
                table: "mailbox_oauth_authorization_states",
                columns: new[] { "company_id", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_oauth_authorization_states_company_id_user_id_purpose_provider_expires_at",
                table: "mailbox_oauth_authorization_states",
                columns: new[] { "company_id", "user_id", "purpose", "provider", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_oauth_authorization_states_nonce_hash",
                table: "mailbox_oauth_authorization_states",
                column: "nonce_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mailbox_oauth_authorization_states_user_id",
                table: "mailbox_oauth_authorization_states",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mailbox_oauth_authorization_states");
        }
    }
}
