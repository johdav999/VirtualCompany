using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentAllocationEntityConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> builder)
    {
        builder.ToTable("payment_allocations");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_payment_allocations_amount_positive", "allocated_amount > 0");
            t.HasCheckConstraint("CK_payment_allocations_single_target", "((invoice_id IS NOT NULL AND bill_id IS NULL) OR (invoice_id IS NULL AND bill_id IS NOT NULL))");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.PaymentId).HasColumnName("payment_id").IsRequired();
        builder.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        builder.Property(x => x.BillId).HasColumnName("bill_id");
        builder.Property(x => x.SourceSimulationEventRecordId).HasColumnName("source_simulation_event_record_id");
        builder.Property(x => x.PaymentSourceSimulationEventRecordId).HasColumnName("payment_source_simulation_event_record_id");
        builder.Property(x => x.TargetSourceSimulationEventRecordId).HasColumnName("target_source_simulation_event_record_id");
        builder.Property(x => x.AllocatedAmount).HasColumnName("allocated_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.PaymentId });
        builder.HasIndex(x => new { x.CompanyId, x.InvoiceId });
        builder.HasIndex(x => new { x.CompanyId, x.BillId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.PaymentSourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetSourceSimulationEventRecordId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.PaymentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Bill)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);

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
