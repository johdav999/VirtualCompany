using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

public sealed class SupplierSubscriptionBillMatchConfiguration : IEntityTypeConfiguration<SupplierSubscriptionBillMatch>
{
    public void Configure(EntityTypeBuilder<SupplierSubscriptionBillMatch> builder)
    {
        builder.ToTable("SupplierSubscriptionBillMatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PeriodStartUtc).HasColumnType("date");
        builder.Property(x => x.PeriodEndUtc).HasColumnType("date");
        builder.Property(x => x.ExpectedBillDateUtc).HasColumnType("date");
        builder.Property(x => x.ExpectedAmount).HasPrecision(18, 2);
        builder.Property(x => x.ActualAmount).HasPrecision(18, 2);
        builder.Property(x => x.AmountVariance).HasPrecision(18, 2);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.MatchMethod).HasMaxLength(32).IsRequired();
        builder.Property(x => x.EvidenceSummary).HasMaxLength(600).IsRequired();
        builder.Property(x => x.CreatedUtc).HasPrecision(3);
        builder.Property(x => x.UpdatedUtc).HasPrecision(3);
        builder.HasIndex(x => new { x.CompanyId, x.BillId })
            .IsUnique()
            .HasFilter("[Status] = 'confirmed'");
        builder.HasIndex(x => new { x.CompanyId, x.SubscriptionId, x.ExpectedBillDateUtc, x.Status });
        builder.HasOne(x => x.Subscription).WithMany(x => x.BillMatches).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Bill).WithMany().HasForeignKey(x => x.BillId).OnDelete(DeleteBehavior.Restrict);
    }
}

