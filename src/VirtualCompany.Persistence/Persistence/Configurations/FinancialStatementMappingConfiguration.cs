using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinancialStatementMappingConfiguration : IEntityTypeConfiguration<FinancialStatementMapping>
{
    public void Configure(EntityTypeBuilder<FinancialStatementMapping> builder)
    {
        builder.ToTable("financial_statement_mappings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.StatementType)
            .HasColumnName("statement_type")
            .HasConversion(value => value.ToStorageValue(), value => FinancialStatementTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
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
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").HasDefaultValue(1L).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasDefaultValue(new DateOnly(1, 1, 1)).IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to");
        builder.Property(x => x.SupersedesMappingId).HasColumnName("supersedes_mapping_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_financial_statement_mappings_statement_type", FinancialStatementTypeValues.BuildCheckConstraintSql("statement_type"));
            t.HasCheckConstraint("CK_financial_statement_mappings_report_section", FinancialStatementReportSectionValues.BuildCheckConstraintSql("report_section"));
            t.HasCheckConstraint("CK_financial_statement_mappings_line_classification", FinancialStatementLineClassificationValues.BuildCheckConstraintSql("line_classification"));
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId });
        builder.HasIndex(x => x.FinanceAccountId);
        builder.HasIndex(x => new { x.CompanyId, x.StatementType, x.IsActive });
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.StatementType }).HasFilter("is_active = 1").IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.StatementType, x.EffectiveFrom });
        builder.HasOne<FinancialStatementMapping>().WithMany().HasForeignKey(x => x.SupersedesMappingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount)
            .WithMany(x => x.FinancialStatementMappings)
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

