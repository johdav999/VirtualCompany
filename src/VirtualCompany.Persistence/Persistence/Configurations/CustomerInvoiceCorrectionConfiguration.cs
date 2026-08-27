using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CustomerInvoiceCorrectionConfiguration : IEntityTypeConfiguration<CustomerInvoiceCorrection>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceCorrection> b)
    {
        b.ToTable("customer_invoice_corrections");
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.CorrectionType).HasColumnName("correction_type").HasMaxLength(40);
        b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128);
        b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64); b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(500);
        b.Property(x => x.BeneficiaryReference).HasColumnName("beneficiary_reference").HasMaxLength(300); b.Property(x => x.PaymentEvidenceReference).HasColumnName("payment_evidence_reference").HasMaxLength(500);
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.TaskId).HasColumnName("task_id"); b.Property(x => x.CreditDraftId).HasColumnName("credit_draft_id");
        b.Property(x => x.CorrectingInvoiceId).HasColumnName("correcting_invoice_id"); b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        b.Property(x => x.OriginalVatReturnId).HasColumnName("original_vat_return_id"); b.Property(x => x.CorrectionVatReturnId).HasColumnName("correction_vat_return_id");
        b.Property(x => x.ExpenseAccountId).HasColumnName("expense_account_id"); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        b.Property(x => x.ExecutedByUserId).HasColumnName("executed_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.ExecutedUtc).HasColumnName("executed_utc");
        b.Property(x => x.FailureReasonCode).HasColumnName("failure_reason_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        b.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique().HasFilter("approval_request_id IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.CreditDraftId }).IsUnique().HasFilter("credit_draft_id IS NOT NULL");
        b.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Task).WithMany().HasForeignKey(x => new { x.CompanyId, x.TaskId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreditDraft).WithMany().HasForeignKey(x => new { x.CompanyId, x.CreditDraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CorrectingInvoice).WithMany().HasForeignKey(x => new { x.CompanyId, x.CorrectingInvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LedgerEntry).WithMany().HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerInvoiceRefundExecutionConfiguration : IEntityTypeConfiguration<CustomerInvoiceRefundExecution>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceRefundExecution> b)
    {
        b.ToTable("customer_invoice_refund_executions");
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CorrectionId).HasColumnName("correction_id");
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x => x.BeneficiaryReference).HasColumnName("beneficiary_reference").HasMaxLength(300); b.Property(x => x.PaymentEvidenceReference).HasColumnName("payment_evidence_reference").HasMaxLength(500);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        b.Property(x => x.AvailableUtc).HasColumnName("available_utc"); b.Property(x => x.ClaimedUtc).HasColumnName("claimed_utc"); b.Property(x => x.ClaimToken).HasColumnName("claim_token").HasMaxLength(64);
        b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(300); b.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        b.Property(x => x.SafeFailureSummary).HasColumnName("safe_failure_summary").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.CompletedUtc).HasColumnName("completed_utc"); b.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        b.HasIndex(x => new { x.CompanyId, x.CorrectionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.AvailableUtc, x.CompanyId });
        b.HasOne(x => x.Correction).WithOne(x => x.RefundExecution).HasForeignKey<CustomerInvoiceRefundExecution>(x => new { x.CompanyId, x.CorrectionId }).HasPrincipalKey<CustomerInvoiceCorrection>(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerInvoiceCorrectionAllocationAdjustmentConfiguration : IEntityTypeConfiguration<CustomerInvoiceCorrectionAllocationAdjustment>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceCorrectionAllocationAdjustment> b)
    {
        b.ToTable("customer_invoice_correction_allocation_adjustments");
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.CorrectionId).HasColumnName("correction_id"); b.Property(x => x.PaymentAllocationId).HasColumnName("payment_allocation_id");
        b.Property(x => x.ReleasedAmount).HasColumnName("released_amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasIndex(x => new { x.CompanyId, x.CorrectionId, x.PaymentAllocationId }).IsUnique();
        b.HasOne(x => x.Correction).WithMany().HasForeignKey(x => new { x.CompanyId, x.CorrectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PaymentAllocation).WithMany().HasForeignKey(x => new { x.CompanyId, x.PaymentAllocationId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
