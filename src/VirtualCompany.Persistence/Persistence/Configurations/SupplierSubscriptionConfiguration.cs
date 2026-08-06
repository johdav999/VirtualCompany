using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class SupplierSubscriptionConfiguration : IEntityTypeConfiguration<SupplierSubscription>
{
    public void Configure(EntityTypeBuilder<SupplierSubscription> builder)
    {
        builder.ToTable("SupplierSubscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContractReference).HasMaxLength(128);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(x => x.AmountTolerance).HasPrecision(18, 2);
        builder.Property(x => x.Cadence).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.StartDateUtc).HasColumnType("date");
        builder.Property(x => x.EndDateUtc).HasColumnType("date");
        builder.Property(x => x.NextExpectedBillDateUtc).HasColumnType("date");
        builder.Property(x => x.CreatedUtc).HasPrecision(3);
        builder.Property(x => x.UpdatedUtc).HasPrecision(3);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextExpectedBillDateUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CounterpartyId, x.Name });
        builder.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => x.CounterpartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ContractDocument).WithMany().HasForeignKey(x => x.ContractDocumentId).OnDelete(DeleteBehavior.SetNull);
    }
}
