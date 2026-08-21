using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingAuthorityPeriodConfiguration : IEntityTypeConfiguration<AccountingAuthorityPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingAuthorityPeriod> builder)
    {
        builder.ToTable("accounting_authority_periods", table =>
        {
            table.HasCheckConstraint(
                "CK_accounting_authority_periods_authority",
                "[authority] IN ('internal_ledger', 'external_provider', 'migration')");
            table.HasCheckConstraint(
                "CK_accounting_authority_periods_target_authority",
                "[target_authority] IS NULL OR [target_authority] IN ('internal_ledger', 'external_provider')");
            table.HasCheckConstraint(
                "CK_accounting_authority_periods_dates",
                "[effective_to] IS NULL OR [effective_to] >= [effective_from]");
            table.HasCheckConstraint(
                "CK_accounting_authority_periods_conflicts",
                "[conflict_count] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        builder.Property(x => x.Authority).HasColumnName("authority").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetAuthority).HasColumnName("target_authority").HasMaxLength(32);
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64);
        builder.Property(x => x.ChangeReason).HasColumnName("change_reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.OpeningBalancesReconciled).HasColumnName("opening_balances_reconciled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.TrialBalanceReconciled).HasColumnName("trial_balance_reconciled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.SourceMappingsReconciled).HasColumnName("source_mappings_reconciled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ConflictCount).HasColumnName("conflict_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ValidationSummary).HasColumnName("validation_summary").HasMaxLength(1000);
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id").IsRequired();
        builder.Property(x => x.CompletedByUserId).HasColumnName("completed_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Authority, x.EffectiveTo });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.EffectiveFrom });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
