using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations
{
    public partial class AddMemoryItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS vector;""");

                migrationBuilder.CreateTable(
                    name: "memory_items",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                        AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                        MemoryType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                        Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                        SourceEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                        SourceEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                        Salience = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                        Metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'", name: "metadata_json"),
                        ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                        ValidToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                        Embedding = table.Column<string>(type: "vector", nullable: true),
                        EmbeddingProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                        EmbeddingModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                        EmbeddingModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                        EmbeddingDimensions = table.Column<int>(type: "integer", nullable: true),
                        CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_memory_items", x => x.Id);
                        table.ForeignKey(
                            name: "FK_memory_items_agents_AgentId",
                            column: x => x.AgentId,
                            principalTable: "agents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_memory_items_companies_CompanyId",
                            column: x => x.CompanyId,
                            principalTable: "companies",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "memory_items",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(nullable: false),
                        CompanyId = table.Column<Guid>(nullable: false),
                        AgentId = table.Column<Guid>(nullable: true),
                        MemoryType = table.Column<string>(maxLength: 32, nullable: false),
                        Summary = table.Column<string>(maxLength: 4000, nullable: false),
                        SourceEntityType = table.Column<string>(maxLength: 100, nullable: true),
                        SourceEntityId = table.Column<Guid>(nullable: true),
                        Salience = table.Column<decimal>(type: "decimal(4,3)", nullable: false),
                        Metadata = table.Column<string>(nullable: false, defaultValue: "{}", name: "metadata_json"),
                        ValidFromUtc = table.Column<DateTime>(nullable: false),
                        ValidToUtc = table.Column<DateTime>(nullable: true),
                        Embedding = table.Column<string>(type: "nvarchar(max)", nullable: true),
                        EmbeddingProvider = table.Column<string>(maxLength: 100, nullable: true),
                        EmbeddingModel = table.Column<string>(maxLength: 200, nullable: true),
                        EmbeddingModelVersion = table.Column<string>(maxLength: 100, nullable: true),
                        EmbeddingDimensions = table.Column<int>(nullable: true),
                        CreatedUtc = table.Column<DateTime>(nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_memory_items", x => x.Id);
                        table.ForeignKey(
                            name: "FK_memory_items_agents_AgentId",
                            column: x => x.AgentId,
                            principalTable: "agents",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Restrict);
                        table.ForeignKey(
                            name: "FK_memory_items_companies_CompanyId",
                            column: x => x.CompanyId,
                            principalTable: "companies",
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    });
            }

            migrationBuilder.CreateIndex(name: "IX_memory_items_AgentId", table: "memory_items", column: "AgentId");
            migrationBuilder.CreateIndex(name: "IX_memory_items_CompanyId_AgentId", table: "memory_items", columns: new[] { "CompanyId", "AgentId" });
            migrationBuilder.CreateIndex(name: "IX_memory_items_CompanyId_CreatedUtc", table: "memory_items", columns: new[] { "CompanyId", "CreatedUtc" });
            migrationBuilder.CreateIndex(name: "IX_memory_items_CompanyId_MemoryType", table: "memory_items", columns: new[] { "CompanyId", "MemoryType" });
            migrationBuilder.CreateIndex(name: "IX_memory_items_CompanyId_ValidToUtc", table: "memory_items", columns: new[] { "CompanyId", "ValidToUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "memory_items");
        }
    }
}