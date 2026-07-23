using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TargetId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RationaleSummary).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.OccurredUtc).IsRequired();
        builder.Property(x => x.PayloadDiffJson).HasColumnName("payload_diff_json").HasMaxLength(16000);
        builder.Property(x => x.AgentName).HasColumnName("agent_name").HasMaxLength(200);
        builder.Property(x => x.AgentRole).HasColumnName("agent_role").HasMaxLength(128);
        builder.Property(x => x.ResponsibilityDomain).HasColumnName("responsibility_domain").HasMaxLength(128);
        builder.Property(x => x.PromptProfileVersion).HasColumnName("prompt_profile_version").HasMaxLength(64);
        builder.Property(x => x.BoundaryDecisionOutcome).HasColumnName("boundary_decision_outcome").HasMaxLength(64);
        builder.Property(x => x.IdentityReasonCode).HasColumnName("identity_reason_code").HasMaxLength(128);
        builder.Property(x => x.BoundaryReasonCode).HasColumnName("boundary_reason_code").HasMaxLength(128);
        builder.Property(x => x.DataSources)
            .HasColumnName("data_sources_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.DataSourcesUsed)
            .HasColumnName("data_sources_used_json")
            .HasJsonConversion<List<AuditDataSourceUsed>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.RelatedAgentId);
        builder.Property(x => x.RelatedTaskId);
        builder.Property(x => x.RelatedWorkflowInstanceId);
        builder.Property(x => x.RelatedApprovalRequestId);
        builder.Property(x => x.RelatedToolExecutionAttemptId);

        builder.HasIndex(x => new { x.CompanyId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ActorType, x.ActorId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetType, x.TargetId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedAgentId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedAgentId, x.BoundaryDecisionOutcome, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedTaskId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedWorkflowInstanceId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedApprovalRequestId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedToolExecutionAttemptId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

