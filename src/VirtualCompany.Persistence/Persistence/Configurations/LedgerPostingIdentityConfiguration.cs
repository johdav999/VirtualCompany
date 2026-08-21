using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class LedgerPostingIdentityConfiguration : IEntityTypeConfiguration<LedgerPostingIdentity>
{
    public void Configure(EntityTypeBuilder<LedgerPostingIdentity> builder)
    {
        builder.ToTable("ledger_posting_identities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Action, x.SourceType, x.SourceId, x.SourceVersion }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.LedgerEntry).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.LedgerEntryId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
