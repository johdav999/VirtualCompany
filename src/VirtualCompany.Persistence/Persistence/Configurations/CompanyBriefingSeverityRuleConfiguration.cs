using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyBriefingSeverityRuleConfiguration : IEntityTypeConfiguration<CompanyBriefingSeverityRule>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingSeverityRule> builder)
    {
        builder.ToTable("company_briefing_severity_rules");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RuleCode).HasColumnName("rule_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SectionType).HasColumnName("section_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ConditionKey).HasColumnName("condition_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConditionValue).HasColumnName("condition_value").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriorityCategory)
            .HasColumnName("priority_category")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSectionPriorityCategoryValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.PriorityScore).HasColumnName("priority_score").IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSeverityRuleStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BriefingSeverityRuleStatus.Active)
            .HasSentinel((BriefingSeverityRuleStatus)0)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.RuleCode }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.SectionType, x.EntityType, x.ConditionKey, x.ConditionValue });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

