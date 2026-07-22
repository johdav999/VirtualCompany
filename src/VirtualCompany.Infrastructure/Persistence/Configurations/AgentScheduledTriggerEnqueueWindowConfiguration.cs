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
internal sealed class AgentScheduledTriggerEnqueueWindowConfiguration : IEntityTypeConfiguration<AgentScheduledTriggerEnqueueWindow>
{
    public void Configure(EntityTypeBuilder<AgentScheduledTriggerEnqueueWindow> builder)
    {
        builder.ToTable("agent_scheduled_trigger_enqueue_windows");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ScheduledTriggerId).HasColumnName("scheduled_trigger_id").IsRequired();
        builder.Property(x => x.WindowStartUtc).HasColumnName("window_start_at").IsRequired();
        builder.Property(x => x.WindowEndUtc).HasColumnName("window_end_at").IsRequired();
        builder.Property(x => x.EnqueuedUtc).HasColumnName("enqueued_at").IsRequired();
        builder.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ScheduledTriggerId, x.WindowStartUtc, x.WindowEndUtc }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.EnqueuedUtc });
        builder.HasIndex(x => x.ExecutionRequestId)
            .HasFilter("execution_request_id IS NOT NULL");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_agent_scheduled_trigger_enqueue_windows_window_order",
            ActiveProviderConstraint.WindowEndAfterStart));

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ScheduledTrigger)
            .WithMany(x => x.EnqueueWindows)
            .HasForeignKey(x => new { x.CompanyId, x.ScheduledTriggerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

