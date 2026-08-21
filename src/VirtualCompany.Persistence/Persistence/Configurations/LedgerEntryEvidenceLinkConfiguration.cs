using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class LedgerEntryEvidenceLinkConfiguration : IEntityTypeConfiguration<LedgerEntryEvidenceLink>
{
    public void Configure(EntityTypeBuilder<LedgerEntryEvidenceLink> builder)
    {
        builder.ToTable("ledger_entry_evidence_links");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId, x.DocumentId }).IsUnique();
        builder.HasOne(x => x.LedgerEntry).WithMany(x => x.EvidenceLinks)
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Document).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.DocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
