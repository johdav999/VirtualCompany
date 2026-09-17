using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationPresetConfiguration : IEntityTypeConfiguration<SalesPresentationPreset>
{
    public void Configure(EntityTypeBuilder<SalesPresentationPreset> b)
    {
        b.ToTable("sales_presentation_presets"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.Lifecycle).HasColumnName("lifecycle").HasConversion(x => x.ToStorageValue(), x => SalesPresentationPresetEnumValues.ParsePresetLifecycle(x)).HasMaxLength(20);
        b.Property(x => x.CurrentPublishedVersionId).HasColumnName("current_published_version_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.ArchivedUtc).HasColumnName("archived_at");
        b.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").HasDefaultValue(1L).IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.Lifecycle, x.UpdatedUtc }); b.HasIndex(x => new { x.CompanyId, x.Name });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CurrentPublishedVersion).WithMany().HasForeignKey(nameof(SalesPresentationPreset.CompanyId), nameof(SalesPresentationPreset.CurrentPublishedVersionId)).HasPrincipalKey(nameof(SalesPresentationPresetVersion.CompanyId), nameof(SalesPresentationPresetVersion.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
