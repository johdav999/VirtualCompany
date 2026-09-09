using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class SalesMeetingChangeProposalConfiguration : IEntityTypeConfiguration<SalesMeetingChangeProposal>
{
    public void Configure(EntityTypeBuilder<SalesMeetingChangeProposal> b)
    {
        b.ToTable("sales_meeting_change_proposals"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever(); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SessionId).HasColumnName("session_id"); b.Property(x => x.EvidenceArtifactId).HasColumnName("evidence_artifact_id");
        b.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseTarget(x));
        b.Property(x => x.TargetId).HasColumnName("target_id"); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(40).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseAction(x));
        b.Property(x => x.Field).HasColumnName("field").HasMaxLength(48).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseField(x));
        b.Property(x => x.ValueKind).HasColumnName("value_kind").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseValueKind(x));
        b.Property(x => x.ProposedValueJson).HasColumnName("proposed_value_json").HasMaxLength(8000); b.Property(x => x.BeforeValueJson).HasColumnName("before_value_json").HasMaxLength(8000);
        b.Property(x => x.TargetVersion).HasColumnName("target_version").HasMaxLength(128); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4);
        b.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(2000); b.Property(x => x.SourceIdsJson).HasColumnName("source_ids_json").HasMaxLength(8000);
        b.Property(x => x.EvidenceVersionHash).HasColumnName("evidence_version_hash").HasMaxLength(128); b.Property(x => x.RiskClass).HasColumnName("risk_class").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseRisk(x));
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).HasConversion(x => x.ToStorageValue(), x => SalesMeetingChangeProposalEnumValues.ParseStatus(x));
        b.Property(x => x.RequiresApproval).HasColumnName("requires_approval"); b.Property(x => x.PolicyVersion).HasColumnName("policy_version").HasMaxLength(64);
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.ApprovalBindingHash).HasColumnName("approval_binding_hash").HasMaxLength(128);
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id"); b.Property(x => x.ReviewedUtc).HasColumnName("reviewed_at"); b.Property(x => x.ApprovedUtc).HasColumnName("approved_at"); b.Property(x => x.RejectedUtc).HasColumnName("rejected_at");
        b.Property(x => x.ExecutionAttemptCount).HasColumnName("execution_attempt_count"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(300);
        b.Property(x => x.ExecutedBeforeValueJson).HasColumnName("executed_before_value_json").HasMaxLength(8000); b.Property(x => x.ExecutedAfterValueJson).HasColumnName("executed_after_value_json").HasMaxLength(8000);
        b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(1000); b.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(120); b.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        b.Property(x => x.ExecutedUtc).HasColumnName("executed_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SessionId, x.Status }); b.HasIndex(x => new { x.CompanyId, x.TargetType, x.TargetId }); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasOne(x => x.Session).WithMany().HasForeignKey(x => new { x.CompanyId, x.SessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.EvidenceArtifact).WithMany().HasForeignKey(x => new { x.CompanyId, x.EvidenceArtifactId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.ToTable(t => { t.HasCheckConstraint("CK_sales_meeting_change_proposals_confidence", "confidence >= 0 AND confidence <= 1"); t.HasCheckConstraint("CK_sales_meeting_change_proposals_attempts", "execution_attempt_count >= 0"); });
    }
}
