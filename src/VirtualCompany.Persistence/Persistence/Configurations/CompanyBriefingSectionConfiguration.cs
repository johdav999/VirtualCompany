using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyBriefingSectionConfiguration : IEntityTypeConfiguration<CompanyBriefingSection>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingSection> builder)
    {
        builder.ToTable("company_briefing_sections");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BriefingId).HasColumnName("briefing_id").IsRequired();
        builder.Property(x => x.SectionKey).HasColumnName("section_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.GroupingType).HasColumnName("grouping_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.GroupingKey).HasColumnName("grouping_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CompanyEntityId).HasColumnName("company_entity_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.SectionType).HasColumnName("section_type").HasMaxLength(64).HasDefaultValue("informational").IsRequired();
        builder.Property(x => x.PriorityCategory)
            .HasColumnName("priority_category")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSectionPriorityCategoryValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BriefingSectionPriorityCategory.Informational)
            .HasSentinel((BriefingSectionPriorityCategory)0)
            .IsRequired();
        builder.Property(x => x.PriorityScore).HasColumnName("priority_score").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.PriorityRuleCode).HasColumnName("priority_rule_code").HasMaxLength(100);
        builder.Property(x => x.EventCorrelationId).HasColumnName("event_correlation_id").HasMaxLength(128);
        builder.Property(x => x.Narrative).HasColumnName("narrative").IsRequired();
        builder.Property(x => x.IsConflicting).HasColumnName("is_conflicting").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ConflictSummary).HasColumnName("conflict_summary").HasMaxLength(2000);
        builder.Property(x => x.SourceReferences)
            .HasColumnName("source_refs_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BriefingId });
        builder.HasIndex(x => new { x.BriefingId, x.SectionKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.GroupingType, x.GroupingKey });
        builder.HasIndex(x => new { x.CompanyId, x.CompanyEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.EventCorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.PriorityScore, x.SectionKey });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Briefing)
            .WithMany(x => x.Sections)
            .HasForeignKey(x => x.BriefingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

