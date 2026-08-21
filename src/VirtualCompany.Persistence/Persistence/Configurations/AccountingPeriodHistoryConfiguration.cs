using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingPeriodHistoryConfiguration : IEntityTypeConfiguration<AccountingPeriodHistory>
{
    public void Configure(EntityTypeBuilder<AccountingPeriodHistory> builder)
    {
        builder.ToTable("accounting_period_history");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SnapshotChecksum).HasColumnName("snapshot_checksum").HasMaxLength(64);
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.OccurredUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
