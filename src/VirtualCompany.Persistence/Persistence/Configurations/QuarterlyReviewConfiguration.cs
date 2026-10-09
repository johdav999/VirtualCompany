using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class QuarterlyReviewConfiguration : IEntityTypeConfiguration<QuarterlyReview>
{
    public void Configure(EntityTypeBuilder<QuarterlyReview> b)
    {
        b.ToTable("quarterly_reviews"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.FiscalYear, x.Quarter, x.Revision }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.PreviousId }).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.Property(x => x.Notes).HasMaxLength(4000); b.Property(x => x.Fingerprint).HasMaxLength(64);
        b.Property(x => x.CommandHash).HasMaxLength(64); b.Property(x => x.Timezone).HasMaxLength(128); b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.SavedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Property(x => x.StartUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Property(x => x.EndUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.HasOne<QuarterlyReview>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.PreviousId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Objectives).WithOne().HasForeignKey(x => new { x.CompanyId, x.ReviewId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Resources).WithOne().HasForeignKey(x => new { x.CompanyId, x.ReviewId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class QuarterlyObjectiveConfiguration : IEntityTypeConfiguration<QuarterlyObjective>
{
    public void Configure(EntityTypeBuilder<QuarterlyObjective> b)
    {
        b.ToTable("quarterly_objectives"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.ReviewId, x.GoalId }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.OwnerName).HasMaxLength(200);
        b.Property(x => x.Unit).HasMaxLength(64); b.Property(x => x.Direction).HasMaxLength(16);
        b.Property(x => x.Baseline).HasPrecision(19, 4); b.Property(x => x.Target).HasPrecision(19, 4);
        b.HasOne<CompanyGoal>().WithMany().HasForeignKey(x => new { x.CompanyId, x.GoalId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.Measures).WithOne().HasForeignKey(x => new { x.CompanyId, x.ObjectiveId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Milestones).WithOne().HasForeignKey(x => new { x.CompanyId, x.ObjectiveId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Initiatives).WithOne().HasForeignKey(x => new { x.CompanyId, x.ObjectiveId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class QuarterlyMeasureLinkConfiguration : IEntityTypeConfiguration<QuarterlyMeasureLink>
{
    public void Configure(EntityTypeBuilder<QuarterlyMeasureLink> b)
    {
        b.ToTable("quarterly_measure_links"); b.HasKey(x => x.Id);
        b.Property(x => x.MeasureKey).HasMaxLength(128); b.Property(x => x.SnapshotChecksum).HasMaxLength(64);
        b.HasIndex(x => new { x.ObjectiveId, x.SnapshotId }).IsUnique();
        b.HasOne<MonthlyReviewSnapshot>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SnapshotId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
internal sealed class QuarterlyMilestoneConfiguration : IEntityTypeConfiguration<QuarterlyMilestone>
{
    public void Configure(EntityTypeBuilder<QuarterlyMilestone> b)
    { b.ToTable("quarterly_milestones"); b.HasKey(x => x.Id); b.Property(x => x.Title).HasMaxLength(200); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.DueUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)); }
}
internal sealed class QuarterlyInitiativeLinkConfiguration : IEntityTypeConfiguration<QuarterlyInitiativeLink>
{
    public void Configure(EntityTypeBuilder<QuarterlyInitiativeLink> b)
    {
        b.ToTable("quarterly_initiative_links"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.ObjectiveId, x.InitiativeId }).IsUnique();
        b.HasOne<OperatingInitiative>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InitiativeId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
internal sealed class QuarterlyResourceAllocationConfiguration : IEntityTypeConfiguration<QuarterlyResourceAllocation>
{
    public void Configure(EntityTypeBuilder<QuarterlyResourceAllocation> b)
    {
        b.ToTable("quarterly_resource_allocations"); b.HasKey(x => x.Id);
        b.Property(x => x.Pool).HasMaxLength(128); b.Property(x => x.Rationale).HasMaxLength(2000);
        b.Property(x => x.SnapshotChecksum).HasMaxLength(64);
        b.Property(x => x.AvailableHours).HasPrecision(19, 4); b.Property(x => x.ProposedHours).HasPrecision(19, 4);
        b.HasOne<MonthlyReviewSnapshot>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SnapshotId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<CompanyGoal>().WithMany().HasForeignKey(x => new { x.CompanyId, x.GoalId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
