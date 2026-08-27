using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Persistence.Migrations.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCollectionPolicyExceptionsR2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_collection_policy_exceptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    company_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    policy_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    customer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    excluded_until_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_utc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_collection_policy_exceptions", x => x.id);
                    table.UniqueConstraint("AK_customer_collection_policy_exceptions_company_id_id", x => new { x.company_id, x.id });
                    table.ForeignKey(
                        name: "FK_customer_collection_policy_exceptions_customer_collection_policies_company_id_policy_id",
                        columns: x => new { x.company_id, x.policy_id },
                        principalTable: "customer_collection_policies",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_collection_policy_exceptions_finance_counterparties_company_id_customer_id",
                        columns: x => new { x.company_id, x.customer_id },
                        principalTable: "finance_counterparties",
                        principalColumns: new[] { "company_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_policy_exceptions_company_id_customer_id",
                table: "customer_collection_policy_exceptions",
                columns: new[] { "company_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_collection_policy_exceptions_company_id_policy_id_customer_id",
                table: "customer_collection_policy_exceptions",
                columns: new[] { "company_id", "policy_id", "customer_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_collection_policy_exceptions");
        }
    }
}
