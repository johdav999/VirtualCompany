using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CustomerInvoiceDraftConfiguration : IEntityTypeConfiguration<CustomerInvoiceDraft>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceDraft> builder)
    {
        builder.ToTable("customer_invoice_drafts");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerId).HasColumnName("customer_id").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.IssueDate).HasColumnName("issue_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.SupplyDate).HasColumnName("supply_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.DueDate).HasColumnName("due_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentTermKind).HasColumnName("payment_term_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PaymentTermDays).HasColumnName("payment_term_days").IsRequired();
        builder.Property(x => x.BuyerReference).HasColumnName("buyer_reference").HasMaxLength(100);
        builder.Property(x => x.SellerReference).HasColumnName("seller_reference").HasMaxLength(100);
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);
        builder.Property(x => x.DeliveryIntent).HasColumnName("delivery_intent").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200);
        builder.Property(x => x.OriginalInvoiceId).HasColumnName("original_invoice_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultHash).HasColumnName("result_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PolicyDefinitionHash).HasColumnName("policy_definition_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RoundingPrecision).HasColumnName("rounding_precision").IsRequired();
        builder.Property(x => x.RoundingMode).HasColumnName("rounding_mode").HasMaxLength(32).IsRequired();
        builder.Property(x => x.NetTotal).HasColumnName("net_total").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.DiscountTotal).HasColumnName("discount_total").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.TaxTotal).HasColumnName("tax_total").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.GrossTotal).HasColumnName("gross_total").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.RoundingAmount).HasColumnName("rounding_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.WarningsJson).HasColumnName("warnings_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.BlockersJson).HasColumnName("blockers_json").HasMaxLength(16000).IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.ApprovalDraftVersion).HasColumnName("approval_draft_version");
        builder.Property(x => x.ApprovalResultHash).HasColumnName("approval_result_hash").HasMaxLength(64);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.DiscardedUtc).HasColumnName("discarded_utc");
        builder.Property(x => x.IssuedInvoiceId).HasColumnName("issued_invoice_id");
        builder.Property(x => x.IssuedStatutoryDocumentId).HasColumnName("issued_statutory_document_id");
        builder.Property(x => x.IssuedLedgerEntryId).HasColumnName("issued_ledger_entry_id");
        builder.Property(x => x.IssuedSnapshotHash).HasColumnName("issued_snapshot_hash").HasMaxLength(64);
        builder.Property(x => x.IssuedUtc).HasColumnName("issued_utc");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CustomerId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.OriginalInvoiceId });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalRequestId }).IsUnique().HasFilter("approval_request_id IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.IssuedInvoiceId }).IsUnique().HasFilter("issued_invoice_id IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Customer).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.CustomerId }).HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovalRequest).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ApprovalRequestId }).HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FinanceInvoice>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.OriginalInvoiceId }).HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
