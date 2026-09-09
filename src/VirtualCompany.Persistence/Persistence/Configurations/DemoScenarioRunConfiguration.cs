using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class DemoScenarioRunConfiguration : IEntityTypeConfiguration<DemoScenarioRun>
{
    public void Configure(EntityTypeBuilder<DemoScenarioRun> builder)
    {
        builder.ToTable("demo_scenario_runs", table =>
        {
            table.HasCheckConstraint("CK_demo_scenario_runs_version", "scenario_version >= 1");
            table.HasCheckConstraint("CK_demo_scenario_runs_step", "current_step >= 0");
            table.HasCheckConstraint("CK_demo_scenario_runs_generation", "reset_generation >= 1");
            table.HasCheckConstraint("CK_demo_scenario_runs_status", "status IN ('ready', 'running', 'completed')");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ScenarioKey).HasColumnName("scenario_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ScenarioVersion).HasColumnName("scenario_version").IsRequired();
        builder.Property(x => x.ProvisionedByUserId).HasColumnName("provisioned_by_user_id").IsRequired();
        builder.Property(x => x.MeetingSessionId).HasColumnName("meeting_session_id");
        builder.Property(x => x.LinkedByUserId).HasColumnName("linked_by_user_id");
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(
                x => x == DemoScenarioRunStatus.Ready ? "ready" : x == DemoScenarioRunStatus.Running ? "running" : "completed",
                x => x == "ready" ? DemoScenarioRunStatus.Ready : x == "running" ? DemoScenarioRunStatus.Running : DemoScenarioRunStatus.Completed)
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.CurrentStep).HasColumnName("current_step").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ResetGeneration).HasColumnName("reset_generation").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.LastResetUtc).HasColumnName("last_reset_at").IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version")
            .HasDefaultValue(1L).IsConcurrencyToken().IsRequired();
        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ScenarioKey, x.ScenarioVersion });
        builder.HasIndex(x => new { x.CompanyId, x.MeetingSessionId }).IsUnique().HasFilter("[meeting_session_id] IS NOT NULL");
        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SalesMeetingSession>().WithMany()
            .HasForeignKey(nameof(DemoScenarioRun.CompanyId), nameof(DemoScenarioRun.MeetingSessionId))
            .HasPrincipalKey(nameof(SalesMeetingSession.CompanyId), nameof(SalesMeetingSession.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}
