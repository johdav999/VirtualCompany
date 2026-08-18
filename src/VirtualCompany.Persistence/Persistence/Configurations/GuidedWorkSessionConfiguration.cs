using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class GuidedWorkSessionConfiguration : IEntityTypeConfiguration<GuidedWorkSession>
{
    public void Configure(EntityTypeBuilder<GuidedWorkSession> builder)
    {
        builder.ToTable("guided_work_sessions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.ArtifactType).HasColumnName("artifact_type").HasMaxLength(96).IsRequired();
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetArtifactId).HasColumnName("target_artifact_id");
        builder.Property(x => x.TargetArtifactVersion).HasColumnName("target_artifact_version").HasMaxLength(128);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Sequence).HasColumnName("sequence").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.RequiredFieldCount).HasColumnName("required_field_count").IsRequired();
        builder.Property(x => x.ReadyFieldCount).HasColumnName("ready_field_count").IsRequired();
        builder.Property(x => x.SafeSummary).HasColumnName("safe_summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.NextQuestion).HasColumnName("next_question").HasMaxLength(1000);
        builder.Property(x => x.ReviewTokenHash).HasColumnName("review_token_hash").HasMaxLength(128);
        builder.Property(x => x.ReviewTokenExpiresUtc).HasColumnName("review_token_expires_at");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        builder.HasIndex(x => new { x.CompanyId, x.CreatedByUserId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.ArtifactType, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ConversationId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConversationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GuidedDraftFieldConfiguration : IEntityTypeConfiguration<GuidedDraftField>
{
    public void Configure(EntityTypeBuilder<GuidedDraftField> builder)
    {
        builder.ToTable("guided_draft_fields");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.Path).HasColumnName("path").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Label).HasColumnName("label").HasMaxLength(160).IsRequired();
        builder.Property(x => x.ValueType).HasColumnName("value_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.IsRequired).HasColumnName("is_required").IsRequired();
        builder.Property(x => x.ValueJson).HasColumnName("value_json");
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceMessageId).HasColumnName("source_message_id");
        builder.Property(x => x.SourceMetadata).HasColumnName("source_metadata_json").HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000);
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.Path }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Session).WithMany(x => x.Fields).HasForeignKey(x => new { x.CompanyId, x.SessionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GuidedSessionOperationConfiguration : IEntityTypeConfiguration<GuidedSessionOperation>
{
    public void Configure(EntityTypeBuilder<GuidedSessionOperation> builder)
    {
        builder.ToTable("guided_session_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.ClientRequestId).HasColumnName("client_request_id").IsRequired();
        builder.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ResponseJson).HasColumnName("response_json").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.OperationType, x.ClientRequestId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Session).WithMany(x => x.Operations).HasForeignKey(x => new { x.CompanyId, x.SessionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GuidedVoiceBindingConfiguration : IEntityTypeConfiguration<GuidedVoiceBinding>
{
    public void Configure(EntityTypeBuilder<GuidedVoiceBinding> builder)
    {
        builder.ToTable("guided_voice_bindings");builder.HasKey(x=>x.Id);builder.Property(x=>x.Id).HasColumnName("id");builder.Property(x=>x.CompanyId).HasColumnName("company_id").IsRequired();builder.Property(x=>x.SessionId).HasColumnName("session_id").IsRequired();builder.Property(x=>x.UserId).HasColumnName("user_id").IsRequired();builder.Property(x=>x.ProviderCallId).HasColumnName("provider_call_id").HasMaxLength(160).IsRequired();builder.Property(x=>x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();builder.Property(x=>x.ReconnectCount).HasColumnName("reconnect_count").IsRequired();builder.Property(x=>x.LastProviderEventId).HasColumnName("last_provider_event_id").HasMaxLength(128);builder.Property(x=>x.ExpiresUtc).HasColumnName("expires_at").IsRequired();builder.Property(x=>x.CreatedUtc).HasColumnName("created_at").IsRequired();builder.Property(x=>x.UpdatedUtc).HasColumnName("updated_at").IsRequired();builder.Property(x=>x.EndedUtc).HasColumnName("ended_at");
        builder.HasIndex(x=>x.ProviderCallId).IsUnique();builder.HasIndex(x=>new{x.CompanyId,x.SessionId,x.Status});builder.HasOne(x=>x.Company).WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.NoAction);builder.HasOne(x=>x.Session).WithMany(x=>x.VoiceBindings).HasForeignKey(x=>new{x.CompanyId,x.SessionId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Cascade);builder.HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
