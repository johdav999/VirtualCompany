using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingPolicyPackSelectionConfiguration : IEntityTypeConfiguration<AccountingPolicyPackSelection>
{
    public void Configure(EntityTypeBuilder<AccountingPolicyPackSelection> builder)
    {
        builder.ToTable("accounting_policy_pack_selections");
        builder.HasKey(selection => selection.Id);
        builder.Property(selection => selection.Id).HasColumnName("id");
        builder.Property(selection => selection.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(selection => selection.AccountingConfigurationId).HasColumnName("accounting_configuration_id").IsRequired();
        builder.Property(selection => selection.PackKey).HasColumnName("pack_key").HasMaxLength(96).IsRequired();
        builder.Property(selection => selection.PackVersion).HasColumnName("pack_version").HasMaxLength(32).IsRequired();
        builder.Property(selection => selection.DefinitionHash).HasColumnName("definition_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(selection => selection.IsStatutoryComplianceValidated).HasColumnName("is_statutory_compliance_validated").IsRequired();
        builder.Property(selection => selection.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date").IsRequired();
        builder.Property(selection => selection.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        builder.Property(selection => selection.SelectedByUserId).HasColumnName("selected_by_user_id").IsRequired();
        builder.Property(selection => selection.SelectedUtc).HasColumnName("selected_utc").IsRequired();

        builder.HasIndex(selection => new { selection.CompanyId, selection.AccountingConfigurationId, selection.EffectiveFrom }).IsUnique();
        builder.HasIndex(selection => new { selection.CompanyId, selection.EffectiveTo })
            .IsUnique()
            .HasFilter("[effective_to] IS NULL");
        builder.HasIndex(selection => new { selection.CompanyId, selection.PackKey, selection.PackVersion });
    }
}
