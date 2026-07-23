using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class AgentScheduledTriggerConfiguration : IEntityTypeConfiguration<AgentScheduledTrigger>
{
    public void Configure(EntityTypeBuilder<AgentScheduledTrigger> builder)
    {
        builder.ToTable("agent_scheduled_triggers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("AK_agent_scheduled_triggers_company_id_id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CronExpression).HasColumnName("cron_expression").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TimeZoneId).HasColumnName("timezone").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.NextRunUtc).HasColumnName("next_run_at");
        builder.Property(x => x.EnabledUtc).HasColumnName("enabled_at");
        builder.Property(x => x.LastEvaluatedUtc).HasColumnName("last_evaluated_at");
        builder.Property(x => x.LastEnqueuedUtc).HasColumnName("last_enqueued_at");
        builder.Property(x => x.LastRunUtc).HasColumnName("last_run_at");
        builder.Property(x => x.DisabledUtc).HasColumnName("disabled_at");
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.AgentId });
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsEnabled, x.NextRunUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.IsEnabled });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.EnqueueWindows)
            .WithOne(x => x.ScheduledTrigger)
            .HasForeignKey(x => new { x.CompanyId, x.ScheduledTriggerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

