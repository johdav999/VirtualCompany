using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServerBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BusinessType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Timezone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ComplianceRegion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    branding_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    settings_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    OnboardingStateJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OnboardingCurrentStep = table.Column<int>(type: "int", nullable: true),
                    OnboardingTemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OnboardingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "not_started"),
                    OnboardingLastSavedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OnboardingCompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OnboardingAbandonedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "company_setup_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IndustryTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BusinessTypeTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    defaults_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_setup_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AuthProvider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthSubject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RationaleSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    data_sources_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_events_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_notes_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AvailableUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "company_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSentUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "pending"),
                    LastDeliveryAttemptUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeliveryError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastDeliveryCorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastDeliveredTokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AcceptedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                name: "company_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvitedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    permissions_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_company_memberships_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_company_memberships_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_CompanyId_OccurredUtc",
                table: "audit_events",
                columns: new[] { "CompanyId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_CompanyId_TargetType_TargetId_OccurredUtc",
                table: "audit_events",
                columns: new[] { "CompanyId", "TargetType", "TargetId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_companies_OnboardingCompletedUtc",
                table: "companies",
                column: "OnboardingCompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_companies_OnboardingStatus",
                table: "companies",
                column: "OnboardingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_AcceptedByUserId",
                table: "company_invitations",
                column: "AcceptedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_CompanyId_DeliveryStatus",
                table: "company_invitations",
                columns: new[] { "CompanyId", "DeliveryStatus" });

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

            migrationBuilder.CreateIndex(
                name: "IX_company_notes_CompanyId_Id",
                table: "company_notes",
                columns: new[] { "CompanyId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_CompanyId_CreatedUtc",
                table: "company_outbox_messages",
                columns: new[] { "CompanyId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_CompanyId_ProcessedUtc",
                table: "company_outbox_messages",
                columns: new[] { "CompanyId", "ProcessedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_ProcessedUtc_AvailableUtc",
                table: "company_outbox_messages",
                columns: new[] { "ProcessedUtc", "AvailableUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_company_outbox_messages_ProcessedUtc_ClaimedUtc",
                table: "company_outbox_messages",
                columns: new[] { "ProcessedUtc", "ClaimedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_company_setup_templates_TemplateId",
                table: "company_setup_templates",
                column: "TemplateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_AuthProvider_AuthSubject",
                table: "users",
                columns: new[] { "AuthProvider", "AuthSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "company_invitations");

            migrationBuilder.DropTable(
                name: "company_memberships");

            migrationBuilder.DropTable(
                name: "company_notes");

            migrationBuilder.DropTable(
                name: "company_outbox_messages");

            migrationBuilder.DropTable(
                name: "company_setup_templates");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "companies");
        }
    }
}
