using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CustomerInvoiceCustomerSnapshotConfiguration : IEntityTypeConfiguration<CustomerInvoiceCustomerSnapshot>
{
    public void Configure(EntityTypeBuilder<CustomerInvoiceCustomerSnapshot> b)
    {
        b.ToTable("customer_invoice_customer_snapshots"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id"); b.Property(x => x.CounterpartyId).HasColumnName("counterparty_id");
        b.Property(x => x.BillingProfileVersion).HasColumnName("billing_profile_version"); b.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32);
        b.Property(x => x.SnapshotJson).HasColumnName("snapshot_json").HasMaxLength(16000); b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.InvoiceId }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.CounterpartyId, x.CreatedUtc });
        b.HasOne<FinanceInvoice>().WithMany().HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceCounterparty>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
