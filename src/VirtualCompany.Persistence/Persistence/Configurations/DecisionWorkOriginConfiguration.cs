using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class DecisionWorkOriginConfiguration : IEntityTypeConfiguration<DecisionWorkOrigin>
{
    public void Configure(EntityTypeBuilder<DecisionWorkOrigin> b)
    {
        b.ToTable("decision_work_origins", t => t.HasCheckConstraint("CK_decision_work_source", "([SourceKind] = 'month' AND [MonthlySnapshotId] = [SourceVersionId] AND [MonthlySnapshotId] IS NOT NULL AND [QuarterReviewId] IS NULL AND [AnnualPlanId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'quarter' AND [QuarterReviewId] = [SourceVersionId] AND [QuarterReviewId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [AnnualPlanId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'annual' AND [AnnualPlanId] = [SourceVersionId] AND [AnnualPlanId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [QuarterReviewId] IS NULL AND [ScenarioId] IS NULL) OR ([SourceKind] = 'scenario' AND [ScenarioId] = [SourceVersionId] AND [ScenarioId] IS NOT NULL AND [MonthlySnapshotId] IS NULL AND [QuarterReviewId] IS NULL AND [AnnualPlanId] IS NULL)"));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasIndex(x => new { x.CompanyId, x.TaskId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.RequestId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SourceKind, x.SourceVersionId, x.ItemKey }).IsUnique();
        b.Property(x => x.SourceKind).HasMaxLength(16); b.Property(x => x.ItemKey).HasMaxLength(128);
        b.Property(x => x.Objective).HasMaxLength(200); b.Property(x => x.AcceptanceOutcome).HasMaxLength(2000);
        b.Property(x => x.ProposedConstraints).HasMaxLength(2000);
        b.Property(x => x.CommandHash).HasMaxLength(64); b.Property(x => x.SourceFingerprint).HasMaxLength(64); b.Property(x => x.PreviewChecksum).HasMaxLength(64);
        b.Property(x => x.CreatedUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.Property(x => x.DueUtc).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<WorkTask>().WithOne(x => x.DecisionOrigin).HasForeignKey<DecisionWorkOrigin>(x => new { x.CompanyId, Id = x.TaskId }).HasPrincipalKey<WorkTask>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<MonthlyReviewSnapshot>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.MonthlySnapshotId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<QuarterlyReview>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.QuarterReviewId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AnnualPlanVersion>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.AnnualPlanId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<StrategicScenarioVersion>().WithMany().HasForeignKey(x => new { x.CompanyId, Id = x.ScenarioId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x => x.Collaborators).WithOne().HasForeignKey(x => new { x.CompanyId, x.OriginId }).HasPrincipalKey(x => new { x.CompanyId, x.Id });
    }
}
internal sealed class DecisionWorkCollaboratorConfiguration : IEntityTypeConfiguration<DecisionWorkCollaborator>
{
    public void Configure(EntityTypeBuilder<DecisionWorkCollaborator> b)
    {
        b.ToTable("decision_work_collaborators"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.OriginId, x.UserId }).IsUnique(); b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
