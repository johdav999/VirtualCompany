using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidedDialogueAndRealtimeVoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guided_work_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    artifact_type = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    schema_version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    target_artifact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    target_artifact_version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    required_field_count = table.Column<int>(type: "int", nullable: false),
                    ready_field_count = table.Column<int>(type: "int", nullable: false),
                    safe_summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    next_question = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    review_token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    review_token_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_work_sessions", x => x.id);
                    table.UniqueConstraint("AK_guided_work_sessions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_guided_work_sessions_agents_company_id_agent_id",
                        columns: x => new { x.company_id, x.agent_id },
                        principalTable: "agents",
                        principalColumns: new[] { "CompanyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_guided_work_sessions_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_guided_work_sessions_conversations_company_id_conversation_id",
                        columns: x => new { x.company_id, x.conversation_id },
                        principalTable: "conversations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_guided_work_sessions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "guided_draft_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    path = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    value_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    is_required = table.Column<bool>(type: "bit", nullable: false),
                    value_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    source_metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                    explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_draft_fields", x => x.id);
                    table.ForeignKey(
                        name: "FK_guided_draft_fields_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_guided_draft_fields_guided_work_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "guided_work_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guided_session_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    operation_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    response_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_session_operations", x => x.id);
                    table.ForeignKey(
                        name: "FK_guided_session_operations_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_guided_session_operations_guided_work_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "guided_work_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guided_voice_bindings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider_call_id = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    reconnect_count = table.Column<int>(type: "int", nullable: false),
                    last_provider_event_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guided_voice_bindings", x => x.id);
                    table.ForeignKey(
                        name: "FK_guided_voice_bindings_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_guided_voice_bindings_guided_work_sessions_company_id_session_id",
                        columns: x => new { x.company_id, x.session_id },
                        principalTable: "guided_work_sessions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_guided_voice_bindings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_guided_draft_fields_company_id_session_id_path",
                table: "guided_draft_fields",
                columns: new[] { "company_id", "session_id", "path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guided_session_operations_company_id_session_id_operation_type_client_request_id",
                table: "guided_session_operations",
                columns: new[] { "company_id", "session_id", "operation_type", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guided_voice_bindings_company_id_session_id_status",
                table: "guided_voice_bindings",
                columns: new[] { "company_id", "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_guided_voice_bindings_provider_call_id",
                table: "guided_voice_bindings",
                column: "provider_call_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_guided_voice_bindings_user_id",
                table: "guided_voice_bindings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_guided_work_sessions_company_id_agent_id_artifact_type_status",
                table: "guided_work_sessions",
                columns: new[] { "company_id", "agent_id", "artifact_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_guided_work_sessions_company_id_conversation_id",
                table: "guided_work_sessions",
                columns: new[] { "company_id", "conversation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_guided_work_sessions_company_id_created_by_user_id_status_updated_at",
                table: "guided_work_sessions",
                columns: new[] { "company_id", "created_by_user_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_guided_work_sessions_created_by_user_id",
                table: "guided_work_sessions",
                column: "created_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guided_draft_fields");

            migrationBuilder.DropTable(
                name: "guided_session_operations");

            migrationBuilder.DropTable(
                name: "guided_voice_bindings");

            migrationBuilder.DropTable(
                name: "guided_work_sessions");
        }
    }
}
