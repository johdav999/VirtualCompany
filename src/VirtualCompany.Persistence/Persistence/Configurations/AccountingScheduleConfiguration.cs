using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingScheduleConfiguration : IEntityTypeConfiguration<AccountingSchedule>
{
    public void Configure(EntityTypeBuilder<AccountingSchedule> builder)
    {
        builder.ToTable("accounting_schedules");
        builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64); builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        builder.Property(x => x.ScheduleType).HasColumnName("schedule_type").HasMaxLength(32); builder.Property(x => x.Cadence).HasColumnName("cadence").HasMaxLength(24);
        builder.Property(x => x.AmountBasis).HasColumnName("amount_basis").HasMaxLength(24); builder.Property(x => x.ProrationRule).HasColumnName("proration_rule").HasMaxLength(16);
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date"); builder.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(x => x.OccurrenceDay).HasColumnName("occurrence_day"); builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100);
        builder.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32); builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.ReversalRule).HasColumnName("reversal_rule").HasMaxLength(24); builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.NextOccurrenceDate).HasColumnName("next_occurrence_date").HasColumnType("date"); builder.Property(x => x.CurrentVersionId).HasColumnName("current_version_id");
        builder.Property(x => x.CurrentVersionNumber).HasColumnName("current_version_number"); builder.Property(x => x.CurrentVersionHash).HasColumnName("current_version_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); builder.Property(x => x.ApprovalVersionNumber).HasColumnName("approval_version_number");
        builder.Property(x => x.ApprovalPayloadHash).HasColumnName("approval_payload_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextOccurrenceDate });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique().HasFilter("approval_request_id IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.CurrentVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.CurrentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingScheduleVersionConfiguration : IEntityTypeConfiguration<AccountingScheduleVersion>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleVersion> builder)
    {
        builder.ToTable("accounting_schedule_versions"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        builder.Property(x => x.VersionNumber).HasColumnName("version_number"); builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500); builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.VersionNumber }).IsUnique();
        builder.HasOne(x => x.Schedule).WithMany(x => x.Versions).HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingScheduleLineConfiguration : IEntityTypeConfiguration<AccountingScheduleLine>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleLine> builder)
    {
        builder.ToTable("accounting_schedule_lines"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleVersionId).HasColumnName("schedule_version_id");
        builder.Property(x => x.Sequence).HasColumnName("sequence"); builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id");
        builder.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasPrecision(19, 4); builder.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(19, 4);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500); builder.HasIndex(x => new { x.CompanyId, x.ScheduleVersionId, x.Sequence }).IsUnique();
        builder.HasOne(x => x.ScheduleVersion).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.ScheduleVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountingScheduleLineDimensionConfiguration : IEntityTypeConfiguration<AccountingScheduleLineDimension>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleLineDimension> builder)
    {
        builder.ToTable("accounting_schedule_line_dimensions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.ScheduleLineId).HasColumnName("schedule_line_id"); builder.Property(x => x.DimensionMemberId).HasColumnName("dimension_member_id");
        builder.HasIndex(x => new { x.CompanyId, x.ScheduleLineId, x.DimensionMemberId }).IsUnique();
        builder.HasOne(x => x.ScheduleLine).WithMany(x => x.DimensionAssignments).HasForeignKey(x => new { x.CompanyId, x.ScheduleLineId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DimensionMember).WithMany().HasForeignKey(x => new { x.CompanyId, x.DimensionMemberId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountingScheduleEvidenceLinkConfiguration : IEntityTypeConfiguration<AccountingScheduleEvidenceLink>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleEvidenceLink> builder)
    {
        builder.ToTable("accounting_schedule_evidence_links"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleVersionId).HasColumnName("schedule_version_id");
        builder.Property(x => x.DocumentId).HasColumnName("document_id"); builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(300);
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.LinkedUtc).HasColumnName("linked_utc");
        builder.HasIndex(x => new { x.CompanyId, x.ScheduleVersionId, x.DocumentId }).IsUnique();
        builder.HasOne(x => x.ScheduleVersion).WithMany(x => x.EvidenceLinks).HasForeignKey(x => new { x.CompanyId, x.ScheduleVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountingScheduleApprovalBindingConfiguration : IEntityTypeConfiguration<AccountingScheduleApprovalBinding>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleApprovalBinding> builder)
    {
        builder.ToTable("accounting_schedule_approval_bindings"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        builder.Property(x => x.ScheduleVersionId).HasColumnName("schedule_version_id"); builder.Property(x => x.VersionNumber).HasColumnName("version_number");
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.BoundUtc).HasColumnName("bound_utc"); builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique();
        builder.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ScheduleVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ScheduleVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingScheduleOccurrenceConfiguration : IEntityTypeConfiguration<AccountingScheduleOccurrence>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleOccurrence> builder)
    {
        builder.ToTable("accounting_schedule_occurrences"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        builder.Property(x => x.ScheduleVersionId).HasColumnName("schedule_version_id"); builder.Property(x => x.ScheduleVersionNumber).HasColumnName("schedule_version_number");
        builder.Property(x => x.ScheduleVersionHash).HasColumnName("schedule_version_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date").HasColumnType("date");
        builder.Property(x => x.PostingDate).HasColumnName("posting_date").HasColumnType("date"); builder.Property(x => x.ScheduledAmount).HasColumnName("scheduled_amount").HasPrecision(19, 4);
        builder.Property(x => x.ReleasedAmount).HasColumnName("released_amount").HasPrecision(19, 4); builder.Property(x => x.ReversedAmount).HasColumnName("reversed_amount").HasPrecision(19, 4);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); builder.Property(x => x.ReversalRule).HasColumnName("reversal_rule").HasMaxLength(24);
        builder.Property(x => x.ReversalDueDate).HasColumnName("reversal_due_date").HasColumnType("date"); builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id"); builder.Property(x => x.ReversalLedgerEntryId).HasColumnName("reversal_ledger_entry_id");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count"); builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_utc"); builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(160);
        builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc"); builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); builder.Property(x => x.PostedUtc).HasColumnName("posted_utc"); builder.Property(x => x.ReversedUtc).HasColumnName("reversed_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.OccurrenceDate }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc }); builder.HasIndex(x => new { x.Status, x.ReversalDueDate, x.ReversalLedgerEntryId });
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique().HasFilter("ledger_entry_id IS NOT NULL");
        builder.HasOne(x => x.Schedule).WithMany(x => x.Occurrences).HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ScheduleVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ScheduleVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class AccountingScheduleExceptionConfiguration : IEntityTypeConfiguration<AccountingScheduleOccurrenceException>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleOccurrenceException> builder)
    {
        builder.ToTable("accounting_schedule_exceptions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id"); builder.Property(x => x.OccurrenceId).HasColumnName("occurrence_id");
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000); builder.Property(x => x.SafeNextAction).HasColumnName("safe_next_action").HasMaxLength(1000);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(24); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_utc");
        builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.Status }); builder.HasOne(x => x.Occurrence).WithMany(x => x.Exceptions).HasForeignKey(x => new { x.CompanyId, x.OccurrenceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AccountingScheduleOperationConfiguration : IEntityTypeConfiguration<AccountingScheduleOperation>
{
    public void Configure(EntityTypeBuilder<AccountingScheduleOperation> builder)
    {
        builder.ToTable("accounting_schedule_operations"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id");
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32); builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.ResultVersion).HasColumnName("result_version"); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); builder.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
