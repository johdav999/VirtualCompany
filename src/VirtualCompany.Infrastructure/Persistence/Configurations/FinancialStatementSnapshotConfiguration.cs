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
internal sealed class FinancialStatementSnapshotConfiguration : IEntityTypeConfiguration<FinancialStatementSnapshot>
{
    public void Configure(EntityTypeBuilder<FinancialStatementSnapshot> builder)
    {
        builder.ToTable("financial_statement_snapshots");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.StatementType)
            .HasColumnName("statement_type")
            .HasConversion(value => value.ToStorageValue(), value => FinancialStatementTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourcePeriodStartUtc).HasColumnName("source_period_start_at").IsRequired();
        builder.Property(x => x.SourcePeriodEndUtc).HasColumnName("source_period_end_at").IsRequired();
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.BalancesChecksum).HasColumnName("balances_checksum").HasMaxLength(128).IsRequired();
        builder.Property(x => x.GeneratedAtUtc).HasColumnName("generated_at").IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();

        builder.ToTable(t => t.HasCheckConstraint("CK_financial_statement_snapshots_statement_type", FinancialStatementTypeValues.BuildCheckConstraintSql("statement_type")));

        builder.HasIndex(x => new { x.CompanyId, x.StatementType, x.FiscalPeriodId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.StatementType, x.FiscalPeriodId, x.GeneratedAtUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Lines).WithOne(x => x.Snapshot).HasForeignKey(x => x.SnapshotId).OnDelete(DeleteBehavior.NoAction);
    }
}

