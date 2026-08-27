using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CustomerCounterpartyRedirectConfiguration : IEntityTypeConfiguration<CustomerCounterpartyRedirect>
{
    public void Configure(EntityTypeBuilder<CustomerCounterpartyRedirect> b)
    {
        b.ToTable("customer_counterparty_redirects"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SourceCounterpartyId).HasColumnName("source_counterparty_id"); b.Property(x => x.TargetCounterpartyId).HasColumnName("target_counterparty_id");
        b.Property(x => x.DuplicateCandidateId).HasColumnName("duplicate_candidate_id"); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.SourceCounterpartyId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.TargetCounterpartyId });
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceCounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.TargetCounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerDuplicateCandidate>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DuplicateCandidateId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
