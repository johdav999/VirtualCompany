using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CustomerInvoiceScheduleConfiguration : IEntityTypeConfiguration<CustomerInvoiceSchedule>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceSchedule> builder)
    {
        builder.ToTable("customer_invoice_schedules"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200); builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date"); builder.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(x => x.Cadence).HasColumnName("cadence").HasMaxLength(32); builder.Property(x => x.BillingDay).HasColumnName("billing_day"); builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100);
        builder.Property(x => x.BusinessDayConvention).HasColumnName("business_day_convention").HasMaxLength(32); builder.Property(x => x.ProrationRule).HasColumnName("proration_rule").HasMaxLength(32); builder.Property(x => x.DueDateOffsetDays).HasColumnName("due_date_offset_days");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32); builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); builder.Property(x => x.PaymentTermKind).HasColumnName("payment_term_kind").HasMaxLength(32); builder.Property(x => x.PaymentTermDays).HasColumnName("payment_term_days");
        builder.Property(x => x.BuyerReference).HasColumnName("buyer_reference").HasMaxLength(100); builder.Property(x => x.SellerReference).HasColumnName("seller_reference").HasMaxLength(100); builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000); builder.Property(x => x.DeliveryIntent).HasColumnName("delivery_intent").HasMaxLength(32); builder.Property(x => x.AutoIssueEnabled).HasColumnName("auto_issue_enabled");
        builder.Property(x => x.TemplateHash).HasColumnName("template_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.TemplateVersion).HasColumnName("template_version");
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); builder.Property(x => x.ApprovalTemplateVersion).HasColumnName("approval_template_version"); builder.Property(x => x.ApprovalTemplateHash).HasColumnName("approval_template_hash").HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.NextOccurrenceDate).HasColumnName("next_occurrence_date").HasColumnType("date"); builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextOccurrenceDate }); builder.HasIndex(x => new { x.CompanyId, x.CustomerId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique().HasFilter("approval_request_id IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => new { x.CompanyId, x.CustomerId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRequest).WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class CustomerInvoiceScheduleLineConfiguration : IEntityTypeConfiguration<CustomerInvoiceScheduleLine>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceScheduleLine> builder)
    {
        builder.ToTable("customer_invoice_schedule_lines"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id"); builder.Property(x => x.Sequence).HasColumnName("sequence"); builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500); builder.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(19, 6); builder.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(32); builder.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(19, 6); builder.Property(x => x.DiscountPercent).HasColumnName("discount_percent").HasPrecision(9, 6); builder.Property(x => x.TaxRuleKey).HasColumnName("tax_rule_key").HasMaxLength(100); builder.Property(x => x.TaxClassification).HasColumnName("tax_classification").HasMaxLength(100); builder.Property(x => x.TaxEvidenceJson).HasColumnName("tax_evidence_json").HasMaxLength(8000); builder.Property(x => x.DimensionFactsJson).HasColumnName("dimension_facts_json").HasMaxLength(8000); builder.Property(x => x.RevenueAccountRoleKey).HasColumnName("revenue_account_role_key").HasMaxLength(100); builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200); builder.Property(x => x.OrderReference).HasColumnName("order_reference").HasMaxLength(200);
        builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.Sequence }).IsUnique(); builder.HasOne(x => x.Schedule).WithMany(x => x.Lines).HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerInvoiceScheduleEvidenceLinkConfiguration : IEntityTypeConfiguration<CustomerInvoiceScheduleEvidenceLink>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceScheduleEvidenceLink> builder)
    {
        builder.ToTable("customer_invoice_schedule_evidence_links"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id"); builder.Property(x => x.DocumentId).HasColumnName("document_id"); builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.DocumentId }).IsUnique(); builder.HasOne(x => x.Schedule).WithMany(x => x.EvidenceLinks).HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); builder.HasOne(x => x.Document).WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerInvoiceScheduleOccurrenceConfiguration : IEntityTypeConfiguration<CustomerInvoiceScheduleOccurrence>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceScheduleOccurrence> builder)
    {
        builder.ToTable("customer_invoice_schedule_occurrences"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id }); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id"); builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date").HasColumnType("date"); builder.Property(x => x.IssueDate).HasColumnName("issue_date").HasColumnType("date"); builder.Property(x => x.DueDate).HasColumnName("due_date").HasColumnType("date"); builder.Property(x => x.ScheduleVersion).HasColumnName("schedule_version"); builder.Property(x => x.TemplateVersion).HasColumnName("template_version"); builder.Property(x => x.TemplateHash).HasColumnName("template_hash").HasMaxLength(64).IsFixedLength(); builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); builder.Property(x => x.DraftId).HasColumnName("draft_id"); builder.Property(x => x.TaskId).HasColumnName("task_id"); builder.Property(x => x.AttemptCount).HasColumnName("attempt_count"); builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_utc"); builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); builder.Property(x => x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); builder.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_utc"); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); builder.HasIndex(x => new { x.CompanyId, x.ScheduleId, x.OccurrenceDate }).IsUnique(); builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextAttemptUtc, x.LeaseExpiresUtc }); builder.HasOne(x => x.Schedule).WithMany(x => x.Occurrences).HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CustomerInvoiceScheduleOperationConfiguration : IEntityTypeConfiguration<CustomerInvoiceScheduleOperation>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceScheduleOperation> builder)
    {
        builder.ToTable("customer_invoice_schedule_operations"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id"); builder.Property(x => x.ScheduleId).HasColumnName("schedule_id"); builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32); builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64); builder.Property(x => x.ResultVersion).HasColumnName("result_version"); builder.Property(x => x.CreatedUtc).HasColumnName("created_utc"); builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); builder.HasOne(x => x.Schedule).WithMany().HasForeignKey(x => new { x.CompanyId, x.ScheduleId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
