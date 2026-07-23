using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.SourceMetadata)
            .HasColumnName("source_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(100);
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.AuditEventId).HasColumnName("audit_event_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.OccurredUtc, x.Id }).IsDescending(false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.Department, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.TaskId, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.AuditEventId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId, x.OccurredUtc, x.Id });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

