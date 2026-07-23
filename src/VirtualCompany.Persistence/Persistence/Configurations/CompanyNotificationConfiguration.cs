using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyNotificationConfiguration : IEntityTypeConfiguration<CompanyNotification>
{
    private static CompanyNotificationType ParseCompanyNotificationType(string value) =>
        value switch
        {
            "approval_requested" => CompanyNotificationType.ApprovalRequested,
            "escalation" => CompanyNotificationType.Escalation,
            "workflow_failure" => CompanyNotificationType.WorkflowFailure,
            "briefing_available" => CompanyNotificationType.BriefingAvailable,
            "proactive_message" => CompanyNotificationType.ProactiveMessage,
            _ => CompanyNotificationType.BriefingAvailable
        };

    public void Configure(EntityTypeBuilder<CompanyNotification> builder)
    {
        builder.ToTable("company_notifications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.BriefingId).HasColumnName("briefing_id");
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(value => value.ToStorageValue(), value => CompanyNotificationChannelValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(
                value => value.ToStorageValue(),
                value => CompanyNotificationStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Type)
            .HasColumnName("notification_type")
            .HasConversion(value => value.ToStorageValue(), value => ParseCompanyNotificationType(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion(value => value.ToStorageValue(), value => CompanyNotificationPriorityValues.Parse(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(x => x.ActionUrl).HasColumnName("action_url").HasMaxLength(2048);
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ReadUtc).HasColumnName("read_at");
        builder.Property(x => x.ActionedUtc).HasColumnName("actioned_at");
        builder.Property(x => x.ActionedByUserId).HasColumnName("actioned_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Type, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Priority, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BriefingId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Briefing).WithMany().HasForeignKey(x => x.BriefingId).OnDelete(DeleteBehavior.Restrict);
    }
}

