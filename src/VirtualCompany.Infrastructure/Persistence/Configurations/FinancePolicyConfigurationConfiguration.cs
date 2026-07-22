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
internal sealed class FinancePolicyConfigurationConfiguration : IEntityTypeConfiguration<FinancePolicyConfiguration>
{
    public void Configure(EntityTypeBuilder<FinancePolicyConfiguration> builder)
    {
        builder.ToTable("finance_policy_configurations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ApprovalCurrency).HasColumnName("approval_currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.InvoiceApprovalThreshold).HasColumnName("invoice_approval_threshold").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.BillApprovalThreshold).HasColumnName("bill_approval_threshold").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.RequireCounterpartyForTransactions).HasColumnName("require_counterparty_for_transactions").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.AnomalyDetectionLowerBound).HasColumnName("anomaly_detection_lower_bound").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.AnomalyDetectionUpperBound).HasColumnName("anomaly_detection_upper_bound").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CashRunwayWarningThresholdDays).HasColumnName("cash_runway_warning_threshold_days").IsRequired();
        builder.Property(x => x.CashRunwayCriticalThresholdDays).HasColumnName("cash_runway_critical_threshold_days").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

