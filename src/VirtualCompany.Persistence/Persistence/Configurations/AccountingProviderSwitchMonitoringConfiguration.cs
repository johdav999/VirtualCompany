using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingProviderSwitchMonitoringRunConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMonitoringRun>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMonitoringRun> b)
    {
        b.ToTable("accounting_provider_switch_monitoring_runs", table =>
        {
            table.HasCheckConstraint("CK_provider_switch_monitoring_window", "[window_days] BETWEEN 7 AND 30");
            table.HasCheckConstraint("CK_provider_switch_monitoring_status", "[status] IN ('active','attention_required','closure_awaiting_approval','closed','failed')");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SwitchId).HasColumnName("switch_id"); b.Property(x => x.ActivationExecutionId).HasColumnName("activation_execution_id");
        b.Property(x => x.WindowDays).HasColumnName("window_days"); b.Property(x => x.AssignedOwnerUserId).HasColumnName("assigned_owner_user_id");
        b.Property(x => x.AssignedOwnerAgentId).HasColumnName("assigned_owner_agent_id"); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.CheckSequence).HasColumnName("check_sequence");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.ConsecutiveFailureCount).HasColumnName("consecutive_failure_count");
        b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.WindowEndsUtc).HasColumnName("window_ends_at");
        b.Property(x => x.LastCheckStartedUtc).HasColumnName("last_check_started_at"); b.Property(x => x.LastSuccessfulCheckUtc).HasColumnName("last_successful_check_at");
        b.Property(x => x.NextRunUtc).HasColumnName("next_run_at"); b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.ClosureApprovalRequestId).HasColumnName("closure_approval_request_id");
        b.Property(x => x.ClosureEvidenceHash).HasColumnName("closure_evidence_hash").HasMaxLength(64); b.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id");
        b.Property(x => x.ClosureDecision).HasColumnName("closure_decision").HasMaxLength(64); b.Property(x => x.ClosureSummary).HasColumnName("closure_summary").HasMaxLength(2000);
        b.Property(x => x.CorrectiveSwitchId).HasColumnName("corrective_switch_id"); b.Property(x => x.ClosedUtc).HasColumnName("closed_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SwitchId }).IsUnique(); b.HasIndex(x => new { x.Status, x.NextRunUtc, x.LeaseExpiresUtc });
        b.HasOne(x => x.Switch).WithMany().HasForeignKey(x => new { x.CompanyId, x.SwitchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingProviderSwitchCutoverExecution>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SwitchId, x.ActivationExecutionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.SwitchId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ApprovalRequest>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ClosureApprovalRequestId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AccountingProviderSwitch>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CorrectiveSwitchId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction).IsRequired(false);
    }
}

public sealed class AccountingProviderSwitchMonitoringCheckConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMonitoringCheck>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMonitoringCheck> b)
    {
        b.ToTable("accounting_provider_switch_monitoring_checks"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.MonitoringRunId).HasColumnName("monitoring_run_id"); b.Property(x => x.CheckSequence).HasColumnName("check_sequence");
        b.Property(x => x.CheckKey).HasColumnName("check_key").HasMaxLength(80); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24); b.Property(x => x.IsBlocking).HasColumnName("is_blocking");
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasMaxLength(16000); b.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        b.Property(x => x.ObservedUtc).HasColumnName("observed_at"); b.HasIndex(x => new { x.CompanyId, x.MonitoringRunId, x.CheckSequence, x.CheckKey }).IsUnique();
        b.HasOne<AccountingProviderSwitchMonitoringRun>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MonitoringRunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingProviderSwitchMonitoringIncidentConfiguration : IEntityTypeConfiguration<AccountingProviderSwitchMonitoringIncident>
{
    public void Configure(EntityTypeBuilder<AccountingProviderSwitchMonitoringIncident> b)
    {
        b.ToTable("accounting_provider_switch_monitoring_incidents"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.SwitchId).HasColumnName("switch_id");
        b.Property(x => x.MonitoringRunId).HasColumnName("monitoring_run_id"); b.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(64);
        b.Property(x => x.CheckKey).HasColumnName("check_key").HasMaxLength(80); b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(24);
        b.Property(x => x.IsBlocking).HasColumnName("is_blocking"); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.TaskId).HasColumnName("task_id"); b.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count");
        b.Property(x => x.FirstObservedUtc).HasColumnName("first_observed_at"); b.Property(x => x.LastObservedUtc).HasColumnName("last_observed_at"); b.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        b.Property(x => x.AcceptedByUserId).HasColumnName("accepted_by_user_id"); b.Property(x => x.ExceptionExplanation).HasColumnName("exception_explanation").HasMaxLength(2000);
        b.Property(x => x.ExceptionScope).HasColumnName("exception_scope").HasMaxLength(500); b.Property(x => x.FinancialImpact).HasColumnName("financial_impact").HasPrecision(19, 4);
        b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(1000); b.Property(x => x.AcceptedUtc).HasColumnName("accepted_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.HasIndex(x => new { x.CompanyId, x.MonitoringRunId, x.Fingerprint }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.IsBlocking });
        b.HasOne<AccountingProviderSwitchMonitoringRun>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MonitoringRunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
    }
}
