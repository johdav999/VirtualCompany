using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class AccountingConfigurationAccountRoleConfiguration : IEntityTypeConfiguration<AccountingConfigurationAccountRole>
{
    public void Configure(EntityTypeBuilder<AccountingConfigurationAccountRole> builder)
    {
        builder.ToTable("accounting_configuration_account_roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasColumnName("id");
        builder.Property(role => role.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(role => role.AccountingConfigurationId).HasColumnName("accounting_configuration_id").IsRequired();
        builder.Property(role => role.RoleKey).HasColumnName("role_key").HasMaxLength(96).IsRequired();
        builder.Property(role => role.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(role => role.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(role => role.UpdatedUtc).HasColumnName("updated_utc").IsRequired();

        builder.HasIndex(role => new { role.CompanyId, role.AccountingConfigurationId, role.RoleKey }).IsUnique();
        builder.HasIndex(role => new { role.CompanyId, role.FinanceAccountId });
        builder.HasOne(role => role.FinanceAccount)
            .WithMany()
            .HasForeignKey(role => new { role.CompanyId, role.FinanceAccountId })
            .HasPrincipalKey(account => new { account.CompanyId, account.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
