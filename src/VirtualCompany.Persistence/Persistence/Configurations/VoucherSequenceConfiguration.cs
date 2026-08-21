using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class VoucherSequenceConfiguration : IEntityTypeConfiguration<VoucherSequence>
{
    public void Configure(EntityTypeBuilder<VoucherSequence> builder)
    {
        builder.ToTable("accounting_voucher_sequences");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.VoucherSeriesId).HasColumnName("voucher_series_id").IsRequired();
        builder.Property(x => x.FiscalYear).HasColumnName("fiscal_year").IsRequired();
        builder.Property(x => x.LastAllocatedNumber).HasColumnName("last_allocated_number").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.VoucherSeriesId, x.FiscalYear }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.VoucherSeries).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.VoucherSeriesId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
