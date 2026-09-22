using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMicrosoft365DocumentOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "credential_mode",
                table: "company_document_repository_connections",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "customer_managed");

            migrationBuilder.CreateTable(
                name: "company_document_repository_onboarding_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    initiating_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_handle_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    state_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    protected_setup_material = table.Column<string>(type: "nvarchar(max)", maxLength: 12000, nullable: true),
                    return_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    correlation_id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    provider_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    failure_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    callback_received_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    expired_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    concurrency_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_document_repository_onboarding_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_company_document_repository_onboarding_sessions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_onboarding_sessions_company_id_initiating_user_id_status",
                table: "company_document_repository_onboarding_sessions",
                columns: new[] { "company_id", "initiating_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_onboarding_sessions_session_handle_hash",
                table: "company_document_repository_onboarding_sessions",
                column: "session_handle_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_onboarding_sessions_state_hash",
                table: "company_document_repository_onboarding_sessions",
                column: "state_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_document_repository_onboarding_sessions_status_expires_at",
                table: "company_document_repository_onboarding_sessions",
                columns: new[] { "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_document_repository_onboarding_sessions");

            migrationBuilder.DropColumn(
                name: "credential_mode",
                table: "company_document_repository_connections");
        }
    }
}
