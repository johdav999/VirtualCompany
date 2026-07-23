using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinancialStatementSnapshotLineConfiguration : IEntityTypeConfiguration<FinancialStatementSnapshotLine>
{
    public void Configure(EntityTypeBuilder<FinancialStatementSnapshotLine> builder)
    {
        builder.ToTable("financial_statement_snapshot_lines");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SnapshotId).HasColumnName("snapshot_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id");
        builder.Property(x => x.LineCode).HasColumnName("line_code").HasMaxLength(64).IsRequired();
        builder.Property(x => x.LineName).HasColumnName("line_name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.LineOrder).HasColumnName("line_order").IsRequired();
        builder.Property(x => x.ReportSection)
            .HasColumnName("report_section")
            .HasConversion(value => value.ToStorageValue(), value => FinancialStatementReportSectionValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.LineClassification)
            .HasColumnName("line_classification")
            .HasConversion(value => value.ToStorageValue(), value => FinancialStatementLineClassificationValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_financial_statement_snapshot_lines_report_section", FinancialStatementReportSectionValues.BuildCheckConstraintSql("report_section"));
            t.HasCheckConstraint("CK_financial_statement_snapshot_lines_line_classification", FinancialStatementLineClassificationValues.BuildCheckConstraintSql("line_classification"));
        });
        builder.HasIndex(x => new { x.SnapshotId, x.LineCode }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SnapshotId, x.LineOrder });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

