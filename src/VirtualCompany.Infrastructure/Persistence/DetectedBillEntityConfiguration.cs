using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class DetectedBillEntityConfiguration : IEntityTypeConfiguration<DetectedBill>
{
    public void Configure(EntityTypeBuilder<DetectedBill> builder)
    {
        builder.ToTable("detected_bills");

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
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,4)");
        builder.Property(x => x.ConfidenceLevel).HasColumnName("confidence_level").HasMaxLength(16).IsRequired();
        builder.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReviewStatus).HasColumnName("review_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequiresReview).HasColumnName("requires_review").IsRequired();
        builder.Property(x => x.IsEligibleForApprovalProposal).HasColumnName("is_eligible_for_approval_proposal").IsRequired();
        builder.Property(x => x.ValidationStatusPersisted).HasColumnName("validation_status_persisted").IsRequired();
        builder.Property(x => x.ValidationStatusPersistedAtUtc).HasColumnName("validation_status_persisted_at");
        builder.Property(x => x.ValidationIssuesJson).HasColumnName("validation_issues_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SourceEmailId).HasColumnName("source_email_id").HasMaxLength(512);
        builder.Property(x => x.SourceAttachmentId).HasColumnName("source_attachment_id").HasMaxLength(512);
        builder.Property(x => x.DuplicateCheckId).HasColumnName("duplicate_check_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.Navigation(x => x.Fields).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_detected_bills_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
            t.HasCheckConstraint("CK_detected_bills_confidence_level", "confidence_level IN ('high', 'medium', 'low')");
            t.HasCheckConstraint("CK_detected_bills_validation_status", "validation_status IN ('pending', 'valid', 'flagged', 'rejected')");
            t.HasCheckConstraint("CK_detected_bills_review_status", "review_status IN ('not_required', 'required', 'completed')");
        });

        builder.HasIndex(x => new { x.CompanyId, x.SourceEmailId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceAttachmentId });
        builder.HasIndex(x => new { x.CompanyId, x.SupplierName, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.SupplierOrgNumber, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.ConfidenceLevel, x.RequiresReview, x.CreatedUtc });
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

internal sealed class DetectedBillFieldEntityConfiguration : IEntityTypeConfiguration<DetectedBillField>
{
    public void Configure(EntityTypeBuilder<DetectedBillField> builder)
    {
        builder.ToTable("detected_bill_fields");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DetectedBillId).HasColumnName("detected_bill_id").IsRequired();
        builder.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RawValue).HasColumnName("raw_value").HasMaxLength(2000);
        builder.Property(x => x.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(2000);
        builder.Property(x => x.SourceDocument).HasColumnName("source_document").HasMaxLength(512).IsRequired();
        builder.Property(x => x.SourceDocumentType).HasColumnName("source_document_type").HasMaxLength(64);
        builder.Property(x => x.PageReference).HasColumnName("page_reference").HasMaxLength(128);
        builder.Property(x => x.SectionReference).HasColumnName("section_reference").HasMaxLength(128);
        builder.Property(x => x.TextSpan).HasColumnName("text_span").HasMaxLength(128);
        builder.Property(x => x.Locator).HasColumnName("locator").HasMaxLength(512);
        builder.Property(x => x.ExtractionMethod).HasColumnName("extraction_method").HasMaxLength(64).IsRequired();
        builder.Property(x => x.FieldConfidence).HasColumnName("field_confidence").HasColumnType("decimal(5,4)");
        builder.Property(x => x.Snippet).HasColumnName("snippet").HasMaxLength(2000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.ToTable(t =>
            t.HasCheckConstraint("CK_detected_bill_fields_field_confidence", "field_confidence IS NULL OR (field_confidence >= 0 AND field_confidence <= 1)"));

        builder.HasIndex(x => new { x.CompanyId, x.DetectedBillId, x.FieldName }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FieldName });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DetectedBill).WithMany(x => x.Fields).HasForeignKey(x => x.DetectedBillId).OnDelete(DeleteBehavior.Cascade);
    }
}
