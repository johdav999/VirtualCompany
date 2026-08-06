using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class FinanceIntegrationProviderConfigurationAuditConfiguration
    : IEntityTypeConfiguration<FinanceIntegrationProviderConfigurationAudit>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationProviderConfigurationAudit> builder)
    {
        builder.ToTable("finance_integration_provider_configuration_audits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ChangedFieldsJson).HasColumnName("changed_fields_json").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_utc").IsRequired();
        builder.HasIndex(x => new { x.ProviderKey, x.OccurredUtc });
    }
}
