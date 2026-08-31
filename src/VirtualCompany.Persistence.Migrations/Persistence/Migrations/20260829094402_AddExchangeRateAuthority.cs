using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExchangeRateAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_currency_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    minor_unit_precision = table.Column<int>(type: "int", nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_currency_definitions", x => x.id);
                    table.UniqueConstraint("AK_company_currency_definitions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_conversions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    input_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    input_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    output_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    effective_rate = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    unrounded_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    rounded_amount = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    rounding_residual = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    output_precision = table.Column<int>(type: "int", nullable: false),
                    rounding_mode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_conversions", x => x.id);
                    table.UniqueConstraint("AK_exchange_rate_conversions_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    priority = table.Column<int>(type: "int", nullable: false),
                    requires_approval = table.Column<bool>(type: "bit", nullable: false),
                    max_staleness_days = table.Column<int>(type: "int", nullable: false),
                    refresh_interval_hours = table.Column<int>(type: "int", nullable: false),
                    license_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    is_enabled = table.Column<bool>(type: "bit", nullable: false),
                    last_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_successful_refresh_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    next_refresh_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    last_failure_reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    last_failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_sources", x => x.id);
                    table.UniqueConstraint("AK_exchange_rate_sources_company_id_id", x => new { x.company_id, x.id });
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    set_version = table.Column<long>(type: "bigint", nullable: false),
                    import_identity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_through = table.Column<DateOnly>(type: "date", nullable: false),
                    published_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    imported_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    corrects_rate_set_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    review_note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_sets", x => x.id);
                    table.UniqueConstraint("AK_exchange_rate_sets_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_exchange_rate_sets_exchange_rate_sets_company_id_corrects_rate_set_id",
                        columns: x => new { x.company_id, x.corrects_rate_set_id },
                        principalTable: "exchange_rate_sets",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_exchange_rate_sets_exchange_rate_sources_company_id_source_id",
                        columns: x => new { x.company_id, x.source_id },
                        principalTable: "exchange_rate_sources",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rate_set_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    checksum = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    protected_payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    retention_expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_evidence", x => x.id);
                    table.ForeignKey(
                        name: "FK_exchange_rate_evidence_exchange_rate_sets_company_id_rate_set_id",
                        columns: x => new { x.company_id, x.rate_set_id },
                        principalTable: "exchange_rate_sets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rate_set_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    base_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    quote_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    rate = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    rate_precision = table.Column<int>(type: "int", nullable: false),
                    quotation_convention = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    corrects_observation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_observations", x => x.id);
                    table.UniqueConstraint("AK_exchange_rate_observations_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_exchange_rate_observations_exchange_rate_observations_company_id_corrects_observation_id",
                        columns: x => new { x.company_id, x.corrects_observation_id },
                        principalTable: "exchange_rate_observations",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_exchange_rate_observations_exchange_rate_sets_company_id_rate_set_id",
                        columns: x => new { x.company_id, x.rate_set_id },
                        principalTable: "exchange_rate_sets",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_refresh_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    source_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    idempotency_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_currencies = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    lease_owner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    failure_reason_code = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    failure_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    rate_set_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_refresh_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_exchange_rate_refresh_jobs_exchange_rate_sets_company_id_rate_set_id",
                        columns: x => new { x.company_id, x.rate_set_id },
                        principalTable: "exchange_rate_sets",
                        principalColumns: new[] { "company_id", "id" });
                    table.ForeignKey(
                        name: "FK_exchange_rate_refresh_jobs_exchange_rate_sources_company_id_source_id",
                        columns: x => new { x.company_id, x.source_id },
                        principalTable: "exchange_rate_sources",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_rate_conversion_legs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<int>(type: "int", nullable: false),
                    observation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    to_currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    factor = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exchange_rate_conversion_legs", x => x.id);
                    table.ForeignKey(
                        name: "FK_exchange_rate_conversion_legs_exchange_rate_conversions_company_id_conversion_id",
                        columns: x => new { x.company_id, x.conversion_id },
                        principalTable: "exchange_rate_conversions",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_exchange_rate_conversion_legs_exchange_rate_observations_company_id_observation_id",
                        columns: x => new { x.company_id, x.observation_id },
                        principalTable: "exchange_rate_observations",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_currency_definitions_company_id_code",
                table: "company_currency_definitions",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_company_currency_definitions_company_id_is_enabled_code",
                table: "company_currency_definitions",
                columns: new[] { "company_id", "is_enabled", "code" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_conversion_legs_company_id_conversion_id_sequence",
                table: "exchange_rate_conversion_legs",
                columns: new[] { "company_id", "conversion_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_conversion_legs_company_id_observation_id",
                table: "exchange_rate_conversion_legs",
                columns: new[] { "company_id", "observation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_conversions_company_id_idempotency_key",
                table: "exchange_rate_conversions",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_conversions_company_id_requested_date_input_currency_output_currency",
                table: "exchange_rate_conversions",
                columns: new[] { "company_id", "requested_date", "input_currency", "output_currency" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_evidence_company_id_checksum",
                table: "exchange_rate_evidence",
                columns: new[] { "company_id", "checksum" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_evidence_company_id_rate_set_id",
                table: "exchange_rate_evidence",
                columns: new[] { "company_id", "rate_set_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_evidence_retention_expires_at",
                table: "exchange_rate_evidence",
                column: "retention_expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_observations_company_id_base_currency_quote_currency_effective_date",
                table: "exchange_rate_observations",
                columns: new[] { "company_id", "base_currency", "quote_currency", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_observations_company_id_corrects_observation_id",
                table: "exchange_rate_observations",
                columns: new[] { "company_id", "corrects_observation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_observations_company_id_rate_set_id_base_currency_quote_currency_effective_date",
                table: "exchange_rate_observations",
                columns: new[] { "company_id", "rate_set_id", "base_currency", "quote_currency", "effective_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_refresh_jobs_company_id_idempotency_key",
                table: "exchange_rate_refresh_jobs",
                columns: new[] { "company_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_refresh_jobs_company_id_rate_set_id",
                table: "exchange_rate_refresh_jobs",
                columns: new[] { "company_id", "rate_set_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_refresh_jobs_company_id_source_id_created_at",
                table: "exchange_rate_refresh_jobs",
                columns: new[] { "company_id", "source_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_refresh_jobs_status_next_attempt_at_lease_expires_at",
                table: "exchange_rate_refresh_jobs",
                columns: new[] { "status", "next_attempt_at", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sets_company_id_corrects_rate_set_id",
                table: "exchange_rate_sets",
                columns: new[] { "company_id", "corrects_rate_set_id" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sets_company_id_source_id_import_identity",
                table: "exchange_rate_sets",
                columns: new[] { "company_id", "source_id", "import_identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sets_company_id_source_id_set_version",
                table: "exchange_rate_sets",
                columns: new[] { "company_id", "source_id", "set_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sets_company_id_status_effective_from_effective_through",
                table: "exchange_rate_sets",
                columns: new[] { "company_id", "status", "effective_from", "effective_through" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sources_company_id_priority_is_enabled",
                table: "exchange_rate_sources",
                columns: new[] { "company_id", "priority", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sources_company_id_source_key",
                table: "exchange_rate_sources",
                columns: new[] { "company_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exchange_rate_sources_is_enabled_next_refresh_at_source_kind",
                table: "exchange_rate_sources",
                columns: new[] { "is_enabled", "next_refresh_at", "source_kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_currency_definitions");

            migrationBuilder.DropTable(
                name: "exchange_rate_conversion_legs");

            migrationBuilder.DropTable(
                name: "exchange_rate_evidence");

            migrationBuilder.DropTable(
                name: "exchange_rate_refresh_jobs");

            migrationBuilder.DropTable(
                name: "exchange_rate_conversions");

            migrationBuilder.DropTable(
                name: "exchange_rate_observations");

            migrationBuilder.DropTable(
                name: "exchange_rate_sets");

            migrationBuilder.DropTable(
                name: "exchange_rate_sources");
        }
    }
}
