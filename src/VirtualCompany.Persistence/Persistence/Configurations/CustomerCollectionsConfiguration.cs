using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CustomerCollectionPolicyConfiguration : IEntityTypeConfiguration<CustomerCollectionPolicy>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionPolicy> b)
    {
        b.ToTable("customer_collection_policies"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.GracePeriodDays).HasColumnName("grace_period_days"); b.Property(x => x.MaterialityThreshold).HasColumnName("materiality_threshold").HasPrecision(19, 2);
        b.Property(x => x.DefaultLocale).HasColumnName("default_locale").HasMaxLength(16); b.Property(x => x.RequireApproval).HasColumnName("require_approval");
        b.Property(x => x.FeesEnabled).HasColumnName("fees_enabled"); b.Property(x => x.InterestEnabled).HasColumnName("interest_enabled");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.HasMany(x => x.Stages).WithOne(x => x.Policy).HasForeignKey(x => new { x.CompanyId, x.PolicyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerCollectionPolicyStageConfiguration : IEntityTypeConfiguration<CustomerCollectionPolicyStage>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionPolicyStage> b)
    {
        b.ToTable("customer_collection_policy_stages"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.PolicyId).HasColumnName("policy_id");
        b.Property(x => x.Stage).HasColumnName("stage"); b.Property(x => x.DaysAfterDue).HasColumnName("days_after_due"); b.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(32);
        b.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(100); b.Property(x => x.RequiresApproval).HasColumnName("requires_approval");
        b.HasIndex(x => new { x.CompanyId, x.PolicyId, x.Stage }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.PolicyId, x.DaysAfterDue });
    }
}

public sealed class CustomerCollectionPolicyExceptionConfiguration : IEntityTypeConfiguration<CustomerCollectionPolicyException>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionPolicyException> b)
    {
        b.ToTable("customer_collection_policy_exceptions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.PolicyId).HasColumnName("policy_id"); b.Property(x => x.CustomerId).HasColumnName("customer_id");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500); b.Property(x => x.ExcludedUntilDate).HasColumnName("excluded_until_date");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.HasIndex(x => new { x.CompanyId, x.PolicyId, x.CustomerId }).IsUnique();
        b.HasOne(x => x.Policy).WithMany(x => x.Exceptions).HasForeignKey(x => new { x.CompanyId, x.PolicyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CustomerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerStatementSnapshotConfiguration : IEntityTypeConfiguration<CustomerStatementSnapshot>
{
    public void Configure(EntityTypeBuilder<CustomerStatementSnapshot> b)
    {
        b.ToTable("customer_statement_snapshots"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CustomerId).HasColumnName("customer_id");
        b.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(300); b.Property(x => x.FromDate).HasColumnName("from_date"); b.Property(x => x.CutoffDate).HasColumnName("cutoff_date");
        b.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100); b.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(16); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.OpeningBalance).HasColumnName("opening_balance").HasPrecision(19, 2); b.Property(x => x.InvoiceActivity).HasColumnName("invoice_activity").HasPrecision(19, 2);
        b.Property(x => x.AllocationActivity).HasColumnName("allocation_activity").HasPrecision(19, 2); b.Property(x => x.CreditActivity).HasColumnName("credit_activity").HasPrecision(19, 2);
        b.Property(x => x.ClosingBalance).HasColumnName("closing_balance").HasPrecision(19, 2); b.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64);
        b.Property(x => x.SourceManifestJson).HasColumnName("source_manifest_json").HasColumnType("nvarchar(max)"); b.Property(x => x.SourceManifestHash).HasColumnName("source_manifest_hash").HasMaxLength(64);
        b.Property(x => x.MediaType).HasColumnName("media_type").HasMaxLength(100); b.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255);
        b.Property(x => x.RenderedContent).HasColumnName("rendered_content").HasColumnType("varbinary(max)"); b.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
        b.Property(x => x.ContentLength).HasColumnName("content_length"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3);
        b.Property(x => x.FunctionalOpeningBalance).HasColumnName("functional_opening_balance").HasPrecision(19, 2);
        b.Property(x => x.FunctionalInvoiceActivity).HasColumnName("functional_invoice_activity").HasPrecision(19, 2);
        b.Property(x => x.FunctionalAllocationActivity).HasColumnName("functional_allocation_activity").HasPrecision(19, 2);
        b.Property(x => x.FunctionalCreditActivity).HasColumnName("functional_credit_activity").HasPrecision(19, 2);
        b.Property(x => x.FunctionalClosingBalance).HasColumnName("functional_closing_balance").HasPrecision(19, 2);
        b.Property(x => x.FunctionalEvidenceStatus).HasColumnName("functional_evidence_status").HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CustomerId, x.CutoffDate }); b.HasIndex(x => new { x.CompanyId, x.Checksum });
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CustomerId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Items).WithOne(x => x.Statement).HasForeignKey(x => new { x.CompanyId, x.StatementId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerStatementItemConfiguration : IEntityTypeConfiguration<CustomerStatementItem>
{
    public void Configure(EntityTypeBuilder<CustomerStatementItem> b)
    {
        b.ToTable("customer_statement_items"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.StatementId).HasColumnName("statement_id"); b.Property(x => x.Sequence).HasColumnName("sequence");
        b.Property(x => x.ItemType).HasColumnName("item_type").HasMaxLength(32); b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.PaymentAllocationId).HasColumnName("payment_allocation_id");
        b.Property(x => x.EffectiveDate).HasColumnName("effective_date"); b.Property(x => x.Reference).HasColumnName("reference").HasMaxLength(200);
        b.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasPrecision(19, 2); b.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(19, 2);
        b.Property(x => x.RunningBalance).HasColumnName("running_balance").HasPrecision(19, 2); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64);
        b.Property(x => x.FunctionalDebitAmount).HasColumnName("functional_debit_amount").HasPrecision(19, 2);
        b.Property(x => x.FunctionalCreditAmount).HasColumnName("functional_credit_amount").HasPrecision(19, 2);
        b.Property(x => x.FunctionalRunningBalance).HasColumnName("functional_running_balance").HasPrecision(19, 2);
        b.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasColumnName("exchange_rate").HasPrecision(28, 12);
        b.Property(x => x.ExchangeRateDate).HasColumnName("exchange_rate_date");
        b.Property(x => x.ExchangeRateIdentity).HasColumnName("exchange_rate_identity").HasMaxLength(64);
        b.Property(x => x.CurrencyProvenance).HasColumnName("currency_provenance").HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.StatementId, x.Sequence }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.InvoiceId });
    }
}

public sealed class CustomerCollectionCaseConfiguration : IEntityTypeConfiguration<CustomerCollectionCase>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionCase> b)
    {
        b.ToTable("customer_collection_cases"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CustomerId).HasColumnName("customer_id"); b.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.ReminderStage).HasColumnName("reminder_stage"); b.Property(x => x.IsOnHold).HasColumnName("is_on_hold"); b.Property(x => x.HoldReason).HasColumnName("hold_reason").HasMaxLength(1000);
        b.Property(x => x.DisputeStatus).HasColumnName("dispute_status").HasMaxLength(32); b.Property(x => x.DisputeReason).HasColumnName("dispute_reason").HasMaxLength(1000); b.Property(x => x.DisputedAmount).HasColumnName("disputed_amount").HasPrecision(19, 2);
        b.Property(x => x.PromiseStatus).HasColumnName("promise_status").HasMaxLength(32); b.Property(x => x.PromiseAmount).HasColumnName("promise_amount").HasPrecision(19, 2); b.Property(x => x.PromiseDueDate).HasColumnName("promise_due_date");
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.FollowUpDueUtc).HasColumnName("follow_up_due_utc"); b.Property(x => x.WorkTaskId).HasColumnName("work_task_id");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        b.HasIndex(x => new { x.CompanyId, x.InvoiceId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CustomerId, x.Status }); b.HasIndex(x => new { x.CompanyId, x.FollowUpDueUtc });
        b.HasOne<FinanceInvoice>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerCollectionActionConfiguration : IEntityTypeConfiguration<CustomerCollectionAction>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionAction> b)
    {
        b.ToTable("customer_collection_actions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CaseId).HasColumnName("case_id");
        b.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(64); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32); b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000);
        b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.OccurredUtc).HasColumnName("occurred_utc");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.CaseId, x.OccurredUtc });
        b.HasOne<CustomerCollectionCase>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerReminderDraftConfiguration : IEntityTypeConfiguration<CustomerReminderDraft>
{
    public void Configure(EntityTypeBuilder<CustomerReminderDraft> b)
    {
        b.ToTable("customer_reminder_drafts"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.CaseId).HasColumnName("case_id"); b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.CustomerId).HasColumnName("customer_id"); b.Property(x => x.StatementId).HasColumnName("statement_id");
        b.Property(x => x.Stage).HasColumnName("stage"); b.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320); b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(300); b.Property(x => x.Body).HasColumnName("body").HasMaxLength(8000);
        b.Property(x => x.PreparedOpenAmount).HasColumnName("prepared_open_amount").HasPrecision(19, 2); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64);
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.PreparedByUserId).HasColumnName("prepared_by_user_id");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.InvoiceId, x.Stage, x.SourceHash }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<CustomerCollectionCase>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CaseId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CustomerStatementSnapshot>().WithMany().HasForeignKey(x => new { x.CompanyId, x.StatementId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerReminderDeliveryConfiguration : IEntityTypeConfiguration<CustomerReminderDelivery>
{
    public void Configure(EntityTypeBuilder<CustomerReminderDelivery> b)
    {
        b.ToTable("customer_reminder_deliveries"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ReminderDraftId).HasColumnName("reminder_draft_id"); b.Property(x => x.SourceHash).HasColumnName("source_hash").HasMaxLength(64);
        b.Property(x => x.RecipientEmail).HasColumnName("recipient_email").HasMaxLength(320); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.RequestedByUserId).HasColumnName("requested_by_user_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.Attempts).HasColumnName("attempts"); b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(256);
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.AcceptedUtc).HasColumnName("accepted_utc");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ReminderDraftId, x.CreatedUtc }); b.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        b.HasOne<CustomerReminderDraft>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ReminderDraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerCollectionWorkerLeaseConfiguration : IEntityTypeConfiguration<CustomerCollectionWorkerLease>
{
    public void Configure(EntityTypeBuilder<CustomerCollectionWorkerLease> b)
    {
        b.ToTable("customer_collection_worker_leases"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc");
        b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_utc");
        b.Property(x => x.LastFailureCode).HasColumnName("last_failure_code").HasMaxLength(100); b.Property(x => x.LastFailureSummary).HasColumnName("last_failure_summary").HasMaxLength(1000);
        b.Property(x => x.IsBlocked).HasColumnName("is_blocked"); b.Property(x => x.BlockedUtc).HasColumnName("blocked_utc");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        b.HasIndex(x => x.CompanyId).IsUnique(); b.HasIndex(x => new { x.NextAttemptUtc, x.LeaseExpiresUtc, x.CompanyId });
    }
}
