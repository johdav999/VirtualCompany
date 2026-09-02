using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAutonomyRunConfiguration : IEntityTypeConfiguration<FinanceAutonomyRun>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyRun> b)
    {
        b.ToTable("finance_autonomy_runs");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        b.Property(x => x.CapabilityId).HasColumnName("capability_id").HasMaxLength(160).IsRequired();
        b.Property(x => x.GrantId).HasColumnName("grant_id").IsRequired();
        b.Property(x => x.GrantVersionId).HasColumnName("grant_version_id").IsRequired();
        b.Property(x => x.GrantVersionNumber).HasColumnName("grant_version_number").IsRequired();
        b.Property(x => x.Trigger).HasColumnName("trigger").HasMaxLength(64).IsRequired();
        b.Property(x => x.TriggerKey).HasColumnName("trigger_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.WindowStartUtc).HasColumnName("window_start_utc").IsRequired();
        b.Property(x => x.WindowEndUtc).HasColumnName("window_end_utc").IsRequired();
        b.Property(x => x.AuthoritativeEventId).HasColumnName("authoritative_event_id").HasMaxLength(240);
        b.Property(x => x.AuthoritativeEventVersion).HasColumnName("authoritative_event_version").HasMaxLength(100);
        b.Property(x => x.LogicalKey).HasColumnName("logical_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.EvidenceSnapshotJson).HasColumnName("evidence_snapshot_json").IsRequired();
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.EvidenceObservedUtc).HasColumnName("evidence_observed_utc").IsRequired();
        b.Property(x => x.PlanJson).HasColumnName("plan_json").IsRequired();
        b.Property(x => x.PlanHash).HasColumnName("plan_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.PlanVersion).HasColumnName("plan_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.BudgetSnapshotJson).HasColumnName("budget_snapshot_json").IsRequired();
        b.Property(x => x.BudgetHash).HasColumnName("budget_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.PolicyVersion).HasColumnName("policy_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.CatalogueVersion).HasColumnName("catalogue_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityVersion).HasColumnName("authority_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityHash).HasColumnName("authority_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.OriginatingGoalId).HasColumnName("originating_goal_id");
        b.Property(x => x.OriginatingTaskId).HasColumnName("originating_task_id");
        b.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        b.Property(x => x.OrchestrationRunId).HasColumnName("orchestration_run_id");
        b.Property(x => x.ReplayOfRunId).HasColumnName("replay_of_run_id");
        b.Property(x => x.ReplayCheckpointStepId).HasColumnName("replay_checkpoint_step_id");
        b.Property(x => x.RevisionOfRunId).HasColumnName("revision_of_run_id");
        b.Property(x => x.RevisionNumber).HasColumnName("revision_number").HasDefaultValue(1).IsRequired();
        b.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyRunEnumValues.ParseRunStatus(x))
            .HasMaxLength(40).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.HasCompletedEffects).HasColumnName("has_completed_effects").IsRequired();
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        b.Property(x => x.StartedUtc).HasColumnName("started_utc");
        b.Property(x => x.TerminalUtc).HasColumnName("terminal_utc");
        b.Property(x => x.SensitiveContentRedactedUtc).HasColumnName("sensitive_content_redacted_utc");
        b.Property(x => x.SensitiveContentRedactedByUserId).HasColumnName("sensitive_content_redacted_by_user_id");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength()
            .IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.LogicalKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.AgentId, x.Status, x.CreatedUtc });
        b.HasIndex(x => new { x.CompanyId, x.GrantId, x.GrantVersionId });
        b.HasIndex(x => new { x.CompanyId, x.WindowStartUtc, x.WindowEndUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Agent).WithMany().HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Grant).WithMany().HasForeignKey(x => new { x.CompanyId, x.GrantId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GrantVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.GrantVersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyGoal>().WithMany().HasForeignKey(x => x.OriginatingGoalId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.OriginatingTaskId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WorkflowInstance>().WithMany().HasForeignKey(x => x.WorkflowInstanceId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<AgentOrchestrationRun>().WithMany().HasForeignKey(x => x.OrchestrationRunId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceAutonomyRun>().WithMany().HasForeignKey(x => x.ReplayOfRunId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceAutonomyRun>().WithMany().HasForeignKey(x => x.RevisionOfRunId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceAutonomyRunStepConfiguration : IEntityTypeConfiguration<FinanceAutonomyRunStep>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyRunStep> b)
    {
        b.ToTable("finance_autonomy_run_steps");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        b.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(160).IsRequired();
        b.Property(x => x.ActionClass).HasColumnName("action_class").HasMaxLength(64).IsRequired();
        b.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(160).IsRequired();
        b.Property(x => x.DependencyStepKeys).HasColumnName("dependency_step_keys_json")
            .HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        b.Property(x => x.Status).HasColumnName("status")
            .HasConversion(x => x.ToStorageValue(), x => FinanceAutonomyRunEnumValues.ParseStepStatus(x))
            .HasMaxLength(40).IsRequired();
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        b.Property(x => x.MaximumAttempts).HasColumnName("maximum_attempts").IsRequired();
        b.Property(x => x.ToolPolicyVersion).HasColumnName("tool_policy_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityVersion).HasColumnName("authority_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityHash).HasColumnName("authority_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestedEffectHash).HasColumnName("requested_effect_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.RequestedEffectSummary).HasColumnName("requested_effect_summary").HasMaxLength(1000);
        b.Property(x => x.ActualEffectHash).HasColumnName("actual_effect_hash").HasMaxLength(64);
        b.Property(x => x.ActualEffectStatus).HasColumnName("actual_effect_status").HasMaxLength(40);
        b.Property(x => x.ActualEffectSummary).HasColumnName("actual_effect_summary").HasMaxLength(1000);
        b.Property(x => x.BusinessIdempotencyKey).HasColumnName("business_idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.ReconciliationReference).HasColumnName("reconciliation_reference").HasMaxLength(240);
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.WorkTaskId).HasColumnName("work_task_id");
        b.Property(x => x.ToolExecutionAttemptId).HasColumnName("tool_execution_attempt_id");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(160);
        b.Property(x => x.LeaseToken).HasColumnName("lease_token").HasMaxLength(160);
        b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
        b.Property(x => x.LastHeartbeatUtc).HasColumnName("last_heartbeat_utc");
        b.Property(x => x.ReplayPermitted).HasColumnName("replay_permitted").IsRequired();
        b.Property(x => x.ReplayOfStepId).HasColumnName("replay_of_step_id");
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        b.Property(x => x.StartedUtc).HasColumnName("started_utc");
        b.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength()
            .IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.StepKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.LeaseExpiresUtc, x.Sequence });
        b.HasOne(x => x.Run).WithMany(x => x.Steps).HasForeignKey(x => new { x.CompanyId, x.RunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => x.ApprovalRequestId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<WorkTask>().WithMany().HasForeignKey(x => x.WorkTaskId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ToolExecutionAttempt>().WithMany().HasForeignKey(x => x.ToolExecutionAttemptId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceAutonomyRunStep>().WithMany().HasForeignKey(x => x.ReplayOfStepId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceAutonomyStepAttemptConfiguration : IEntityTypeConfiguration<FinanceAutonomyStepAttempt>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyStepAttempt> b)
    {
        b.ToTable("finance_autonomy_step_attempts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.RunId).HasColumnName("run_id").IsRequired(); b.Property(x => x.StepId).HasColumnName("step_id").IsRequired();
        b.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(160).IsRequired();
        b.Property(x => x.LeaseTokenHash).HasColumnName("lease_token_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.PolicyVersion).HasColumnName("policy_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityVersion).HasColumnName("authority_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.AuthorityHash).HasColumnName("authority_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(40).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.ToolExecutionAttemptId).HasColumnName("tool_execution_attempt_id");
        b.Property(x => x.StartedUtc).HasColumnName("started_utc").IsRequired(); b.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        b.HasIndex(x => new { x.CompanyId, x.StepId, x.AttemptNumber }).IsUnique();
        b.HasOne(x => x.Step).WithMany(x => x.Attempts).HasForeignKey(x => new { x.CompanyId, x.StepId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ToolExecutionAttempt>().WithMany().HasForeignKey(x => x.ToolExecutionAttemptId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceAutonomyRunHistoryConfiguration : IEntityTypeConfiguration<FinanceAutonomyRunHistory>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyRunHistory> b)
    {
        b.ToTable("finance_autonomy_run_history"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(40); b.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(40).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100).IsRequired(); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.ActorType).HasColumnName("actor_type").HasMaxLength(32).IsRequired(); b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired(); b.Property(x => x.OccurredUtc).HasColumnName("occurred_utc").IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.OccurredUtc });
        b.HasOne(x => x.Run).WithMany(x => x.History).HasForeignKey(x => new { x.CompanyId, x.RunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceAutonomyRunSourceReferenceConfiguration : IEntityTypeConfiguration<FinanceAutonomyRunSourceReference>
{
    public void Configure(EntityTypeBuilder<FinanceAutonomyRunSourceReference> b)
    {
        b.ToTable("finance_autonomy_run_sources"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired(); b.Property(x => x.RunId).HasColumnName("run_id").IsRequired();
        b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired(); b.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(240).IsRequired(); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(100).IsRequired();
        b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.SafeLabel).HasColumnName("safe_label").HasMaxLength(300);
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.RunId, x.SourceType, x.EntityType, x.EntityId, x.SourceVersion }).IsUnique();
        b.HasOne(x => x.Run).WithMany(x => x.Sources).HasForeignKey(x => new { x.CompanyId, x.RunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
