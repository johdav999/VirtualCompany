using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyGoalConfiguration : IEntityTypeConfiguration<CompanyGoal>
{
    public void Configure(EntityTypeBuilder<CompanyGoal> builder)
    {
        builder.ToTable("company_goals"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired(); builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => CompanyGoalStatusValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion(x => x.ToStorageValue(), x => CompanyGoalPriorityValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MetricKey).HasColumnName("metric_key").HasMaxLength(128); builder.Property(x => x.MetricUnit).HasColumnName("metric_unit").HasMaxLength(64);
        builder.Property(x => x.BaselineValue).HasColumnName("baseline_value").HasPrecision(19, 4); builder.Property(x => x.TargetValue).HasColumnName("target_value").HasPrecision(19, 4);
        builder.Property(x => x.StartUtc).HasColumnName("start_at").IsRequired(); builder.Property(x => x.TargetUtc).HasColumnName("target_at").IsRequired();
        builder.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); builder.Property(x => x.OwnerAgentId).HasColumnName("owner_agent_id");
        builder.Property(x => x.Constraints).HasColumnName("constraints_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired(); builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.Priority }); builder.HasIndex(x => new { x.CompanyId, x.TargetUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.OwnerUser).WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.OwnerAgent).WithMany().HasForeignKey(x => x.OwnerAgentId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CompanyOperatingConfigurationConfiguration : IEntityTypeConfiguration<CompanyOperatingConfiguration>
{
    public void Configure(EntityTypeBuilder<CompanyOperatingConfiguration> builder)
    {
        builder.ToTable("company_operating_configurations"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.Property(x => x.CoordinatorAgentId).HasColumnName("coordinator_agent_id");
        builder.Property(x => x.AutonomyLevel).HasColumnName("autonomy_level").HasConversion(x => x.ToStorageValue(), x => CompanyAutonomyLevelValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(100).IsRequired(); builder.Property(x => x.DailyCycleHour).HasColumnName("daily_cycle_hour");
        builder.Property(x => x.MinimumCycleIntervalMinutes).HasColumnName("minimum_cycle_interval_minutes"); builder.Property(x => x.MaximumCyclesPerDay).HasColumnName("maximum_cycles_per_day");
        builder.Property(x => x.MaximumInitiativesPerCycle).HasColumnName("maximum_initiatives_per_cycle"); builder.Property(x => x.MaximumTasksPerCycle).HasColumnName("maximum_tasks_per_cycle");
        builder.Property(x => x.MaximumCollaborators).HasColumnName("maximum_collaborators"); builder.Property(x => x.MaximumRuntimeSeconds).HasColumnName("maximum_runtime_seconds");
        builder.Property(x => x.MaximumModelCallsPerCycle).HasColumnName("maximum_model_calls_per_cycle"); builder.Property(x => x.MaximumToolCallsPerCycle).HasColumnName("maximum_tool_calls_per_cycle");
        builder.Property(x => x.MaximumMonetaryBudgetPerCycle).HasColumnName("maximum_monetary_budget_per_cycle").HasPrecision(19, 4);
        builder.Property(x => x.MaximumTasksPerDay).HasColumnName("maximum_tasks_per_day"); builder.Property(x => x.MaximumModelCallsPerDay).HasColumnName("maximum_model_calls_per_day"); builder.Property(x => x.MaximumToolCallsPerDay).HasColumnName("maximum_tool_calls_per_day"); builder.Property(x => x.MaximumMonetaryBudgetPerDay).HasColumnName("maximum_monetary_budget_per_day").HasPrecision(19, 4);
        builder.Property(x => x.IsPaused).HasColumnName("is_paused"); builder.Property(x => x.PauseReason).HasColumnName("pause_reason").HasMaxLength(500);
        builder.Property(x => x.EmergencyStopped).HasColumnName("emergency_stopped"); builder.Property(x => x.EmergencyStopReason).HasColumnName("emergency_stop_reason").HasMaxLength(500); builder.Property(x => x.EmergencyStoppedUtc).HasColumnName("emergency_stopped_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CoordinatorAgent).WithMany().HasForeignKey(x => x.CoordinatorAgentId).OnDelete(DeleteBehavior.NoAction);
    }
}
