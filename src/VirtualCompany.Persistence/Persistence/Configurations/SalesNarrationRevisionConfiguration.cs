using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesNarrationRevisionConfiguration : IEntityTypeConfiguration<SalesNarrationRevision>
{
    public void Configure(EntityTypeBuilder<SalesNarrationRevision> b)
    {
        b.ToTable("sales_narration_revisions",t=>t.HasCheckConstraint("CK_sales_narration_revision_owner",
            "([PresetVersionId] IS NOT NULL AND [SessionId] IS NULL AND [DeckId] IS NULL AND [AudienceId] IS NULL AND [SourcePresetRevisionId] IS NULL) OR ([PresetVersionId] IS NULL AND [SessionId] IS NOT NULL AND [DeckId] IS NOT NULL AND [AudienceId] IS NOT NULL)"));
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.SessionId, x.ManifestHash }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.PresetVersionId, x.ManifestHash }).IsUnique().HasFilter("[PresetVersionId] IS NOT NULL");
        b.HasOne<SalesPresentationPresetVersion>().WithMany().HasForeignKey(x => new { x.CompanyId, x.PresetVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesNarrationRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourcePresetRevisionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.AudienceHash).HasMaxLength(64);
        b.Property(x => x.ManifestHash).HasMaxLength(64);
        b.Property(x => x.Language).HasMaxLength(10);
        b.Property(x => x.Voice).HasMaxLength(100);
        b.Property(x => x.Model).HasMaxLength(100);
        b.Property(x => x.ConfigurationVersion).HasMaxLength(64);
        b.HasOne<SalesMeetingSession>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SessionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesPresentationDeck>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DeckId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

