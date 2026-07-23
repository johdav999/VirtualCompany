using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260505130000_AddCustomerMemoryProfiles")]
public partial class AddCustomerMemoryProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_memory_profiles",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ai_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                relationship_memory = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                last_outreach_summary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                engagement_score = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                updated_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profiles", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profiles_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profiles_engagement_score_range", "engagement_score IS NULL OR (engagement_score >= 0 AND engagement_score <= 100)");
                table.ForeignKey(
                    name: "FK_customer_memory_profiles_companies_company_id",
                    column: x => x.company_id,
                    principalTable: "companies",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_customer_memory_profiles_contacts_company_id_contact_id",
                    columns: x => new { x.company_id, x.contact_id },
                    principalTable: "contacts",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[conversations]', N'U') IS NOT NULL
   AND NOT EXISTS (
       SELECT 1 FROM sys.indexes
       WHERE name = N'IX_conversations_company_id_id'
         AND object_id = OBJECT_ID(N'[dbo].[conversations]'))
BEGIN
    CREATE UNIQUE INDEX [IX_conversations_company_id_id]
    ON [conversations] ([company_id], [id]);
END;
");

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_conversations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                last_message_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                relevance = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_conversations", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_conversations_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profile_conversations_relevance_range", "relevance IS NULL OR (relevance >= 0 AND relevance <= 1)");
                table.ForeignKey(
                    name: "FK_customer_memory_profile_conversations_conversations_company_id_conversation_id",
                    columns: x => new { x.company_id, x.conversation_id },
                    principalTable: "conversations",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_customer_memory_profile_conversations_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_deals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                deal_role = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                outcome = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                closed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_deals", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_deals_company_id_id", x => new { x.company_id, x.id });
                table.ForeignKey(
                    name: "FK_customer_memory_profile_deals_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_customer_memory_profile_deals_deals_company_id_deal_id",
                    columns: x => new { x.company_id, x.deal_id },
                    principalTable: "deals",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_engagement_attributes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                attribute_type = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                attribute_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                attribute_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                score_impact = table.Column<decimal>(type: "decimal(6,3)", nullable: true),
                observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'{}'"),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_engagement_attributes", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_engagement_attributes_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profile_engagement_attributes_score_impact_range", "score_impact IS NULL OR (score_impact >= -100 AND score_impact <= 100)");
                table.ForeignKey(
                    name: "FK_customer_memory_profile_engagement_attributes_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_industry_signals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                signal_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                signal_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                confidence = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                source_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_industry_signals", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_industry_signals_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profile_industry_signals_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                table.ForeignKey(
                    name: "FK_customer_memory_profile_industry_signals_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_preferences",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                preference_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                preference_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                source_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                confidence = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_preferences", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_preferences_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profile_preferences_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                table.ForeignKey(
                    name: "FK_customer_memory_profile_preferences_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "customer_memory_profile_price_signals",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                customer_memory_profile_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                signal_key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                signal_value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                confidence = table.Column<decimal>(type: "decimal(5,3)", nullable: true),
                observed_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                source_summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_memory_profile_price_signals", x => x.id);
                table.UniqueConstraint("AK_customer_memory_profile_price_signals_company_id_id", x => new { x.company_id, x.id });
                table.CheckConstraint("CK_customer_memory_profile_price_signals_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
                table.ForeignKey(
                    name: "FK_customer_memory_profile_price_signals_customer_memory_profiles_company_id_customer_memory_profile_id",
                    columns: x => new { x.company_id, x.customer_memory_profile_id },
                    principalTable: "customer_memory_profiles",
                    principalColumns: new[] { "company_id", "id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profiles_company_id",
            table: "customer_memory_profiles",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profiles_contact_id",
            table: "customer_memory_profiles",
            column: "contact_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profiles_company_id_contact_id",
            table: "customer_memory_profiles",
            columns: new[] { "company_id", "contact_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profiles_company_id_updated_at",
            table: "customer_memory_profiles",
            columns: new[] { "company_id", "updated_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_conversations_company_id",
            table: "customer_memory_profile_conversations",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_conversations_company_id_conversation_id",
            table: "customer_memory_profile_conversations",
            columns: new[] { "company_id", "conversation_id" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_conversations_company_id_customer_memory_profile_id_conversation_id",
            table: "customer_memory_profile_conversations",
            columns: new[] { "company_id", "customer_memory_profile_id", "conversation_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_conversations_company_id_last_message_at",
            table: "customer_memory_profile_conversations",
            columns: new[] { "company_id", "last_message_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_deals_company_id",
            table: "customer_memory_profile_deals",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_deals_company_id_deal_id",
            table: "customer_memory_profile_deals",
            columns: new[] { "company_id", "deal_id" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_deals_company_id_customer_memory_profile_id_deal_id",
            table: "customer_memory_profile_deals",
            columns: new[] { "company_id", "customer_memory_profile_id", "deal_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_deals_company_id_outcome_closed_at",
            table: "customer_memory_profile_deals",
            columns: new[] { "company_id", "outcome", "closed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_engagement_attributes_company_id",
            table: "customer_memory_profile_engagement_attributes",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_engagement_attributes_company_id_customer_memory_profile_id_attribute_type_attribute_key",
            table: "customer_memory_profile_engagement_attributes",
            columns: new[] { "company_id", "customer_memory_profile_id", "attribute_type", "attribute_key" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_engagement_attributes_company_id_attribute_type_observed_at",
            table: "customer_memory_profile_engagement_attributes",
            columns: new[] { "company_id", "attribute_type", "observed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_industry_signals_company_id",
            table: "customer_memory_profile_industry_signals",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_industry_signals_company_id_customer_memory_profile_id_signal_key",
            table: "customer_memory_profile_industry_signals",
            columns: new[] { "company_id", "customer_memory_profile_id", "signal_key" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_industry_signals_company_id_signal_key_observed_at",
            table: "customer_memory_profile_industry_signals",
            columns: new[] { "company_id", "signal_key", "observed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_preferences_company_id",
            table: "customer_memory_profile_preferences",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_preferences_company_id_customer_memory_profile_id_preference_key",
            table: "customer_memory_profile_preferences",
            columns: new[] { "company_id", "customer_memory_profile_id", "preference_key" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_preferences_company_id_preference_key_observed_at",
            table: "customer_memory_profile_preferences",
            columns: new[] { "company_id", "preference_key", "observed_at" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_price_signals_company_id",
            table: "customer_memory_profile_price_signals",
            column: "company_id");

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_price_signals_company_id_customer_memory_profile_id_signal_key",
            table: "customer_memory_profile_price_signals",
            columns: new[] { "company_id", "customer_memory_profile_id", "signal_key" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_memory_profile_price_signals_company_id_signal_key_observed_at",
            table: "customer_memory_profile_price_signals",
            columns: new[] { "company_id", "signal_key", "observed_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("customer_memory_profile_conversations");
        migrationBuilder.DropTable("customer_memory_profile_deals");
        migrationBuilder.DropTable("customer_memory_profile_engagement_attributes");
        migrationBuilder.DropTable("customer_memory_profile_industry_signals");
        migrationBuilder.DropTable("customer_memory_profile_preferences");
        migrationBuilder.DropTable("customer_memory_profile_price_signals");
        migrationBuilder.DropTable("customer_memory_profiles");
    }
}
