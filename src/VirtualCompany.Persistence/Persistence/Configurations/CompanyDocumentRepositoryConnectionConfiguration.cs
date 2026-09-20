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
