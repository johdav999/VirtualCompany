using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ExecutionExceptionRecordConfiguration : IEntityTypeConfiguration<ExecutionExceptionRecord>
{
    public void Configure(EntityTypeBuilder<ExecutionExceptionRecord> builder)
    {
        builder.ToTable("execution_exceptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionKindValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionSeverityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(ExecutionExceptionStatusValues.DefaultStatus)
            .HasSentinel((ExecutionExceptionStatus)0)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionSourceTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.BackgroundExecutionId).HasColumnName("background_execution_id");
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100);
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id").HasMaxLength(128);
        builder.Property(x => x.IncidentKey).HasColumnName("incident_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(200);
        builder.Property(x => x.Details)
            .HasColumnName("details_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Kind, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IncidentKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId });
        builder.HasIndex(x => x.BackgroundExecutionId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.BackgroundExecution)
            .WithMany()
            .HasForeignKey(x => x.BackgroundExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

