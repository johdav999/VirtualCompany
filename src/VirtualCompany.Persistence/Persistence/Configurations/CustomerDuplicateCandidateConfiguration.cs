using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CustomerDuplicateCandidateConfiguration : IEntityTypeConfiguration<CustomerDuplicateCandidate>
{
    public void Configure(EntityTypeBuilder<CustomerDuplicateCandidate> b)
    {
        b.ToTable("customer_duplicate_candidates"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.FirstCounterpartyId).HasColumnName("first_counterparty_id"); b.Property(x => x.SecondCounterpartyId).HasColumnName("second_counterparty_id");
        b.Property(x => x.Score).HasColumnName("score"); b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(4000);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.MergeSourceCounterpartyId).HasColumnName("merge_source_counterparty_id");
        b.Property(x => x.MergeTargetCounterpartyId).HasColumnName("merge_target_counterparty_id"); b.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
        b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id"); b.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        b.Property(x => x.DetectedUtc).HasColumnName("detected_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.FirstCounterpartyId, x.SecondCounterpartyId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.FirstCounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SecondCounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
