using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal sealed class MailboxFolderSyncCursorConfiguration : IEntityTypeConfiguration<MailboxFolderSyncCursor>
{
    public void Configure(EntityTypeBuilder<MailboxFolderSyncCursor> builder)
    {
        builder.ToTable("mailbox_folder_sync_cursors", table =>
            table.HasCheckConstraint(
                "CK_mailbox_folder_sync_cursors_status",
                "status IN ('active', 'reconciliation_required')"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.MailboxConnectionId).HasColumnName("mailbox_connection_id").IsRequired();
        builder.Property(x => x.FolderId).HasColumnName("folder_id").HasMaxLength(512).IsRequired();
        builder.Property(x => x.UidValidity).HasColumnName("uid_validity");
        builder.Property(x => x.LastProcessedUid).HasColumnName("last_processed_uid").HasDefaultValue(0L).IsRequired();
        builder.Property(x => x.HighestModSequence).HasColumnName("highest_mod_sequence");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => MailboxCursorStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.LastSuccessfulSyncUtc).HasColumnName("last_successful_sync_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.MailboxConnectionId, x.FolderId }).IsUnique();
        builder.HasOne(x => x.MailboxConnection)
            .WithMany(x => x.FolderSyncCursors)
            .HasForeignKey(x => new { x.CompanyId, x.MailboxConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
