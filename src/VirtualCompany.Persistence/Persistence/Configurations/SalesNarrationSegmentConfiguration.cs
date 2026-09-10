using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesNarrationSegmentConfiguration : IEntityTypeConfiguration<SalesNarrationSegment>
{
    public void Configure(EntityTypeBuilder<SalesNarrationSegment> b)
    {
        b.ToTable("sales_narration_segments");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.RevisionId, x.SlideNumber, x.TalkingPoint }).IsUnique();
        b.Property(x => x.SourceHash).HasMaxLength(64);
        b.Property(x => x.SourceText).HasMaxLength(16000);
        b.Property(x => x.Script).HasMaxLength(3000);
        b.Property(x => x.ScriptHash).HasMaxLength(64);
        b.HasOne<SalesNarrationRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.RevisionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesNarrationAsset>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

