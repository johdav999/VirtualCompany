using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal static class FinanceAutonomyUsageColumnConfiguration
{
    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, FinanceAutonomyUsageValues> b, string prefix)
        where TOwner : class
    {
        b.Property(x => x.RecordsEvaluated).HasColumnName($"{prefix}_records_evaluated").IsRequired();
        b.Property(x => x.DraftsOrTasksCreated).HasColumnName($"{prefix}_drafts_tasks_created").IsRequired();
        b.Property(x => x.ExecuteAttempts).HasColumnName($"{prefix}_execute_attempts").IsRequired();
        b.Property(x => x.AmountExposure).HasColumnName($"{prefix}_amount_exposure").HasPrecision(19, 4).IsRequired();
        b.Property(x => x.ObjectBytes).HasColumnName($"{prefix}_object_bytes").IsRequired();
        b.Property(x => x.ExportsCreated).HasColumnName($"{prefix}_exports_created").IsRequired();
        b.Property(x => x.ModelCalls).HasColumnName($"{prefix}_model_calls").IsRequired();
        b.Property(x => x.ToolCalls).HasColumnName($"{prefix}_tool_calls").IsRequired();
        b.Property(x => x.EstimatedCost).HasColumnName($"{prefix}_estimated_cost").HasPrecision(19, 4).IsRequired();
        b.Property(x => x.Retries).HasColumnName($"{prefix}_retries").IsRequired();
        b.Property(x => x.RuntimeSeconds).HasColumnName($"{prefix}_runtime_seconds").IsRequired();
    }

    public static void Configure<TOwner>(OwnedNavigationBuilder<TOwner, FinanceAutonomyUsageLimits> b, string prefix)
        where TOwner : class
    {
        b.Property(x => x.RecordsEvaluated).HasColumnName($"{prefix}_records_evaluated");
        b.Property(x => x.DraftsOrTasksCreated).HasColumnName($"{prefix}_drafts_tasks_created");
        b.Property(x => x.ExecuteAttempts).HasColumnName($"{prefix}_execute_attempts");
        b.Property(x => x.AmountExposure).HasColumnName($"{prefix}_amount_exposure").HasPrecision(19, 4);
        b.Property(x => x.ObjectBytes).HasColumnName($"{prefix}_object_bytes");
        b.Property(x => x.ExportsCreated).HasColumnName($"{prefix}_exports_created");
        b.Property(x => x.ModelCalls).HasColumnName($"{prefix}_model_calls");
        b.Property(x => x.ToolCalls).HasColumnName($"{prefix}_tool_calls");
        b.Property(x => x.EstimatedCost).HasColumnName($"{prefix}_estimated_cost").HasPrecision(19, 4);
        b.Property(x => x.Retries).HasColumnName($"{prefix}_retries");
        b.Property(x => x.RuntimeSeconds).HasColumnName($"{prefix}_runtime_seconds");
    }
}

internal sealed class FinanceAutonomyBudgetPolicyConfiguration : IEntityTypeConfiguration<FinanceAutonomyBudgetPolicy>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyBudgetPolicy> b)
    {
        b.ToTable("finance_autonomy_budget_policies"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id"); b.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160);
        b.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(240).IsRequired();
        b.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(100).IsRequired();
        b.Property(x => x.WindowMinutes).HasColumnName("window_minutes").IsRequired();
        b.OwnsOne(x => x.PerRunLimits, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "per_run"));
        b.OwnsOne(x => x.WindowLimits, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "window_limit"));
        b.Property(x => x.PolicyDenialThreshold).HasColumnName("policy_denial_threshold").IsRequired();
        b.Property(x => x.InvalidPlanThreshold).HasColumnName("invalid_plan_threshold").IsRequired();
        b.Property(x => x.ProviderAmbiguityThreshold).HasColumnName("provider_ambiguity_threshold").IsRequired();
        b.Property(x => x.ErrorBurstThreshold).HasColumnName("error_burst_threshold").IsRequired();
        b.Property(x => x.StaleEvidenceThreshold).HasColumnName("stale_evidence_threshold").IsRequired();
        b.Property(x => x.AuditOutboxFailureThreshold).HasColumnName("audit_outbox_failure_threshold").IsRequired();
        b.Property(x => x.CircuitWindowMinutes).HasColumnName("circuit_window_minutes").IsRequired();
        b.Property(x => x.CircuitCooldownMinutes).HasColumnName("circuit_cooldown_minutes").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired(); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.ScopeKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.IsActive, x.AgentId, x.CapabilityId });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceAutonomyBudgetWindowConfiguration : IEntityTypeConfiguration<FinanceAutonomyBudgetWindow>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyBudgetWindow> b)
    {
        b.ToTable("finance_autonomy_budget_windows"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.PolicyId).HasColumnName("policy_id").IsRequired(); b.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").IsRequired();
        b.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").IsRequired();
        b.OwnsOne(x => x.Reserved, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "reserved"));
        b.OwnsOne(x => x.Consumed, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "consumed"));
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired(); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.PolicyId, x.WindowStartUtc, x.WindowEndUtc }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.WindowEndUtc, x.UpdatedUtc });
        b.HasOne(x => x.Policy).WithMany().HasForeignKey(x => new { x.CompanyId, x.PolicyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceAutonomyBudgetReservationConfiguration : IEntityTypeConfiguration<FinanceAutonomyBudgetReservation>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyBudgetReservation> b)
    {
        b.ToTable("finance_autonomy_budget_reservations"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.PolicyId).HasColumnName("policy_id").IsRequired(); b.Property(x => x.WindowId).HasColumnName("window_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired(); b.Property(x => x.StepId).HasColumnName("step_id").IsRequired();
        b.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired(); b.Property(x => x.ReservationKey).HasColumnName("reservation_key").HasMaxLength(200).IsRequired();
        b.OwnsOne(x => x.Planned, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "planned"));
        b.OwnsOne(x => x.Actual, owned => FinanceAutonomyUsageColumnConfiguration.Configure(owned, "actual"));
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyBudgetEnumValues.ParseReservationStatus(x)).HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired(); b.Property(x => x.ReconciledUtc).HasColumnName("reconciled_utc");
        b.HasIndex(x => new { x.CompanyId, x.ReservationKey, x.PolicyId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        b.HasOne(x => x.Policy).WithMany().HasForeignKey(x => new { x.CompanyId, x.PolicyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Window).WithMany().HasForeignKey(x => new { x.CompanyId, x.WindowId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        // Preserve usage evidence and avoid SQL Server multiple-cascade paths through run -> step.
        b.HasOne(x => x.Run).WithMany().HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Step).WithMany().HasForeignKey(x => new { x.CompanyId, x.StepId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceAutonomyCircuitBreakerConfiguration : IEntityTypeConfiguration<FinanceAutonomyCircuitBreaker>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyCircuitBreaker> b)
    {
        b.ToTable("finance_autonomy_circuit_breakers"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        b.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160).IsRequired(); b.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(240).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyBudgetEnumValues.ParseCircuitStatus(x)).HasMaxLength(32).IsRequired();
        b.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").IsRequired(); b.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").IsRequired();
        b.Property(x => x.PolicyDenials).HasColumnName("policy_denials").IsRequired(); b.Property(x => x.InvalidPlans).HasColumnName("invalid_plans").IsRequired();
        b.Property(x => x.ProviderAmbiguities).HasColumnName("provider_ambiguities").IsRequired(); b.Property(x => x.Errors).HasColumnName("errors").IsRequired();
        b.Property(x => x.StaleEvidence).HasColumnName("stale_evidence").IsRequired(); b.Property(x => x.AuditOutboxFailures).HasColumnName("audit_outbox_failures").IsRequired();
        b.Property(x => x.OpenReasonCode).HasColumnName("open_reason_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.OpenedUtc).HasColumnName("opened_utc"); b.Property(x => x.CooldownUntilUtc).HasColumnName("cooldown_until_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.ScopeKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceAutonomyBudgetAlertConfiguration : IEntityTypeConfiguration<FinanceAutonomyBudgetAlert>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyBudgetAlert> b)
    {
        b.ToTable("finance_autonomy_budget_alerts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.CircuitId).HasColumnName("circuit_id").IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100).IsRequired(); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000).IsRequired();
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired(); b.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyBudgetEnumValues.ParseAlertStatus(x)).HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired(); b.Property(x => x.ResolvedUtc).HasColumnName("resolved_utc");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc }); b.HasIndex(x => new { x.CompanyId, x.CircuitId, x.Status });
        b.HasOne(x => x.Circuit).WithMany().HasForeignKey(x => new { x.CompanyId, x.CircuitId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
