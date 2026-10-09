using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class MonthlyReviewSnapshotConfiguration : IEntityTypeConfiguration<MonthlyReviewSnapshot>
{
    public void Configure(EntityTypeBuilder<MonthlyReviewSnapshot> b)
    {
        b.ToTable("monthly_review_snapshots"); b.HasKey(x=>x.Id);
        b.Property(x=>x.Lens).HasMaxLength(32); b.Property(x=>x.AccessStamp).HasMaxLength(64);
        b.Property(x=>x.Checksum).HasMaxLength(64); b.Property(x=>x.CalculationVersion).HasMaxLength(64);
        b.Property(x=>x.PayloadJson).HasColumnType("nvarchar(max)");
        b.HasIndex(x=>new {x.CompanyId,x.CreatedByUserId,x.RequestId}).IsUnique();
        b.HasIndex(x=>new {x.CompanyId,x.SeriesId,x.Revision}).IsUnique();
        b.HasIndex(x=>new {x.CompanyId,x.PreviousId}).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.HasIndex(x=>new {x.CompanyId,x.CreatedByUserId,x.Lens,x.Year,x.Month,x.SavedAtUtc});
        b.HasOne(x=>x.Company).WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
