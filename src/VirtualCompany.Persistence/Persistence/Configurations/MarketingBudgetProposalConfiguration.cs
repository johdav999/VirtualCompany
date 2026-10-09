using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class MarketingBudgetProposalConfiguration : IEntityTypeConfiguration<MarketingBudgetProposalRevision>
{
    public void Configure(EntityTypeBuilder<MarketingBudgetProposalRevision> b)
    {
        b.ToTable("marketing_budget_proposal_revisions"); b.HasKey(x => x.Id);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.Checksum).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.AccountableUserId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SeriesId, x.Revision }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.PreviousId }).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.AccountableUserId, x.SavedAtUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
