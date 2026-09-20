using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyDocumentPublicationRequestConfiguration : IEntityTypeConfiguration<CompanyDocumentPublicationRequest>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentPublicationRequest> builder)
    {
        builder.ToTable("company_document_publication_requests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id").IsRequired();
        builder.Property(x => x.TargetFolderItemId).HasColumnName("target_folder_item_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(160);
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.ContentSha256).HasColumnName("content_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        builder.Property(x => x.RequestingAgentId).HasColumnName("requesting_agent_id").IsRequired();
        builder.Property(x => x.RequestingActorType).HasColumnName("requesting_actor_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.RequestingActorId).HasColumnName("requesting_actor_id").IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OperationKind).HasColumnName("operation_kind").HasMaxLength(16).HasDefaultValue(DocumentPublicationOperationKinds.Create).IsRequired();
        builder.Property(x => x.TargetItemId).HasColumnName("target_item_id").HasMaxLength(200);
        builder.Property(x => x.ExpectedRemoteVersion).HasColumnName("expected_remote_version").HasMaxLength(512);
        builder.Property(x => x.OriginalEvidenceVersion).HasColumnName("original_evidence_version").HasMaxLength(512);
        builder.Property(x => x.OriginalSizeBytes).HasColumnName("original_size_bytes");
        builder.Property(x => x.OriginalContentSha256).HasColumnName("original_content_sha256").HasMaxLength(64);
        builder.Property(x => x.OriginalStorageKey).HasColumnName("original_storage_key").HasMaxLength(512);
        builder.Property(x => x.StalePublicationRequestId).HasColumnName("stale_publication_request_id");
        builder.Property(x => x.ConflictRemoteVersion).HasColumnName("conflict_remote_version").HasMaxLength(512);
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.ToolExecutionAttemptId).HasColumnName("tool_execution_attempt_id");
        builder.Property(x => x.PolicyDecisionJson).HasColumnName("policy_decision_json").HasColumnType("nvarchar(max)");
        builder.Property(x => x.ApprovalVersionUtc).HasColumnName("approval_version_at");
        builder.Property(x => x.ProviderItemId).HasColumnName("provider_item_id").HasMaxLength(200);
        builder.Property(x => x.ProviderVersion).HasColumnName("provider_version").HasMaxLength(512);
        builder.Property(x => x.SourceWebUrl).HasColumnName("source_web_url").HasMaxLength(2048);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.LocalArtifactsPurgedUtc).HasColumnName("local_artifacts_purged_at");
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ToolExecutionAttemptId }).IsUnique()
            .HasFilter("[tool_execution_attempt_id] IS NOT NULL");
        builder.HasOne(x => x.Connection).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
