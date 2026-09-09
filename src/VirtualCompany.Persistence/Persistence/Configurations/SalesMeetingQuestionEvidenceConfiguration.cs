using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesMeetingQuestionEvidenceConfiguration : IEntityTypeConfiguration<SalesMeetingQuestionEvidence>
{
    public void Configure(EntityTypeBuilder<SalesMeetingQuestionEvidence> builder)
    {
        builder.ToTable("sales_meeting_question_evidence", table => table.HasCheckConstraint("CK_sales_meeting_question_evidence_confidence", "confidence >= 0 AND confidence <= 1"));
        builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.QuestionId).HasColumnName("question_id"); builder.Property(x => x.ClaimOrder).HasColumnName("claim_order");
        builder.Property(x => x.ClaimText).HasColumnName("claim_text").HasMaxLength(4000); builder.Property(x => x.ClaimType).HasColumnName("claim_type").HasMaxLength(100);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4); builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(500);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(100); builder.Property(x => x.SourceTitle).HasColumnName("source_title").HasMaxLength(500); builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.HasIndex(x => new { x.CompanyId, x.QuestionId, x.ClaimOrder, x.SourceId }).IsUnique();
        builder.HasOne(x => x.Question).WithMany(x => x.Evidence).HasForeignKey(nameof(SalesMeetingQuestionEvidence.CompanyId), nameof(SalesMeetingQuestionEvidence.QuestionId)).HasPrincipalKey(nameof(SalesMeetingQuestion.CompanyId), nameof(SalesMeetingQuestion.Id)).OnDelete(DeleteBehavior.Cascade);
    }
}
