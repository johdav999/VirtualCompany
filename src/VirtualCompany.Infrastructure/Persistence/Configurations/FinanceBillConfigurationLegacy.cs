using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinanceBillConfigurationLegacy : IEntityTypeConfiguration<FinanceBill>
{
    public void Configure(EntityTypeBuilder<FinanceBill> builder)
    {
        builder.ToTable("finance_bills");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CounterpartyId).HasColumnName("counterparty_id").IsRequired();
        builder.Property(x => x.BillNumber).HasColumnName("bill_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReceivedUtc).HasColumnName("received_at").IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at").IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.PaidAmount).HasColumnName("paid_amount").HasColumnType("decimal(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PostingStatus).HasColumnName("posting_status").HasMaxLength(32).HasDefaultValue(FinanceDocumentPostingStatuses.Booked).IsRequired();
        builder.Property(x => x.SettlementStatus).HasColumnName("settlement_status").HasMaxLength(32).HasDefaultValue(FinanceSettlementStatuses.Unpaid).IsRequired();
        builder.Property(x => x.DueStatus).HasColumnName("due_status").HasMaxLength(32).HasDefaultValue(FinanceDocumentDueStatuses.NotDue).IsRequired();
        builder.Property(x => x.DocumentKind).HasColumnName("document_kind").HasMaxLength(32).HasDefaultValue(FinanceDocumentKinds.SupplierInvoice).IsRequired();
        builder.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(128);
        builder.Property(x => x.ProcessingStatus).HasColumnName("processing_status").HasMaxLength(32).HasDefaultValue(FinanceDocumentProcessingStatuses.None).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BillNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.PostingStatus, x.SettlementStatus, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DocumentKind, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProcessingStatus, x.DueUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Counterparty).WithMany(x => x.Bills).HasForeignKey(x => new { x.CompanyId, x.CounterpartyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Transactions).WithOne(x => x.Bill).HasForeignKey(x => new { x.CompanyId, x.BillId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

