using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentBeneficiaryProfileConfiguration : IEntityTypeConfiguration<PaymentBeneficiaryProfile>
{
    public void Configure(EntityTypeBuilder<PaymentBeneficiaryProfile> b)
    {
        b.ToTable("payment_beneficiary_profiles", t => t.HasCheckConstraint("CK_payment_beneficiary_profiles_status", PaymentBeneficiaryVerificationStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.PartyType).HasColumnName("party_type").HasMaxLength(40).IsRequired(); b.Property(x => x.PartyId).HasColumnName("party_id");
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.Rail).HasColumnName("rail").HasMaxLength(40).IsRequired(); b.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(200).IsRequired();
        b.Property(x => x.MaskedDestination).HasColumnName("masked_destination").HasMaxLength(100).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.Version).HasColumnName("version"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired(); b.Property(x => x.IsCurrent).HasColumnName("is_current");
        b.Property(x => x.VerificationEvidenceReference).HasColumnName("verification_evidence_reference").HasMaxLength(500).IsRequired();
        b.Property(x => x.VerificationEvidenceHash).HasColumnName("verification_evidence_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.VerifiedByUserId).HasColumnName("verified_by_user_id"); b.Property(x => x.VerifiedUtc).HasColumnName("verified_at");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.SupersededUtc).HasColumnName("superseded_at"); b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.PartyType, x.PartyId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.PartyType, x.PartyId, x.IsCurrent }).IsUnique().HasFilter("[is_current] = 1");
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchConfiguration : IEntityTypeConfiguration<PaymentBatch>
{
    public void Configure(EntityTypeBuilder<PaymentBatch> b)
    {
        b.ToTable("payment_batches", t => t.HasCheckConstraint("CK_payment_batches_status", PaymentBatchStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired(); b.Property(x => x.PlannedExecutionDate).HasColumnName("planned_execution_date").HasColumnType("date");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version");
        b.Property(x => x.CurrentValidationResultId).HasColumnName("current_validation_result_id"); b.Property(x => x.CurrentExportArtifactId).HasColumnName("current_export_artifact_id"); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); b.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        b.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id"); b.Property(x => x.RejectedByUserId).HasColumnName("rejected_by_user_id"); b.Property(x => x.CancelledByUserId).HasColumnName("cancelled_by_user_id");
        b.Property(x => x.DecisionComment).HasColumnName("decision_comment").HasMaxLength(2000); b.Property(x => x.CreateIdempotencyKey).HasColumnName("create_idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.CreatePayloadHash).HasColumnName("create_payload_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.SubmittedUtc).HasColumnName("submitted_at"); b.Property(x => x.ApprovedUtc).HasColumnName("approved_at"); b.Property(x => x.RejectedUtc).HasColumnName("rejected_at"); b.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CreateIdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchObligationLinkConfiguration : IEntityTypeConfiguration<PaymentBatchObligationLink>
{
    public void Configure(EntityTypeBuilder<PaymentBatchObligationLink> b)
    {
        b.ToTable("payment_batch_obligations", t => t.HasCheckConstraint("CK_payment_batch_obligations_type", PaymentBatchObligationTypes.BuildCheckConstraintSql("obligation_type")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.Ignore(x => x.IsActive);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.ObligationType).HasColumnName("obligation_type").HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceId).HasColumnName("source_id"); b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200).IsRequired(); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128).IsRequired(); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired(); b.Property(x => x.DueDate).HasColumnName("due_date").HasColumnType("date"); b.Property(x => x.PaymentReference).HasColumnName("payment_reference").HasMaxLength(200).IsRequired();
        b.Property(x => x.AddedByUserId).HasColumnName("added_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.RemovedByUserId).HasColumnName("removed_by_user_id"); b.Property(x => x.RemovedUtc).HasColumnName("removed_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.ObligationType, x.SourceId }); b.HasIndex(x => new { x.CompanyId, x.ObligationType, x.SourceId, x.RemovedUtc });
        b.HasOne(x => x.Batch).WithMany(x => x.Obligations).HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBeneficiarySnapshotConfiguration : IEntityTypeConfiguration<PaymentBeneficiarySnapshot>
{
    public void Configure(EntityTypeBuilder<PaymentBeneficiarySnapshot> b)
    {
        b.ToTable("payment_beneficiary_snapshots"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ObligationLinkId).HasColumnName("obligation_link_id"); b.Property(x => x.ProfileId).HasColumnName("profile_id"); b.Property(x => x.ProfileVersion).HasColumnName("profile_version");
        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired(); b.Property(x => x.Rail).HasColumnName("rail").HasMaxLength(40).IsRequired(); b.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(200).IsRequired(); b.Property(x => x.MaskedDestination).HasColumnName("masked_destination").HasMaxLength(100).IsRequired();
        b.Property(x => x.VerificationEvidenceReference).HasColumnName("verification_evidence_reference").HasMaxLength(500).IsRequired(); b.Property(x => x.VerificationEvidenceHash).HasColumnName("verification_evidence_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.VerifiedUtc).HasColumnName("verified_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ObligationLinkId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ProfileId, x.ProfileVersion });
        b.HasOne(x => x.ObligationLink).WithOne(x => x.BeneficiarySnapshot).HasForeignKey<PaymentBeneficiarySnapshot>(x => new { x.CompanyId, x.ObligationLinkId }).HasPrincipalKey<PaymentBatchObligationLink>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentInstructionConfiguration : IEntityTypeConfiguration<PaymentInstruction>
{
    public void Configure(EntityTypeBuilder<PaymentInstruction> b)
    {
        b.ToTable("payment_instructions", t => t.HasCheckConstraint("CK_payment_instructions_status", PaymentInstructionStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.ObligationLinkId).HasColumnName("obligation_link_id"); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version"); b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.ExecutionDate).HasColumnName("execution_date").HasColumnType("date"); b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired(); b.Property(x => x.PaymentReference).HasColumnName("payment_reference").HasMaxLength(200).IsRequired();
        b.Property(x => x.BeneficiaryName).HasColumnName("beneficiary_name").HasMaxLength(200).IsRequired(); b.Property(x => x.Rail).HasColumnName("rail").HasMaxLength(40).IsRequired(); b.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(200).IsRequired(); b.Property(x => x.MaskedDestination).HasColumnName("masked_destination").HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128).IsRequired(); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired(); b.Property(x => x.IsCurrent).HasColumnName("is_current"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.ApprovedUtc).HasColumnName("approved_at"); b.Property(x => x.SupersededUtc).HasColumnName("superseded_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.InstructionSetVersion, x.Sequence }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.ObligationLinkId, x.IsCurrent }).IsUnique().HasFilter("[is_current] = 1");
        b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PaymentBatchObligationLink>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ObligationLinkId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentBatchValidationResultConfiguration : IEntityTypeConfiguration<PaymentBatchValidationResult>
{
    public void Configure(EntityTypeBuilder<PaymentBatchValidationResult> b)
    {
        b.ToTable("payment_batch_validation_results"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.EvaluatedBatchVersion).HasColumnName("evaluated_batch_version"); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version");
        b.Property(x => x.IsValid).HasColumnName("is_valid"); b.Property(x => x.SourceSetHash).HasColumnName("source_set_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.TotalsJson).HasColumnName("totals_json").HasMaxLength(8000).IsRequired(); b.Property(x => x.CashAvailabilityJson).HasColumnName("cash_availability_json").HasMaxLength(8000).IsRequired(); b.Property(x => x.ValidatedByUserId).HasColumnName("validated_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.CreatedUtc }); b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchValidationIssueConfiguration : IEntityTypeConfiguration<PaymentBatchValidationIssue>
{
    public void Configure(EntityTypeBuilder<PaymentBatchValidationIssue> b)
    {
        b.ToTable("payment_batch_validation_issues"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ValidationResultId).HasColumnName("validation_result_id"); b.Property(x => x.ObligationLinkId).HasColumnName("obligation_link_id");
        b.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(20).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100).IsRequired(); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000).IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ValidationResultId, x.ReasonCode }); b.HasOne(x => x.ValidationResult).WithMany(x => x.Issues).HasForeignKey(x => new { x.CompanyId, x.ValidationResultId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchExportArtifactConfiguration : IEntityTypeConfiguration<PaymentBatchExportArtifact>
{
    public void Configure(EntityTypeBuilder<PaymentBatchExportArtifact> b)
    {
        b.ToTable("payment_batch_export_artifacts"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version");
        b.Property(x => x.Format).HasColumnName("format").HasMaxLength(100).IsRequired(); b.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired(); b.Property(x => x.Content).HasColumnName("content").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.IsCurrent).HasColumnName("is_current"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.SupersededUtc).HasColumnName("superseded_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.InstructionSetVersion }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BatchId, x.IsCurrent }).IsUnique().HasFilter("[is_current] = 1");
        b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchApprovalBindingConfiguration : IEntityTypeConfiguration<PaymentBatchApprovalBinding>
{
    public void Configure(EntityTypeBuilder<PaymentBatchApprovalBinding> b)
    {
        b.ToTable("payment_batch_approval_bindings", t => t.HasCheckConstraint("CK_payment_batch_approval_bindings_status", PaymentBatchApprovalBindingStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version"); b.Property(x => x.SourceSetHash).HasColumnName("source_set_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired(); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id"); b.Property(x => x.DecisionComment).HasColumnName("decision_comment").HasMaxLength(2000); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.ApprovalRequestId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BatchId, x.Status });
        b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentBatchOperationConfiguration : IEntityTypeConfiguration<PaymentBatchOperation>
{
    public void Configure(EntityTypeBuilder<PaymentBatchOperation> b)
    {
        b.ToTable("payment_batch_operations"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(40).IsRequired(); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired(); b.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.ResultBatchVersion).HasColumnName("result_batch_version"); b.Property(x => x.ResultStatus).HasColumnName("result_status").HasMaxLength(32).IsRequired(); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.OperationType, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BatchId, x.CreatedUtc });
        b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
