using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class OperatingEventConfiguration : IEntityTypeConfiguration<OperatingEvent>
{
    public void Configure(EntityTypeBuilder<OperatingEvent> b)
    {
        b.ToTable("operating_events"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100);
        b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(100);
        b.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(200);
        b.Property(x => x.SourceVersion).HasColumnName("source_version"); b.Property(x => x.ObservedUtc).HasColumnName("observed_at");
        b.Property(x => x.Materiality).HasColumnName("materiality").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => OperatingEventMaterialityValues.Parse(x));
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasConversion(x => x.ToStorageValue(), x => OperatingEventStatusValues.Parse(x));
        b.Property(x => x.DeduplicationKey).HasColumnName("deduplication_key").HasMaxLength(200);
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.AffectedGoalId).HasColumnName("affected_goal_id");
        b.Property(x => x.Payload).HasColumnName("payload_json").HasJsonConversion<Dictionary<string, System.Text.Json.Nodes.JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault);
        b.Property(x => x.SuppressionReason).HasColumnName("suppression_reason").HasMaxLength(500);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.ProcessedUtc).HasColumnName("processed_at");
        b.HasIndex(x => new { x.CompanyId, x.DeduplicationKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.Materiality, x.ObservedUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AffectedGoal).WithMany().HasForeignKey(x => x.AffectedGoalId).OnDelete(DeleteBehavior.NoAction);
    }
}
