using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class SalesPresentationLegacyCompatibilityRecordConfiguration:IEntityTypeConfiguration<SalesPresentationLegacyCompatibilityRecord>
{
    public void Configure(EntityTypeBuilder<SalesPresentationLegacyCompatibilityRecord>b)
    {
        b.ToTable("sales_presentation_legacy_compatibility",t=>t.HasCheckConstraint("CK_sales_presentation_legacy_compatibility_attempts","attempt_count >= 0"));
        b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        b.Property(x=>x.Id).HasColumnName("id");b.Property(x=>x.CompanyId).HasColumnName("company_id");b.Property(x=>x.DeckId).HasColumnName("deck_id");b.Property(x=>x.SessionId).HasColumnName("session_id");b.Property(x=>x.PresentationRunId).HasColumnName("presentation_run_id");
        b.Property(x=>x.SourceContentHash).HasColumnName("source_content_hash").HasMaxLength(64);b.Property(x=>x.Status).HasColumnName("status").HasMaxLength(24);b.Property(x=>x.Disposition).HasColumnName("disposition").HasMaxLength(32);b.Property(x=>x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100);b.Property(x=>x.Summary).HasColumnName("summary").HasMaxLength(1000);b.Property(x=>x.AttemptCount).HasColumnName("attempt_count");b.Property(x=>x.CompletedUtc).HasColumnName("completed_at");b.Property(x=>x.FailedUtc).HasColumnName("failed_at");b.Property(x=>x.CreatedUtc).HasColumnName("created_at");b.Property(x=>x.UpdatedUtc).HasColumnName("updated_at");b.Property(x=>x.ConcurrencyVersion).HasColumnName("concurrency_version").HasDefaultValue(1L).IsConcurrencyToken();
        b.HasIndex(x=>new{x.CompanyId,x.DeckId}).IsUnique();b.HasIndex(x=>new{x.CompanyId,x.Status,x.UpdatedUtc});
        b.HasOne(x=>x.Deck).WithMany().HasForeignKey(x=>new{x.CompanyId,x.DeckId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x=>x.PresentationRun).WithMany().HasForeignKey(x=>new{x.CompanyId,x.PresentationRunId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict);
    }
}
