using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentSimulationEventLinkConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceInvoiceSimulationEventLinkConfiguration : IEntityTypeConfiguration<FinanceInvoice>
{
    public void Configure(EntityTypeBuilder<FinanceInvoice> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceBillSimulationEventLinkConfiguration : IEntityTypeConfiguration<FinanceBill>
{
    public void Configure(EntityTypeBuilder<FinanceBill> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceTransactionSimulationEventLinkConfiguration : IEntityTypeConfiguration<FinanceTransaction>
{
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentAllocationSimulationEventLinkConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.Property(x => x.PaymentSourceSimulationEventRecordId).HasColumnName("payment_source_simulation_event_record_id");
        builder.Property(x => x.TargetSourceSimulationEventRecordId).HasColumnName("target_source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.PaymentSourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetSourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PaymentSourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.PaymentSourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.TargetSourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.TargetSourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceBalanceSimulationEventLinkConfiguration : IEntityTypeConfiguration<FinanceBalance>
{
    public void Configure(EntityTypeBuilder<FinanceBalance> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceAssetSimulationEventLinkConfiguration : IEntityTypeConfiguration<FinanceAsset>
{
    public void Configure(EntityTypeBuilder<FinanceAsset> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankTransactionSimulationEventLinkConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasOne(x => x.SourceSimulationEventRecord)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SourceSimulationEventRecordId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}