using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion(value => value.ToStorageValue(), value => AlertTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasConversion(value => value.ToStorageValue(), value => AlertSeverityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Evidence)
            .HasColumnName("evidence_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => AlertStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(AlertStatusValues.DefaultStatus)
            .HasSentinel((AlertStatus)0)
            .IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(256).IsRequired();
        builder.Property(x => x.SourceAgentId).HasColumnName("source_agent_id");
        builder.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.SourceLifecycleVersion).HasColumnName("source_lifecycle_version").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.LastDetectedUtc).HasColumnName("last_detected_at");
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        builder.Property(x => x.ClosedUtc).HasColumnName("closed_at");

        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Type, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Severity, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Fingerprint });
        builder.HasIndex(x => new { x.CompanyId, x.Fingerprint })
            .HasFilter("\"status\" IN ('open', 'acknowledged')")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SourceAgent).WithMany().HasForeignKey(x => x.SourceAgentId).OnDelete(DeleteBehavior.NoAction);
    }
}

