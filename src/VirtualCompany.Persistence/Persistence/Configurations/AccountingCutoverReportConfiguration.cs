using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class AccountingCutoverReportConfiguration : IEntityTypeConfiguration<AccountingCutoverReport>
{
    public void Configure(EntityTypeBuilder<AccountingCutoverReport> builder)
    {
        builder.ToTable("accounting_cutover_reports");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MigrationRunId).HasColumnName("migration_run_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.OpeningBalance).HasColumnName("opening_balance").HasPrecision(19, 4);
        builder.Property(x => x.JournalDebit).HasColumnName("journal_debit").HasPrecision(19, 4);
        builder.Property(x => x.JournalCredit).HasColumnName("journal_credit").HasPrecision(19, 4);
        builder.Property(x => x.ReceivablesBalance).HasColumnName("receivables_balance").HasPrecision(19, 4);
        builder.Property(x => x.PayablesBalance).HasColumnName("payables_balance").HasPrecision(19, 4);
        builder.Property(x => x.BankBalance).HasColumnName("bank_balance").HasPrecision(19, 4);
        builder.Property(x => x.SuspenseBalance).HasColumnName("suspense_balance").HasPrecision(19, 4);
        builder.Property(x => x.TaxFactLineCount).HasColumnName("tax_fact_line_count").IsRequired();
        builder.Property(x => x.ProviderReferenceCount).HasColumnName("provider_reference_count").IsRequired();
        builder.Property(x => x.EvidenceLinkCount).HasColumnName("evidence_link_count").IsRequired();
        builder.Property(x => x.SnapshotCount).HasColumnName("snapshot_count").IsRequired();
        builder.Property(x => x.IssueCount).HasColumnName("issue_count").IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnName("details_json").HasMaxLength(32000).IsRequired();
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.GeneratedUtc).HasColumnName("generated_utc").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.MigrationRunId, x.FiscalPeriodId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.GeneratedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.MigrationRun).WithMany(x => x.Reports)
            .HasForeignKey(x => new { x.CompanyId, x.MigrationRunId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.FiscalPeriod).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
