using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceAgentInsightEntityConfiguration : IEntityTypeConfiguration<FinanceAgentInsight>
{
    public void Configure(EntityTypeBuilder<FinanceAgentInsight> builder)
    {
        builder.ToTable("finance_agent_insights");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_finance_agent_insights_severity", FinancialCheckSeverityValues.BuildCheckConstraintSql("severity"));
            t.HasCheckConstraint("CK_finance_agent_insights_status", FinanceInsightStatusValues.BuildCheckConstraintSql("status"));
            t.HasCheckConstraint("CK_finance_agent_insights_confidence", "confidence >= 0 AND confidence <= 1");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CheckCode).HasColumnName("check_code").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ConditionKey).HasColumnName("condition_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasConversion(x => x.ToStorageValue(), x => FinancialCheckSeverityValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Recommendation).HasColumnName("recommendation").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.EntityDisplayName).HasColumnName("entity_display_name").HasMaxLength(256);
        builder.Property(x => x.AffectedEntitiesJson).HasColumnName("affected_entities_json").HasColumnType("nvarchar(max)").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("nvarchar(max)").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion(x => x.ToStorageValue(), x => FinanceInsightStatusValues.Parse(x)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");

        builder.HasIndex(x => new { x.CompanyId, x.CheckCode, x.ConditionKey, x.EntityType, x.EntityId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CheckCode, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.EntityType, x.EntityId, x.Status });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}