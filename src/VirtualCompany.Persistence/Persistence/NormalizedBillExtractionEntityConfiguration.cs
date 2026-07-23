using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class NormalizedBillExtractionEntityConfiguration : IEntityTypeConfiguration<NormalizedBillExtraction>
{
    public void Configure(EntityTypeBuilder<NormalizedBillExtraction> builder)
    {
        builder.ToTable("normalized_bill_extractions");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(200);
        builder.Property(x => x.SupplierOrgNumber).HasColumnName("supplier_org_number").HasMaxLength(64);
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(64);
        builder.Property(x => x.InvoiceDateUtc).HasColumnName("invoice_date");
        builder.Property(x => x.DueDateUtc).HasColumnName("due_date");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.VatAmount).HasColumnName("vat_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.PaymentReference).HasColumnName("payment_reference").HasMaxLength(128);
        builder.Property(x => x.Bankgiro).HasColumnName("bankgiro").HasMaxLength(32);
        builder.Property(x => x.Plusgiro).HasColumnName("plusgiro").HasMaxLength(32);
        builder.Property(x => x.Iban).HasColumnName("iban").HasMaxLength(34);
        builder.Property(x => x.Bic).HasColumnName("bic").HasMaxLength(11);
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasMaxLength(16).IsRequired();
        builder.Property(x => x.SourceEmailId).HasColumnName("source_email_id").HasMaxLength(512);
        builder.Property(x => x.SourceAttachmentId).HasColumnName("source_attachment_id").HasMaxLength(512);
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ValidationFindingsJson).HasColumnName("validation_findings_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DuplicateCheckId).HasColumnName("duplicate_check_id").IsRequired();
        builder.Property(x => x.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(x => x.IsEligibleForApprovalProposal).HasColumnName("is_eligible_for_approval_proposal").IsRequired();
        builder.Property(x => x.ValidationStatusPersistedAtUtc).HasColumnName("validation_status_persisted_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_normalized_bill_extractions_confidence", "confidence IN ('high', 'medium', 'low')");
            t.HasCheckConstraint("CK_normalized_bill_extractions_validation_status", "validation_status IN ('pending', 'valid', 'flagged', 'rejected')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.SupplierOrgNumber, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.RequiresReview, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ValidationStatus, x.CreatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DuplicateCheck)
            .WithMany()
            .HasForeignKey(x => x.DuplicateCheckId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
