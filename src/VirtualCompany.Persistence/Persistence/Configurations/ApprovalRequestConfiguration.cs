using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ToolExecutionAttemptId);
        builder.Property(x => x.RequestedByUserId).IsRequired();
        builder.Property(x => x.TargetEntityType).HasColumnName("entity_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetEntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.RequestedByActorType).HasColumnName("requested_by_actor_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequestedByActorId).HasColumnName("requested_by_actor_id").IsRequired();
        builder.Property(x => x.ApprovalType).HasColumnName("approval_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActionType)
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RequiredRole).HasColumnName("required_role").HasMaxLength(100);
        builder.Property(x => x.RequiredUserId).HasColumnName("required_user_id");
        builder.Property(x => x.DecisionSummary).HasColumnName("decision_summary").HasMaxLength(2000);
        builder.Property(x => x.ApprovalTarget).HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => ApprovalRequestStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ThresholdContext)
            .HasColumnName("threshold_context_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.DecisionChain)
            .HasColumnName("decision_chain_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.HasMany(x => x.Steps)
            .WithOne(x => x.Approval)
            .HasForeignKey(x => x.ApprovalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TargetEntityType, x.TargetEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => x.ToolExecutionAttemptId).IsUnique().HasFilter("[ToolExecutionAttemptId] IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

