using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingConfigurationConfiguration : IEntityTypeConfiguration<AccountingConfiguration>
{
    public void Configure(EntityTypeBuilder<AccountingConfiguration> builder)
    {
        builder.ToTable("accounting_configurations", table =>
        {
            table.HasCheckConstraint("CK_accounting_configurations_fiscal_year_start_month", "[fiscal_year_start_month] >= 1 AND [fiscal_year_start_month] <= 12");
            table.HasCheckConstraint("CK_accounting_configurations_fiscal_year_start_day", "[fiscal_year_start_day] >= 1 AND [fiscal_year_start_day] <= 31");
            table.HasCheckConstraint("CK_accounting_configurations_rounding_precision", "[rounding_precision] >= 0 AND [rounding_precision] <= 6");
            table.HasCheckConstraint("CK_accounting_configurations_authority", "[authority] IN ('internal_ledger', 'external_provider', 'migration')");
            table.HasCheckConstraint("CK_accounting_configurations_setup_state", "[setup_state] IN ('incomplete', 'ready')");
        });

        builder.HasKey(configuration => configuration.Id);
        builder.HasAlternateKey(configuration => new { configuration.CompanyId, configuration.Id });
        builder.Property(configuration => configuration.Id).HasColumnName("id");
        builder.Property(configuration => configuration.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(configuration => configuration.BaseCurrency).HasColumnName("base_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(configuration => configuration.FiscalYearStartMonth).HasColumnName("fiscal_year_start_month").IsRequired();
        builder.Property(configuration => configuration.FiscalYearStartDay).HasColumnName("fiscal_year_start_day").IsRequired();
        builder.Property(configuration => configuration.Authority).HasColumnName("authority").HasMaxLength(32).IsRequired();
        builder.Property(configuration => configuration.SetupState).HasColumnName("setup_state").HasMaxLength(32).IsRequired();
        builder.Property(configuration => configuration.PolicyPackKey).HasColumnName("policy_pack_key").HasMaxLength(96).IsRequired();
        builder.Property(configuration => configuration.PolicyPackVersion).HasColumnName("policy_pack_version").HasMaxLength(32).IsRequired();
        builder.Property(configuration => configuration.PolicyPackEffectiveFrom).HasColumnName("policy_pack_effective_from").HasColumnType("date").IsRequired();
        builder.Property(configuration => configuration.RoundingPrecision).HasColumnName("rounding_precision").IsRequired();
        builder.Property(configuration => configuration.RoundingMode).HasColumnName("rounding_mode").HasMaxLength(32).IsRequired();
        builder.Property(configuration => configuration.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(configuration => configuration.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(configuration => configuration.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(configuration => configuration.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(configuration => configuration.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(configuration => configuration.CompanyId).IsUnique();
        builder.HasIndex(configuration => new { configuration.CompanyId, configuration.PolicyPackKey, configuration.PolicyPackVersion });
        builder.HasOne(configuration => configuration.Company)
            .WithMany()
            .HasForeignKey(configuration => configuration.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(configuration => configuration.AccountRoles)
            .WithOne(role => role.AccountingConfiguration)
            .HasForeignKey(role => new { role.CompanyId, role.AccountingConfigurationId })
            .HasPrincipalKey(configuration => new { configuration.CompanyId, configuration.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(configuration => configuration.PolicyPackSelections)
            .WithOne(selection => selection.AccountingConfiguration)
            .HasForeignKey(selection => new { selection.CompanyId, selection.AccountingConfigurationId })
            .HasPrincipalKey(configuration => new { configuration.CompanyId, configuration.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
