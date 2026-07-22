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
internal sealed class ToolExecutionAttemptConfiguration : IEntityTypeConfiguration<ToolExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<ToolExecutionAttempt> builder)
    {
        builder.ToTable("tool_executions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ToolVersion).HasColumnName("tool_version").HasMaxLength(32).HasDefaultValue("1.0.0").IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.ActionType)
            .HasColumnName("action_type")
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => ToolExecutionStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestPayload)
            .HasColumnName("request_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ResultPayload)
            .HasColumnName("response_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.DenialReason).HasColumnName("denial_reason").HasMaxLength(512);
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at");
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.StartedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

