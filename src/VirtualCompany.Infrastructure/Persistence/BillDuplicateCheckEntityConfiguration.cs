using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BillDuplicateCheckEntityConfiguration : IEntityTypeConfiguration<BillDuplicateCheck>
{
    public void Configure(EntityTypeBuilder<BillDuplicateCheck> builder)
    {
        builder.ToTable("bill_duplicate_checks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(200);
        builder.Property(x => x.SupplierOrgNumber).HasColumnName("supplier_org_number").HasMaxLength(64);
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(64);
        builder.Property(x => x.TotalAmount).HasColumnName("total_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.IsDuplicate).HasColumnName("is_duplicate").IsRequired();
        builder.Property(x => x.ResultStatus).HasColumnName("result_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.MatchedBillIdsJson).HasColumnName("matched_bill_ids_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CriteriaSummary).HasColumnName("criteria_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceEmailId).HasColumnName("source_email_id").HasMaxLength(512);
        builder.Property(x => x.SourceAttachmentId).HasColumnName("source_attachment_id").HasMaxLength(512);
        builder.Property(x => x.CheckedUtc).HasColumnName("checked_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.ResultStatus, x.CheckedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SupplierName, x.InvoiceNumber, x.TotalAmount });
        builder.HasIndex(x => new { x.CompanyId, x.CheckedUtc });
        builder.ToTable(t =>
            t.HasCheckConstraint("CK_bill_duplicate_checks_result_status", "result_status IN ('pending', 'not_duplicate', 'duplicate', 'inconclusive')"));

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
