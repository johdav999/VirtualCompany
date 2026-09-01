using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceConversationRunConfiguration : IEntityTypeConfiguration<FinanceConversationRun>
{
    public void Configure(EntityTypeBuilder<FinanceConversationRun> b)
    {
        b.ToTable("finance_conversation_runs"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        b.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired(); b.Property(x => x.InitiatingUserId).HasColumnName("initiating_user_id").IsRequired();
        b.Property(x => x.TaskId).HasColumnName("task_id"); b.Property(x => x.ConversationId).HasColumnName("conversation_id");
        b.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id"); b.Property(x => x.DelegationAuthorityId).HasColumnName("delegation_authority_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(128).IsRequired();
        b.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.EffectiveAuthorityVersion).HasColumnName("effective_authority_version").HasMaxLength(128).IsRequired();
        b.Property(x => x.EffectiveAuthorityHash).HasColumnName("effective_authority_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.PlanningContextVersion).HasColumnName("planning_context_version").HasMaxLength(64).IsRequired();
        b.Property(x => x.PlanningContextHash).HasColumnName("planning_context_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(2000).IsRequired(); b.Property(x => x.FinalOutcomeCode).HasColumnName("final_outcome_code").HasMaxLength(100);
        b.Property(x => x.SupersededByRunId).HasColumnName("superseded_by_run_id"); b.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        b.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(1000); b.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at"); b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.Property(x => x.RetainUntilUtc).HasColumnName("retain_until_at"); b.Property(x => x.RedactedUtc).HasColumnName("redacted_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.AgentId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc }); b.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        b.HasIndex(x => new { x.RetainUntilUtc, x.RedactedUtc }); b.HasIndex(x => x.CorrelationId);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceConversationRunStepConfiguration : IEntityTypeConfiguration<FinanceConversationRunStep>
{
    public void Configure(EntityTypeBuilder<FinanceConversationRunStep> b)
    {
        b.ToTable("finance_conversation_run_steps"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.RunId).HasColumnName("run_id");
        b.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(64); b.Property(x => x.Sequence).HasColumnName("sequence_no");
        b.Property(x => x.DependenciesJson).HasColumnName("dependencies_json").HasMaxLength(4000); b.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(100);
        b.Property(x => x.ToolVersion).HasColumnName("tool_version").HasMaxLength(32); b.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(16); b.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(100);
        b.Property(x => x.NormalizedArgumentsJson).HasColumnName("normalized_arguments_json").HasMaxLength(16000); b.Property(x => x.NormalizedArgumentsHash).HasColumnName("normalized_arguments_hash").HasMaxLength(64);
        b.Property(x => x.ExpectedEffect).HasColumnName("expected_effect").HasMaxLength(1000); b.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasMaxLength(16000);
        b.Property(x => x.ResultSummaryJson).HasColumnName("result_summary_json").HasMaxLength(16000); b.Property(x => x.PolicyDecisionSummaryJson).HasColumnName("policy_decision_summary_json").HasMaxLength(16000);
        b.Property(x => x.BusinessIdempotencyKey).HasColumnName("business_idempotency_key").HasMaxLength(200); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.ToolExecutionAttemptId).HasColumnName("tool_execution_attempt_id"); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.ConfirmedByUserId).HasColumnName("confirmed_by_user_id"); b.Property(x => x.ConfirmationPayloadHash).HasColumnName("confirmation_payload_hash").HasMaxLength(64);
        b.Property(x => x.ConfirmationTargetSnapshotHash).HasColumnName("confirmation_target_snapshot_hash").HasMaxLength(64); b.Property(x => x.ConfirmationAuthorityHash).HasColumnName("confirmation_authority_hash").HasMaxLength(64);
        b.Property(x => x.ConfirmedUtc).HasColumnName("confirmed_at"); b.Property(x => x.ConfirmationExpiresUtc).HasColumnName("confirmation_expires_at");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.MaxAttempts).HasColumnName("max_attempts"); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.SafeFailureSummary).HasColumnName("safe_failure_summary").HasMaxLength(2000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at"); b.Property(x => x.RedactedUtc).HasColumnName("redacted_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.RunId, x.StepKey }).IsUnique(); b.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc }); b.HasIndex(x => x.ToolExecutionAttemptId); b.HasIndex(x => x.ApprovalRequestId);
        b.HasIndex(x => new { x.CompanyId, x.BusinessIdempotencyKey }).IsUnique();
        b.HasOne(x => x.Run).WithMany(x => x.Steps).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceConversationRunRevisionConfiguration : IEntityTypeConfiguration<FinanceConversationRunRevision>
{
    public void Configure(EntityTypeBuilder<FinanceConversationRunRevision> b)
    {
        b.ToTable("finance_conversation_run_revisions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.RunId).HasColumnName("run_id"); b.Property(x => x.Revision).HasColumnName("revision_no"); b.Property(x => x.PlanId).HasColumnName("plan_id");
        b.Property(x => x.PlanState).HasColumnName("plan_state").HasMaxLength(32); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.PlanningContextHash).HasColumnName("planning_context_hash").HasMaxLength(64);
        b.Property(x => x.EvidenceReferencesJson).HasColumnName("evidence_references_json").HasMaxLength(16000); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.RunId, x.Revision }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        b.HasOne(x => x.Run).WithMany(x => x.Revisions).HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceConversationRunAttemptConfiguration : IEntityTypeConfiguration<FinanceConversationRunAttempt>
{
    public void Configure(EntityTypeBuilder<FinanceConversationRunAttempt> b)
    {
        b.ToTable("finance_conversation_run_attempts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.RunStepId).HasColumnName("run_step_id"); b.Property(x => x.AttemptNumber).HasColumnName("attempt_no");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32);
        b.Property(x => x.ToolExecutionAttemptId).HasColumnName("tool_execution_attempt_id"); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(2000);
        b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasIndex(x => new { x.RunStepId, x.AttemptNumber }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.StartedUtc });
        b.HasOne(x => x.Step).WithMany(x => x.Attempts).HasForeignKey(x => x.RunStepId).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}
