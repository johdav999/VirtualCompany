using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CompanyDocumentRepositorySynchronizationJobConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositorySynchronizationJob>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositorySynchronizationJob> b)
    {
        b.ToTable("company_document_repository_sync_jobs"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.IdempotencyKey).HasMaxLength(200); b.Property(x => x.CorrelationId).HasMaxLength(128); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.Mode).HasMaxLength(32);
        b.Property(x => x.PageCursor).HasMaxLength(4096); b.Property(x => x.PendingDeltaCursor).HasMaxLength(4096); b.Property(x => x.LeaseOwner).HasMaxLength(128); b.Property(x => x.FailureCode).HasMaxLength(100); b.Property(x => x.FailureMessage).HasMaxLength(1000);
        b.Property(x => x.UpdatedUtc).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.Status, x.NextRetryUtc, x.CreatedUtc });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyDocumentRepositoryTrackedItemConfiguration : IEntityTypeConfiguration<CompanyDocumentRepositoryTrackedItem>
{
    public void Configure(EntityTypeBuilder<CompanyDocumentRepositoryTrackedItem> b)
    {
        b.ToTable("company_document_repository_tracked_items"); b.HasKey(x => x.Id);
        b.Property(x => x.DriveId).HasMaxLength(160); b.Property(x => x.ItemId).HasMaxLength(160); b.Property(x => x.ParentItemId).HasMaxLength(160); b.Property(x => x.Name).HasMaxLength(255); b.Property(x => x.RemoteVersion).HasMaxLength(256); b.Property(x => x.UnavailableReason).HasMaxLength(100);
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.DriveId, x.ItemId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.ParentItemId }); b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.LastSeenGeneration });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
