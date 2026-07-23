using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class SalesAcquisitionCampaignConfiguration : IEntityTypeConfiguration<SalesAcquisitionCampaign>
{
    public void Configure(EntityTypeBuilder<SalesAcquisitionCampaign> b) { b.ToTable("SalesAcquisitionCampaigns"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Category).HasMaxLength(64); b.Property(x => x.Provider).HasMaxLength(120); b.Property(x => x.ExternalReference).HasMaxLength(256); b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.Budget).HasPrecision(18,2); b.HasIndex(x => new { x.CompanyId, x.Name }); b.HasIndex(x => new { x.CompanyId, x.Provider, x.ExternalReference }); }
}

public sealed class SalesSourceTouchConfiguration : IEntityTypeConfiguration<SalesSourceTouch>
{
    public void Configure(EntityTypeBuilder<SalesSourceTouch> b) { b.ToTable("SalesSourceTouches"); b.HasKey(x => x.Id); b.Property(x => x.SubjectType).HasMaxLength(40); b.Property(x => x.Category).HasMaxLength(64); b.Property(x => x.Provider).HasMaxLength(120); b.Property(x => x.Channel).HasMaxLength(64); b.Property(x => x.InteractionType).HasMaxLength(64); b.Property(x => x.SourceReference).HasMaxLength(512); b.Property(x => x.Evidence).HasMaxLength(1000); b.Property(x => x.LandingPage).HasMaxLength(512); b.Property(x => x.Referrer).HasMaxLength(512); b.Property(x => x.UtmSource).HasMaxLength(120); b.Property(x => x.UtmMedium).HasMaxLength(120); b.Property(x => x.UtmCampaign).HasMaxLength(200); b.Property(x => x.UtmContent).HasMaxLength(200); b.Property(x => x.UtmTerm).HasMaxLength(200); b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.ActorType).HasMaxLength(32); b.Property(x => x.ActorReference).HasMaxLength(160); b.Property(x => x.MetadataJson).HasMaxLength(8000); b.Property(x => x.DedupeKey).HasMaxLength(64); b.Property(x => x.Cost).HasPrecision(18,2); b.HasIndex(x => new { x.CompanyId, x.SubjectType, x.SubjectId, x.ObservedUtc }); b.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique(); b.HasOne<SalesAcquisitionCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.SetNull); }
}

public sealed class SalesSourceAttributionConfiguration : IEntityTypeConfiguration<SalesSourceAttribution>
{
    public void Configure(EntityTypeBuilder<SalesSourceAttribution> b) { b.ToTable("SalesSourceAttributions"); b.HasKey(x => x.Id); b.Property(x => x.SubjectType).HasMaxLength(40); b.Property(x => x.Currency).HasMaxLength(3); b.Property(x => x.TotalAcquisitionCost).HasPrecision(18,2); b.HasIndex(x => new { x.CompanyId, x.SubjectType, x.SubjectId }).IsUnique(); }
}

public sealed class SalesContactPermissionConfiguration : IEntityTypeConfiguration<SalesContactPermission>
{
    public void Configure(EntityTypeBuilder<SalesContactPermission> b) { b.ToTable("SalesContactPermissions"); b.HasKey(x => x.Id); b.Property(x => x.Channel).HasMaxLength(32); b.Property(x => x.Address).HasMaxLength(320); b.Property(x => x.Status).HasMaxLength(32); b.Property(x => x.LegalBasis).HasMaxLength(64); b.Property(x => x.SourceReference).HasMaxLength(512); b.HasIndex(x => new { x.CompanyId, x.ContactId, x.Channel, x.Address }).IsUnique(); }
}
