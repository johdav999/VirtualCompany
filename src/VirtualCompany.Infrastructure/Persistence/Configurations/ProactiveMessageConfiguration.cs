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
internal sealed class ProactiveMessageConfiguration : IEntityTypeConfiguration<ProactiveMessage>
{
    public void Configure(EntityTypeBuilder<ProactiveMessage> builder)
    {
        builder.ToTable("proactive_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageChannelValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.SourceEntityType)
            .HasColumnName("source_entity_type")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageSourceEntityTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.OriginatingAgentId).HasColumnName("originating_agent_id").IsRequired();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => value == "delivered" ? ProactiveMessageDeliveryStatus.Delivered : ProactiveMessageDeliveryStatus.Blocked)
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SentUtc).HasColumnName("sent_at").IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecisionReason).HasColumnName("policy_decision_reason").HasMaxLength(200);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.RecipientUserId, x.SentUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Channel, x.SentUtc });
        builder.HasIndex(x => x.NotificationId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RecipientUser).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginatingAgent).WithMany().HasForeignKey(x => x.OriginatingAgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.NoAction);
    }
}

