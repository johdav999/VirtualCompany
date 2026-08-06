using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class FinanceIntegrationProviderConfigurationConfiguration
    : IEntityTypeConfiguration<FinanceIntegrationProviderConfiguration>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationProviderConfiguration> builder)
    {
        builder.ToTable("finance_integration_provider_configurations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(x => x.RedirectUri).HasColumnName("redirect_uri").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ScopesJson).HasColumnName("scopes_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CredentialSecretName).HasColumnName("credential_secret_name").HasMaxLength(256);
        builder.Property(x => x.CredentialSecretVersion).HasColumnName("credential_secret_version").HasMaxLength(256);
        builder.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ValidationSummary).HasColumnName("validation_summary").HasMaxLength(1000);
        builder.Property(x => x.LastValidatedUtc).HasColumnName("last_validated_utc");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.HasIndex(x => x.ProviderKey).IsUnique();
    }
}
