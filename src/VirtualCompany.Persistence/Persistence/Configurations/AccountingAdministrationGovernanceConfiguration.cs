using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingAccountLifecycleHistoryConfiguration : IEntityTypeConfiguration<AccountingAccountLifecycleHistory>
{
    public void Configure(EntityTypeBuilder<AccountingAccountLifecycleHistory> b)
    {
        b.ToTable("accounting_account_lifecycle_history");
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id"); b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.ChangeType).HasColumnName("change_type").HasMaxLength(32); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
        b.Property(x => x.AccountClass).HasColumnName("account_class").HasMaxLength(32); b.Property(x => x.NormalBalance).HasColumnName("normal_balance").HasMaxLength(16);
        b.Property(x => x.IsReportable).HasColumnName("is_reportable"); b.Property(x => x.PostingRestriction).HasColumnName("posting_restriction").HasMaxLength(16);
        b.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date"); b.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        b.Property(x => x.ReplacementAccountId).HasColumnName("replacement_account_id"); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512);
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.RecordedUtc).HasColumnName("recorded_utc");
        b.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.Version }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.FinanceAccountId, x.EffectiveFrom });
        b.HasOne(x => x.FinanceAccount).WithMany(x => x.LifecycleHistory)
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountingSeriesPolicyConfiguration : IEntityTypeConfiguration<AccountingSeriesPolicy>
{
    public void Configure(EntityTypeBuilder<AccountingSeriesPolicy> b)
    {
        b.ToTable("accounting_series_policies"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.SeriesKind).HasColumnName("series_kind").HasMaxLength(32); b.Property(x => x.SeriesId).HasColumnName("series_id");
        b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64); b.Property(x => x.TransactionType).HasColumnName("transaction_type").HasMaxLength(64);
        b.Property(x => x.FiscalYear).HasColumnName("fiscal_year"); b.Property(x => x.LocationDimensionMemberId).HasColumnName("location_dimension_member_id");
        b.Property(x => x.Jurisdiction).HasColumnName("jurisdiction").HasMaxLength(16); b.Property(x => x.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(64);
        b.Property(x => x.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        b.Property(x => x.ProviderSeriesCode).HasColumnName("provider_series_code").HasMaxLength(64); b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(512);
        b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc");
        b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.SeriesKind, x.ScopeKey }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SeriesKind, x.SeriesId, x.IsActive });
    }
}

public sealed class AccountingVoucherGapEvidenceConfiguration : IEntityTypeConfiguration<AccountingVoucherGapEvidence>
{
    public void Configure(EntityTypeBuilder<AccountingVoucherGapEvidence> b)
    {
        b.ToTable("accounting_voucher_gap_evidence"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.VoucherSeriesId).HasColumnName("voucher_series_id"); b.Property(x => x.FiscalYear).HasColumnName("fiscal_year");
        b.Property(x => x.MissingNumber).HasColumnName("missing_number"); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(512);
        b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.RecordedUtc).HasColumnName("recorded_utc");
        b.HasIndex(x => new { x.CompanyId, x.VoucherSeriesId, x.FiscalYear, x.MissingNumber }).IsUnique();
        b.HasOne(x => x.VoucherSeries).WithMany().HasForeignKey(x => new { x.CompanyId, x.VoucherSeriesId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountingCommerceEventReceiptConfiguration : IEntityTypeConfiguration<AccountingCommerceEventReceipt>
{
    public void Configure(EntityTypeBuilder<AccountingCommerceEventReceipt> b)
    {
        b.ToTable("accounting_commerce_event_receipts"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.EventId).HasColumnName("event_id"); b.Property(x => x.EventVersion).HasColumnName("event_version");
        b.Property(x => x.ContractVersion).HasColumnName("contract_version").HasMaxLength(32); b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64);
        b.Property(x => x.SourceSystem).HasColumnName("source_system").HasMaxLength(64); b.Property(x => x.OccurredUtc).HasColumnName("occurred_utc");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.ReceivedUtc).HasColumnName("received_utc");
        b.HasIndex(x => new { x.CompanyId, x.EventId, x.EventVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.EventType, x.ReceivedUtc });
    }
}
