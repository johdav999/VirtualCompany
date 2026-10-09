using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SupportCapacityProposalRevisionConfiguration : IEntityTypeConfiguration<SupportCapacityProposalRevision>
{
    public void Configure(EntityTypeBuilder<SupportCapacityProposalRevision> b)
    {
        b.ToTable("support_capacity_proposal_revisions"); b.HasKey(x=>x.Id); b.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        b.Property(x=>x.Name).HasMaxLength(120); b.Property(x=>x.Payload).HasColumnType("nvarchar(max)");
        foreach(var name in new[]{nameof(SupportCapacityProposalRevision.HandlingMinutes),nameof(SupportCapacityProposalRevision.AvailablePeople),nameof(SupportCapacityProposalRevision.HoursPerBusinessDay),nameof(SupportCapacityProposalRevision.UtilizationPercent)}) b.Property<decimal>(name).HasPrecision(12,2);
        b.Property(x=>x.Checksum).HasMaxLength(64); b.Property(x=>x.SourceFingerprint).HasMaxLength(64); b.Property(x=>x.RequestHash).HasMaxLength(64);
        b.HasIndex(x=>new{x.CompanyId,x.OwnerId,x.RequestId}).IsUnique();
        b.HasIndex(x=>new{x.CompanyId,x.OwnerId,x.SavedUtc});
        b.HasIndex(x=>new{x.CompanyId,x.PreviousId}).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SupportCapacityProposalRevision>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.PreviousId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
    }
}
