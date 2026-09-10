using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesNarrationAttemptConfiguration : IEntityTypeConfiguration<SalesNarrationAttempt>
{
    public void Configure(EntityTypeBuilder<SalesNarrationAttempt> b)
    {
        b.ToTable("sales_narration_attempts");
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.AssetId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.StartedUtc });
        b.Property(x => x.Status).HasMaxLength(32);
        b.Property(x => x.ProviderResponseId).HasMaxLength(200);
        b.Property(x => x.UsageJson).HasMaxLength(8000);
        b.Property(x => x.RateVersion).HasMaxLength(200);
        b.Property(x => x.EstimatedCostUsd).HasPrecision(18, 8);
        b.HasOne<SalesNarrationAsset>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesNarrationRevision>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ApprovalRevisionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

