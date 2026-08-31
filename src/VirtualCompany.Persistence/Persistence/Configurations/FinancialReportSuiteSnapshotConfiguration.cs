using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinancialReportSuiteSnapshotConfiguration : IEntityTypeConfiguration<FinancialReportSuiteSnapshot>
{
    public void Configure(EntityTypeBuilder<FinancialReportSuiteSnapshot> builder)
    {
        builder.ToTable("financial_report_suite_snapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.ReportKind).HasColumnName("report_kind").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CalculationVersion).HasColumnName("calculation_version").HasMaxLength(64).IsRequired();
        builder.Property(x => x.MappingVersion).HasColumnName("mapping_version").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ParametersHash).HasColumnName("parameters_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReportJson).HasColumnName("report_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.ReportDefinitionVersionId).HasColumnName("report_definition_version_id");
        builder.Property(x => x.ReportDefinitionVersionNumber).HasColumnName("report_definition_version_number");
        builder.Property(x => x.ReportDefinitionHash).HasColumnName("report_definition_hash").HasMaxLength(64);
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.ReportKind, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.ReportKind, x.ParametersHash, x.Checksum });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReportDefinitionVersion).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReportDefinitionVersionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
