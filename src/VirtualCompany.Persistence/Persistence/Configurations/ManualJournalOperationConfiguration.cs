using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ManualJournalOperationConfiguration : IEntityTypeConfiguration<ManualJournalOperation>
{
    public void Configure(EntityTypeBuilder<ManualJournalOperation> builder)
    {
        builder.ToTable("manual_journal_operations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResultVersion).HasColumnName("result_version").IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.DraftId, x.Action, x.ResultVersion });
        builder.HasOne(x => x.Draft).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
