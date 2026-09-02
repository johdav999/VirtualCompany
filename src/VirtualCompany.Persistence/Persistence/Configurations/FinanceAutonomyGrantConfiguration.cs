using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAutonomyGrantConfiguration : IEntityTypeConfiguration<FinanceAutonomyGrant>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyGrant> builder)
    {
        builder.ToTable("finance_autonomy_grants");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.LatestVersionNumber).HasColumnName("latest_version_number").IsRequired();
        builder.Property(x => x.ActiveVersionId).HasColumnName("active_version_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CapabilityId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Versions).WithOne(x => x.Grant).HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceAutonomyGrantVersionConfiguration : IEntityTypeConfiguration<FinanceAutonomyGrantVersion>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyGrantVersion> builder)
    {
        builder.ToTable("finance_autonomy_grant_versions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.GrantId).HasColumnName("grant_id").IsRequired();
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.Level).HasColumnName("level")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyEnumValues.ParseFinanceAutonomyLevel(x))
            .HasMaxLength(48).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyEnumValues.ParseFinanceAutonomyGrantStatus(x))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.AllowedTriggers).HasColumnName("allowed_triggers_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.AllowedActionClasses).HasColumnName("allowed_action_classes_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.AllowedTools).HasColumnName("allowed_tools_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.AllowedEventTypes).HasColumnName("allowed_event_types_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.MaximumRecordsPerRun).HasColumnName("maximum_records_per_run").IsRequired();
        builder.Property(x => x.MaximumAmountPerRun).HasColumnName("maximum_amount_per_run").HasPrecision(19, 4);
        builder.Property(x => x.MaximumActionsPerRun).HasColumnName("maximum_actions_per_run").IsRequired();
        builder.Property(x => x.ScheduleExpression).HasColumnName("schedule_expression").HasMaxLength(160);
        builder.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(100).IsRequired();
        builder.Property(x => x.WindowStartLocal).HasColumnName("window_start_local").HasMaxLength(5).IsRequired();
        builder.Property(x => x.WindowEndLocal).HasColumnName("window_end_local").HasMaxLength(5).IsRequired();
        builder.Property(x => x.EvidenceFreshnessMinutes).HasColumnName("evidence_freshness_minutes").IsRequired();
        builder.Property(x => x.MinimumIntervalMinutes).HasColumnName("minimum_interval_minutes").HasDefaultValue(60).IsRequired();
        builder.Property(x => x.MaximumRunsPerWindow).HasColumnName("maximum_runs_per_window").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.DebounceMinutes).HasColumnName("debounce_minutes").HasDefaultValue(5).IsRequired();
        builder.Property(x => x.CatchUpBehavior).HasColumnName("catch_up_behavior").HasMaxLength(16).HasDefaultValue("latest").IsRequired();
        builder.Property(x => x.MaximumCatchUpWindows).HasColumnName("maximum_catch_up_windows").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.LateEventToleranceMinutes).HasColumnName("late_event_tolerance_minutes").HasDefaultValue(1440).IsRequired();
        builder.Property(x => x.ConfirmationBehavior).HasColumnName("confirmation_behavior").HasMaxLength(48).IsRequired();
        builder.Property(x => x.EscalationRoute).HasColumnName("escalation_route").HasMaxLength(240).IsRequired();
        builder.Property(x => x.EffectiveFromUtc).HasColumnName("effective_from_utc").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_utc");
        builder.Property(x => x.CatalogueVersion).HasColumnName("catalogue_version").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CapabilityPolicyHash).HasColumnName("capability_policy_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AuthorityVersion).HasColumnName("authority_version").HasMaxLength(100).IsRequired();
        builder.Property(x => x.AuthorityHash).HasColumnName("authority_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(1000);
        builder.Property(x => x.ReviewedUtc).HasColumnName("reviewed_utc");
        builder.Property(x => x.ActivatedUtc).HasColumnName("activated_utc");
        builder.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.Property(x => x.RevocationReason).HasColumnName("revocation_reason").HasMaxLength(1000);
        builder.Property(x => x.RevokedUtc).HasColumnName("revoked_utc");
        builder.HasIndex(x => new { x.CompanyId, x.GrantId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresUtc });
    }
}

internal sealed class FinanceAutonomyControlConfiguration : IEntityTypeConfiguration<FinanceAutonomyControl>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyControl> builder)
    {
        builder.ToTable("finance_autonomy_controls");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Scope).HasColumnName("scope")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyEnumValues.ParseFinanceAutonomyControlScope(x))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160);
        builder.Property(x => x.State).HasColumnName("state")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyEnumValues.ParseFinanceAutonomyControlState(x))
            .HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000);
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.ScopeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.State });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
