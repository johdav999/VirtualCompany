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
            t.HasCheckConstraint("CK_payment_allocations_fee_non_negative", "fee_amount >= 0");
            t.HasCheckConstraint("CK_payment_allocations_write_off_non_negative", "write_off_amount >= 0");
            t.HasCheckConstraint("CK_payment_allocations_payment_amount_positive", "allocated_payment_amount > 0");
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
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        builder.Property(x => x.FeeAmount).HasColumnName("fee_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.WriteOffAmount).HasColumnName("write_off_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.AllocatedPaymentAmount).HasColumnName("allocated_payment_amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.PaymentCurrency).HasColumnName("payment_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.FunctionalCurrency).HasColumnName("functional_currency").HasMaxLength(3);
        builder.Property(x => x.AllocatedFunctionalAmount).HasColumnName("allocated_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.SettlementFunctionalAmount).HasColumnName("settlement_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.BankFunctionalAmount).HasColumnName("bank_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.FeeFunctionalAmount).HasColumnName("fee_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.WriteOffFunctionalAmount).HasColumnName("write_off_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.RealizedGainLossAmount).HasColumnName("realized_gain_loss_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.RoundingFunctionalAmount).HasColumnName("rounding_functional_amount").HasColumnType("decimal(18,2)");
        builder.Property(x => x.DocumentOutstandingAfter).HasColumnName("document_outstanding_after").HasColumnType("decimal(18,2)");
        builder.Property(x => x.FunctionalOutstandingAfter).HasColumnName("functional_outstanding_after").HasColumnType("decimal(18,2)");
        builder.Property(x => x.SettlementRateDate).HasColumnName("settlement_rate_date");
        builder.Property(x => x.SettlementRate).HasColumnName("settlement_rate").HasColumnType("decimal(28,18)");
        builder.Property(x => x.SettlementExchangeRateConversionId).HasColumnName("settlement_exchange_rate_conversion_id");
        builder.Property(x => x.SettlementRateIdentity).HasColumnName("settlement_rate_identity").HasMaxLength(128);
        builder.Property(x => x.SettlementConversionRoundingResidual).HasColumnName("settlement_conversion_rounding_residual").HasColumnType("decimal(28,18)");
        builder.Property(x => x.SettlementLedgerEntryId).HasColumnName("settlement_ledger_entry_id");
        builder.Property(x => x.ReversalLedgerEntryId).HasColumnName("reversal_ledger_entry_id");
        builder.Property(x => x.SettlementStatus).HasColumnName("settlement_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReversedUtc).HasColumnName("reversed_at");
        builder.Property(x => x.ReversedByUserId).HasColumnName("reversed_by_user_id");
        builder.Property(x => x.ReversalReason).HasColumnName("reversal_reason").HasMaxLength(500);
        builder.Property(x => x.ReversalIdempotencyKey).HasColumnName("reversal_idempotency_key").HasMaxLength(200);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.PaymentId });
        builder.HasIndex(x => new { x.CompanyId, x.InvoiceId });
        builder.HasIndex(x => new { x.CompanyId, x.BillId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.PaymentSourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetSourceSimulationEventRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("[idempotency_key] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.SettlementStatus, x.SettlementRateDate });
        builder.HasIndex(x => new { x.CompanyId, x.SettlementExchangeRateConversionId });
        builder.HasIndex(x => new { x.CompanyId, x.SettlementLedgerEntryId }).IsUnique()
            .HasFilter("[settlement_ledger_entry_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ReversalLedgerEntryId }).IsUnique()
            .HasFilter("[reversal_ledger_entry_id] IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ReversalIdempotencyKey }).IsUnique()
            .HasFilter("[reversal_idempotency_key] IS NOT NULL");

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Payment)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.PaymentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Invoice)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Bill)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => new { x.CompanyId, x.BillId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);

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

        builder.HasOne(x => x.SettlementExchangeRateConversion)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SettlementExchangeRateConversionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SettlementLedgerEntry)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.SettlementLedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReversalLedgerEntry)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReversalLedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
