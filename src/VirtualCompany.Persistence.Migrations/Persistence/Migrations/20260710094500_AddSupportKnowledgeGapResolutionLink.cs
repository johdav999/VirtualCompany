using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualCompany.Infrastructure.Persistence.Migrations;

[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(VirtualCompanyDbContext))]
[Migration("20260710094500_AddSupportKnowledgeGapResolutionLink")]
public partial class AddSupportKnowledgeGapResolutionLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("linked_knowledge_document_id", "support_knowledge_gaps", "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex("IX_support_knowledge_gaps_company_id_linked_knowledge_document_id", "support_knowledge_gaps", ["company_id", "linked_knowledge_document_id"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_support_knowledge_gaps_company_id_linked_knowledge_document_id", "support_knowledge_gaps");
        migrationBuilder.DropColumn("linked_knowledge_document_id", "support_knowledge_gaps");
    }
}
