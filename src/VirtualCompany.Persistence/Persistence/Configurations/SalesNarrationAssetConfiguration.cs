using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesNarrationAssetConfiguration : IEntityTypeConfiguration<SalesNarrationAsset>
{
    public void Configure(EntityTypeBuilder<SalesNarrationAsset> b)
    {
        b.ToTable("sales_narration_assets");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.CacheKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.LeaseUntilUtc });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.CacheKey).HasMaxLength(64);
        b.Property(x => x.Status).HasMaxLength(32);
        b.Property(x => x.StorageKey).HasMaxLength(1000);
        b.Property(x => x.AudioHash).HasMaxLength(64);
        b.Property(x => x.TranscriptHash).HasMaxLength(64);
        b.Property(x => x.MediaFormat).HasMaxLength(100);
        b.Property(x => x.FailureCode).HasMaxLength(100);
    }
}

