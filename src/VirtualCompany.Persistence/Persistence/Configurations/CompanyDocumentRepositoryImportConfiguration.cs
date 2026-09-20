using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyDocumentRepositoryImportJobConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryImportJob>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryImportJob> b)
    {
        b.ToTable("company_document_repository_import_jobs"); b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id");
        b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.DiscoveredCount).HasColumnName("discovered_count"); b.Property(x => x.ProcessedCount).HasColumnName("processed_count"); b.Property(x => x.FailedCount).HasColumnName("failed_count");
        b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(1000);
        b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsConcurrencyToken(); b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.Status, x.CreatedUtc });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyDocumentRepositoryImportItemConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryImportItem>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryImportItem> b)
    {
        b.ToTable("company_document_repository_import_items"); b.HasKey(x => x.Id);
        b.Property(x => x.DriveId).HasMaxLength(160); b.Property(x => x.ItemId).HasMaxLength(160); b.Property(x => x.Name).HasMaxLength(255); b.Property(x => x.RemoteVersion).HasMaxLength(256); b.Property(x => x.SourceWebUrl).HasMaxLength(2048); b.Property(x => x.ContentType).HasMaxLength(255); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.FailureCode).HasMaxLength(100); b.Property(x => x.FailureMessage).HasMaxLength(1000);
        b.HasIndex(x => new { x.CompanyId, x.JobId, x.DriveId, x.ItemId, x.RemoteVersion }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.JobId, x.Status });
        b.HasOne(x => x.Job).WithMany(x => x.Items).HasForeignKey(x => new { x.CompanyId, x.JobId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
    }
}

internal sealed class CompanyKnowledgeDocumentRemoteSourceConfiguration : IEntityTypeConfiguration<CompanyKnowledgeDocumentRemoteSource>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeDocumentRemoteSource> b)
    {
        b.ToTable("knowledge_document_remote_sources"); b.HasKey(x => x.Id);
        b.Property(x => x.DriveId).HasMaxLength(160); b.Property(x => x.ItemId).HasMaxLength(160); b.Property(x => x.RemoteVersion).HasMaxLength(256); b.Property(x => x.SourceWebUrl).HasMaxLength(2048); b.Property(x => x.ContentHash).HasMaxLength(64); b.Property(x => x.ImportState).HasMaxLength(32);
        b.Property(x => x.ObservedRemoteVersion).HasMaxLength(256); b.Property(x => x.UnavailableReason).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.DriveId, x.ItemId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.DocumentId }).IsUnique();
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => new { x.CompanyId, x.DocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}
