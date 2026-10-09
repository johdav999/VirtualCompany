using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SupportIssueGroupingRevisionConfiguration : IEntityTypeConfiguration<SupportIssueGroupingRevision>
{
    public void Configure(EntityTypeBuilder<SupportIssueGroupingRevision> b)
    {
        b.ToTable("support_issue_grouping_revisions"); b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        b.Property(x=>x.Group).HasMaxLength(80); b.Property(x=>x.Reason).HasMaxLength(1000);
        b.Property(x=>x.SourceFingerprint).HasMaxLength(64); b.Property(x=>x.RequestHash).HasMaxLength(64);
        b.HasIndex(x=>new{x.CompanyId,x.ActorId,x.RequestId}).IsUnique();
        b.HasIndex(x=>new{x.CompanyId,x.SupportCaseId,x.SavedUtc});
        b.HasIndex(x=>new{x.CompanyId,x.PreviousId}).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SupportCase>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.SupportCaseId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SupportIssueGroupingRevision>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.PreviousId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
    }
}
