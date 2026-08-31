using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class VatFilingPeriodConfiguration : IEntityTypeConfiguration<VatFilingPeriod>
{
    public void Configure(EntityTypeBuilder<VatFilingPeriod> b)
    {
        b.ToTable("vat_filing_periods"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.PeriodCode).HasColumnName("period_code").HasMaxLength(40).IsRequired();
        b.Property(x => x.StartDate).HasColumnName("start_date"); b.Property(x => x.EndDate).HasColumnName("end_date");
        b.Property(x => x.DueDate).HasColumnName("due_date");
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.PeriodCode }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.StartDate, x.EndDate }).IsUnique();
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.FiscalPeriod).WithMany().HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VatReturnConfiguration : IEntityTypeConfiguration<VatReturn>
{
    public void Configure(EntityTypeBuilder<VatReturn> b)
    {
        b.ToTable("vat_returns"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.FilingPeriodId).HasColumnName("filing_period_id"); b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24).IsRequired();
        b.Property(x => x.CorrectionOfVatReturnId).HasColumnName("correction_of_vat_return_id");
        b.Property(x => x.CorrectionReason).HasColumnName("correction_reason").HasMaxLength(1000);
        b.Property(x => x.CorrectionEvidenceReference).HasColumnName("correction_evidence_reference").HasMaxLength(500);
        b.Property(x => x.CutoffUtc).HasColumnName("cutoff_at"); b.Property(x => x.InputHash).HasColumnName("input_hash").HasMaxLength(64);
        b.Property(x => x.CalculationChecksum).HasColumnName("calculation_checksum").HasMaxLength(64);
        b.Property(x => x.IncludedSourceCount).HasColumnName("included_source_count"); b.Property(x => x.ExcludedSourceCount).HasColumnName("excluded_source_count");
        b.Property(x => x.OutputVatExact).HasColumnName("output_vat_exact").HasPrecision(19, 6);
        b.Property(x => x.InputVatExact).HasColumnName("input_vat_exact").HasPrecision(19, 6);
        b.Property(x => x.SettlementExact).HasColumnName("settlement_exact").HasPrecision(19, 6);
        b.Property(x => x.SettlementFilingAmount).HasColumnName("settlement_filing_amount");
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        b.Property(x => x.FinalizedByUserId).HasColumnName("finalized_by_user_id"); b.Property(x => x.FinalizedUtc).HasColumnName("finalized_at");
        b.Property(x => x.PackageStorageKey).HasColumnName("package_storage_key").HasMaxLength(500);
        b.Property(x => x.PackageChecksum).HasColumnName("package_checksum").HasMaxLength(64);
        b.Property(x => x.PackageFileName).HasColumnName("package_file_name").HasMaxLength(180);
        b.Property(x => x.PackageMediaType).HasColumnName("package_media_type").HasMaxLength(100);
        b.Property(x => x.PackageContentLength).HasColumnName("package_content_length");
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();
        b.HasIndex(x => new { x.CompanyId, x.FilingPeriodId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.FilingPeriodId, x.Status });
        b.HasOne(x => x.FilingPeriod).WithMany(x => x.Returns).HasForeignKey(x => new { x.CompanyId, x.FilingPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CorrectionOfVatReturn).WithMany(x => x.Corrections).HasForeignKey(x => new { x.CompanyId, x.CorrectionOfVatReturnId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VatReturnBoxResultConfiguration : IEntityTypeConfiguration<VatReturnBoxResult>
{
    public void Configure(EntityTypeBuilder<VatReturnBoxResult> b)
    {
        b.ToTable("vat_return_box_results"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VatReturnId).HasColumnName("vat_return_id");
        b.Property(x => x.BoxCode).HasColumnName("box_code").HasMaxLength(8); b.Property(x => x.FactType).HasColumnName("fact_type").HasMaxLength(40);
        b.Property(x => x.ExactAmount).HasColumnName("exact_amount").HasPrecision(19, 6); b.Property(x => x.FilingAmount).HasColumnName("filing_amount");
        b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3); b.Property(x => x.SourceCount).HasColumnName("source_count");
        b.HasIndex(x => new { x.CompanyId, x.VatReturnId, x.BoxCode }).IsUnique();
        b.HasOne(x => x.VatReturn).WithMany(x => x.Boxes).HasForeignKey(x => new { x.CompanyId, x.VatReturnId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VatReturnSourceContributionConfiguration : IEntityTypeConfiguration<VatReturnSourceContribution>
{
    public void Configure(EntityTypeBuilder<VatReturnSourceContribution> b)
    {
        b.ToTable("vat_return_source_contributions"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VatReturnId).HasColumnName("vat_return_id");
        b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id"); b.Property(x => x.VoucherNumber).HasColumnName("voucher_number").HasMaxLength(64);
        b.Property(x => x.PostingDate).HasColumnName("posting_date"); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64);
        b.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(64);
        b.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(96); b.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32);
        b.Property(x => x.TaxRuleKey).HasColumnName("tax_rule_key").HasMaxLength(96); b.Property(x => x.TaxRuleVersion).HasColumnName("tax_rule_version").HasMaxLength(32);
        b.Property(x => x.BoxCode).HasColumnName("box_code").HasMaxLength(8); b.Property(x => x.FactType).HasColumnName("fact_type").HasMaxLength(40);
        b.Property(x => x.ExactAmount).HasColumnName("exact_amount").HasPrecision(19, 6); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        b.Property(x => x.SourceChecksum).HasColumnName("source_checksum").HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.VatReturnId, x.LedgerEntryId, x.SourceChecksum, x.BoxCode }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.LedgerEntryId });
        b.HasOne(x => x.VatReturn).WithMany(x => x.Contributions).HasForeignKey(x => new { x.CompanyId, x.VatReturnId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VatReturnValidationIssueConfiguration : IEntityTypeConfiguration<VatReturnValidationIssue>
{
    public void Configure(EntityTypeBuilder<VatReturnValidationIssue> b)
    {
        b.ToTable("vat_return_validation_issues"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VatReturnId).HasColumnName("vat_return_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(100); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        b.Property(x => x.IsBlocking).HasColumnName("is_blocking"); b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(500); b.Property(x => x.Difference).HasColumnName("difference").HasPrecision(19, 6);
        b.HasIndex(x => new { x.CompanyId, x.VatReturnId, x.Code });
        b.HasOne(x => x.VatReturn).WithMany(x => x.Issues).HasForeignKey(x => new { x.CompanyId, x.VatReturnId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class VatReturnReviewConfiguration : IEntityTypeConfiguration<VatReturnReview>
{
    public void Configure(EntityTypeBuilder<VatReturnReview> b)
    {
        b.ToTable("vat_return_reviews"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.VatReturnId).HasColumnName("vat_return_id");
        b.Property(x => x.Action).HasColumnName("action").HasMaxLength(40); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.EvidenceHash).HasColumnName("evidence_hash").HasMaxLength(64);
        b.Property(x => x.OccurredUtc).HasColumnName("occurred_at");
        b.HasIndex(x => new { x.CompanyId, x.VatReturnId, x.OccurredUtc });
        b.HasOne(x => x.VatReturn).WithMany(x => x.Reviews).HasForeignKey(x => new { x.CompanyId, x.VatReturnId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
