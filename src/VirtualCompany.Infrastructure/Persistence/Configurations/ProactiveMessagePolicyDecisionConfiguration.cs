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
internal sealed class ProactiveMessagePolicyDecisionConfiguration : IEntityTypeConfiguration<ProactiveMessagePolicyDecision>
{
    public void Configure(EntityTypeBuilder<ProactiveMessagePolicyDecision> builder)
    {
        builder.ToTable("proactive_message_policy_decisions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProactiveMessageId).HasColumnName("proactive_message_id");
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
        builder.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessagePolicyDecisionOutcomeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(200);
        builder.Property(x => x.ReasonSummary).HasColumnName("reason_summary").HasMaxLength(2000);
        builder.Property(x => x.EvaluatedAutonomyLevel)
            .HasColumnName("evaluated_autonomy_level")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Outcome, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Channel, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RecipientUserId, x.CreatedUtc });
        builder.HasIndex(x => x.ProactiveMessageId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProactiveMessage).WithMany().HasForeignKey(x => x.ProactiveMessageId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.RecipientUser).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginatingAgent).WithMany().HasForeignKey(x => x.OriginatingAgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

