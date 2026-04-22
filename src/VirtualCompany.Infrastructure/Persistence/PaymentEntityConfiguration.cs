using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class PaymentEntityConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("finance_payments");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_payments_amount_positive", "amount > 0");
            t.HasCheckConstraint("CK_finance_payments_payment_type", PaymentTypes.BuildCheckConstraintSql("payment_type"));
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.PaymentType).HasColumnName("payment_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentDate).HasColumnName("payment_date").IsRequired();
        builder.Property(x => x.Method).HasColumnName("method").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.CounterpartyReference).HasColumnName("counterparty_reference").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PaymentDate);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.CashLedgerLinks)
            .WithOne(x => x.Payment)
            .HasForeignKey(x => x.PaymentId);
    }
}
