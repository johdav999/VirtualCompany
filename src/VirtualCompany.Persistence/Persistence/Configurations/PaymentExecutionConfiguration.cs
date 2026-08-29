using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentBatchExecutionConfiguration : IEntityTypeConfiguration<PaymentBatchExecution>
{
    public void Configure(EntityTypeBuilder<PaymentBatchExecution> b)
    {
        b.ToTable("payment_batch_executions", t => t.HasCheckConstraint("CK_payment_batch_executions_status", PaymentExecutionStatuses.BuildCheckConstraintSql("status")));
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.BatchId).HasColumnName("batch_id"); b.Property(x => x.InstructionSetVersion).HasColumnName("instruction_set_version");
        b.Property(x => x.ApprovalBindingId).HasColumnName("approval_binding_id"); b.Property(x => x.BankConnectionId).HasColumnName("bank_connection_id");
        b.Property(x => x.CompanyBankAccountId).HasColumnName("company_bank_account_id"); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired(); b.Property(x => x.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(256);
        b.Property(x => x.ProviderAuthorizationUri).HasColumnName("provider_authorization_uri").HasMaxLength(1000); b.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(40);
        b.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.BusinessIdempotencyKey).HasColumnName("business_idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.UpdatesExpected).HasColumnName("updates_expected"); b.Property(x => x.CanCancelAtProvider).HasColumnName("can_cancel_at_provider"); b.Property(x => x.StatusPollCount).HasColumnName("status_poll_count");
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id"); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.RowVersion).HasColumnName("row_version").HasMaxLength(16).IsFixedLength().IsConcurrencyToken().ValueGeneratedNever();
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.SubmittedUtc).HasColumnName("submitted_at");
        b.Property(x => x.ProviderAcceptedUtc).HasColumnName("provider_accepted_at"); b.Property(x => x.ProviderCompletedUtc).HasColumnName("provider_completed_at"); b.Property(x => x.SettledUtc).HasColumnName("settled_at"); b.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        b.HasIndex(x => new { x.CompanyId, x.BatchId, x.InstructionSetVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.BusinessIdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.ProviderKey, x.ProviderPaymentId }).IsUnique().HasFilter("[provider_payment_id] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<PaymentBatch>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BatchId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentBatchApprovalBinding>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalBindingId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankConnection>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyBankAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CompanyBankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentExecutionAttemptConfiguration : IEntityTypeConfiguration<PaymentExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentExecutionAttempt> b)
    {
        b.ToTable("payment_execution_attempts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id");
        b.Property(x => x.AttemptNumber).HasColumnName("attempt_number"); b.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(32).IsRequired(); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        b.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(256);
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.RetryClassification).HasColumnName("retry_classification").HasMaxLength(32).IsRequired();
        b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.Operation, x.AttemptNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.Outcome });
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentProviderAcknowledgementConfiguration : IEntityTypeConfiguration<PaymentProviderAcknowledgement>
{
    public void Configure(EntityTypeBuilder<PaymentProviderAcknowledgement> b)
    {
        b.ToTable("payment_provider_acknowledgements"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id");
        b.Property(x => x.EventIdentity).HasColumnName("event_identity").HasMaxLength(256).IsRequired(); b.Property(x => x.Source).HasColumnName("source").HasMaxLength(32).IsRequired();
        b.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(40).IsRequired(); b.Property(x => x.NormalizedStatus).HasColumnName("normalized_status").HasMaxLength(40).IsRequired();
        b.Property(x => x.IsFinal).HasColumnName("is_final"); b.Property(x => x.UpdatesExpected).HasColumnName("updates_expected"); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000);
        b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.AcknowledgedUtc).HasColumnName("acknowledged_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.EventIdentity }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.AcknowledgedUtc });
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentExecutionInstructionConfiguration : IEntityTypeConfiguration<PaymentExecutionInstruction>
{
    public void Configure(EntityTypeBuilder<PaymentExecutionInstruction> b)
    {
        b.ToTable("payment_execution_instructions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id");
        b.Property(x => x.PaymentInstructionId).HasColumnName("payment_instruction_id"); b.Property(x => x.ObligationLinkId).HasColumnName("obligation_link_id"); b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired(); b.Property(x => x.BeneficiaryName).HasColumnName("beneficiary_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.MaskedDestination).HasColumnName("masked_destination").HasMaxLength(100).IsRequired(); b.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(256); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.PaymentId).HasColumnName("payment_id"); b.Property(x => x.PaymentAllocationId).HasColumnName("payment_allocation_id");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.PaymentInstructionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ProviderTransactionId });
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PaymentInstruction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PaymentInstructionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PaymentBatchObligationLink>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ObligationLinkId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PaymentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PaymentAllocationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class PaymentProviderWebhookReceiptConfiguration : IEntityTypeConfiguration<PaymentProviderWebhookReceipt>
{
    public void Configure(EntityTypeBuilder<PaymentProviderWebhookReceipt> b)
    {
        b.ToTable("payment_provider_webhook_receipts"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id"); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.WebhookId).HasColumnName("webhook_id").HasMaxLength(256).IsRequired(); b.Property(x => x.ProviderPaymentId).HasColumnName("provider_payment_id").HasMaxLength(256).IsRequired(); b.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(40).IsRequired();
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.TriggeredUtc).HasColumnName("triggered_at"); b.Property(x => x.ReceivedUtc).HasColumnName("received_at");
        b.HasIndex(x => new { x.ProviderKey, x.WebhookId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.ReceivedUtc });
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PaymentBatchSettlementConfiguration : IEntityTypeConfiguration<PaymentBatchSettlement>
{
    public void Configure(EntityTypeBuilder<PaymentBatchSettlement> b)
    {
        b.ToTable("payment_batch_settlements"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id"); b.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id");
        b.Property(x => x.BankReference).HasColumnName("bank_reference").HasMaxLength(240).IsRequired(); b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.PaymentCount).HasColumnName("payment_count"); b.Property(x => x.AllocationCount).HasColumnName("allocation_count"); b.Property(x => x.LedgerEntryIdsJson).HasColumnName("ledger_entry_ids_json").HasMaxLength(8000).IsRequired(); b.Property(x => x.SettledByUserId).HasColumnName("settled_by_user_id"); b.Property(x => x.SettledUtc).HasColumnName("settled_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.BankTransactionId }).IsUnique();
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BankTransaction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.BankTransactionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentRemittanceConfiguration : IEntityTypeConfiguration<PaymentRemittance>
{
    public void Configure(EntityTypeBuilder<PaymentRemittance> b)
    {
        b.ToTable("payment_remittances"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ExecutionId).HasColumnName("execution_id"); b.Property(x => x.PaymentInstructionId).HasColumnName("payment_instruction_id");
        b.Property(x => x.BeneficiaryName).HasColumnName("beneficiary_name").HasMaxLength(200).IsRequired(); b.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320); b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(300).IsRequired();
        b.Property(x => x.Content).HasColumnName("content").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(500); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(1000); b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.AcceptedUtc).HasColumnName("accepted_at");
        b.HasIndex(x => new { x.CompanyId, x.ExecutionId, x.PaymentInstructionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<PaymentBatchExecution>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ExecutionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<PaymentInstruction>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PaymentInstructionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
