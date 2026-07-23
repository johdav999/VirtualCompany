using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class AgentTaskCreationDedupeRecordConfiguration : IEntityTypeConfiguration<AgentTaskCreationDedupeRecord>
{
    public void Configure(EntityTypeBuilder<AgentTaskCreationDedupeRecord> builder)
    {
        builder.ToTable("agent_task_creation_dedupe");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.TriggerSource).HasColumnName("trigger_source").HasMaxLength(128).IsRequired();
        builder.Property(x => x.TriggerEventId).HasColumnName("trigger_event_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerSource, x.TriggerEventId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

