using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CollaborationContributionConfiguration : IEntityTypeConfiguration<CollaborationContribution>
{
    public void Configure(EntityTypeBuilder<CollaborationContribution> b)
    {
        b.ToTable("collaboration_contributions"); b.HasKey(x => x.Id);
        b.Property(x => x.Objective).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Status).HasMaxLength(32).IsRequired();
        b.Property(x => x.Rationale).HasMaxLength(2000); b.Property(x => x.ReviewOutcome).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.ParentTaskId, x.Sequence, x.Version }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.SourceTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Agent>().WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class CollaborationArtifactHandoffConfiguration : IEntityTypeConfiguration<CollaborationArtifactHandoff>
{
    public void Configure(EntityTypeBuilder<CollaborationArtifactHandoff> b)
    {
        b.ToTable("collaboration_artifact_handoffs"); b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.InputContributionId, x.ReceivingContributionId }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CollaborationContribution>().WithMany().HasForeignKey(x => x.InputContributionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CollaborationContribution>().WithMany().HasForeignKey(x => x.ReceivingContributionId).OnDelete(DeleteBehavior.Restrict);
    }
}
