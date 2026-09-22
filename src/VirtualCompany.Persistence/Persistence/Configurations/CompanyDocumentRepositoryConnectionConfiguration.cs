using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyDocumentRepositoryConnectionConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryConnection>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryConnection> builder)
    {
        builder.ToTable("company_document_repository_connections");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProviderKind).HasColumnName("provider_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DirectoryTenantId).HasColumnName("directory_tenant_id").IsRequired();
        builder.Property(x => x.ApplicationClientId).HasColumnName("application_client_id").IsRequired();
        builder.Property(x => x.CredentialReference).HasColumnName("credential_reference").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CredentialMode).HasColumnName("credential_mode").HasMaxLength(32).HasDefaultValue(DocumentRepositoryCredentialModes.CustomerManaged).IsRequired();
        builder.Property(x => x.DriveId).HasColumnName("drive_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.RootItemId).HasColumnName("root_item_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.IsReadOnly).HasColumnName("is_read_only").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.WritableFolderItemId).HasColumnName("writable_folder_item_id").HasMaxLength(160);
        builder.Property(x => x.LifecycleState).HasColumnName("lifecycle_state").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Audience).HasColumnName("audience").HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastValidationCode).HasColumnName("last_validation_code").HasMaxLength(64);
        builder.Property(x => x.LastValidationSummary).HasColumnName("last_validation_summary").HasMaxLength(500);
        builder.Property(x => x.LastValidatedUtc).HasColumnName("last_validated_at");
        builder.Property(x => x.DisconnectedUtc).HasColumnName("disconnected_at");
        builder.Property(x => x.SynchronizationCursor).HasColumnName("synchronization_cursor").HasMaxLength(4096);
        builder.Property(x => x.SynchronizationMode).HasColumnName("synchronization_mode").HasMaxLength(32);
        builder.Property(x => x.LastSynchronizedUtc).HasColumnName("last_synchronized_at");
        builder.Property(x => x.RetrievalPausedUtc).HasColumnName("retrieval_paused_at");
        builder.Property(x => x.SynchronizationPausedUtc).HasColumnName("synchronization_paused_at");
        builder.Property(x => x.WritesPausedUtc).HasColumnName("writes_paused_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKind, x.DirectoryTenantId, x.DriveId, x.RootItemId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.LifecycleState });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyDocumentRepositoryOnboardingSessionConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryOnboardingSession>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryOnboardingSession> builder)
    {
        builder.ToTable("company_document_repository_onboarding_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.InitiatingUserId).HasColumnName("initiating_user_id").IsRequired();
        builder.Property(x => x.SessionHandleHash).HasColumnName("session_handle_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StateHash).HasColumnName("state_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProtectedSetupMaterial).HasColumnName("protected_setup_material").HasMaxLength(12000);
        builder.Property(x => x.ReturnPath).HasColumnName("return_path").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderTenantId).HasColumnName("provider_tenant_id");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(500);
        builder.Property(x => x.CallbackReceivedUtc).HasColumnName("callback_received_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CancelledUtc).HasColumnName("cancelled_at");
        builder.Property(x => x.ExpiredUtc).HasColumnName("expired_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken().IsRequired();
        builder.HasIndex(x => x.SessionHandleHash).IsUnique();
        builder.HasIndex(x => x.StateHash).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.InitiatingUserId, x.Status });
        builder.HasIndex(x => new { x.Status, x.ExpiresUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyDocumentRepositoryAgentGrantConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryAgentGrant>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryAgentGrant> builder)
    {
        builder.ToTable("company_document_repository_agent_grants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.AgentId }).IsUnique();
        builder.HasOne(x => x.Connection).WithMany(x => x.AgentGrants)
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class CompanyDocumentRepositoryProvisioningConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryProvisioning>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryProvisioning> builder)
    {
        builder.ToTable("company_document_repository_provisionings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.OnboardingSessionId).HasColumnName("onboarding_session_id").IsRequired();
        builder.Property(x => x.InitiatingUserId).HasColumnName("initiating_user_id").IsRequired();
        builder.Property(x => x.ProviderKind).HasColumnName("provider_kind").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DirectoryTenantId).HasColumnName("directory_tenant_id").IsRequired();
        builder.Property(x => x.SiteId).HasColumnName("site_id").HasMaxLength(256);
        builder.Property(x => x.DriveId).HasColumnName("drive_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.RootItemId).HasColumnName("root_item_id").HasMaxLength(160).IsRequired();
        builder.Property(x => x.SourceDisplayName).HasColumnName("source_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.RootDisplayName).HasColumnName("root_display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.EnableWrites).HasColumnName("enable_writes").IsRequired();
        builder.Property(x => x.WritableFolderItemId).HasColumnName("writable_folder_item_id").HasMaxLength(160);
        builder.Property(x => x.WritableFolderDisplayName).HasColumnName("writable_folder_display_name").HasMaxLength(200);
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        builder.Property(x => x.RootPermissionId).HasColumnName("root_permission_id").HasMaxLength(256);
        builder.Property(x => x.RootPermissionManaged).HasColumnName("root_permission_managed").IsRequired();
        builder.Property(x => x.WritePermissionId).HasColumnName("write_permission_id").HasMaxLength(256);
        builder.Property(x => x.WritePermissionManaged).HasColumnName("write_permission_managed").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(500);
        builder.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => x.OnboardingSessionId).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextAttemptUtc });
        builder.HasIndex(x => x.ConnectionId).IsUnique().HasFilter("[connection_id] IS NOT NULL");
        builder.HasOne(x => x.OnboardingSession).WithMany().HasForeignKey(x => x.OnboardingSessionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyDocumentRepositoryProvisioningAgentConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryProvisioningAgent>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryProvisioningAgent> builder)
    {
        builder.ToTable("company_document_repository_provisioning_agents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProvisioningId).HasColumnName("provisioning_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ProvisioningId, x.AgentId }).IsUnique();
        builder.HasOne(x => x.Provisioning).WithMany(x => x.Agents).HasForeignKey(x => x.ProvisioningId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(x => new { x.CompanyId, x.AgentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
