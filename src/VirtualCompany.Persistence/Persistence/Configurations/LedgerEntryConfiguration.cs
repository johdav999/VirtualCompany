using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("ledger_entries");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id").IsRequired();
        builder.Property(x => x.EntryNumber).HasColumnName("entry_number").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntryUtc).HasColumnName("entry_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64);
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(x => x.PostedAtUtc).HasColumnName("posted_at");
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.EntryUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.PostedAtUtc }).IsUnique().HasFilter("source_type IS NOT NULL AND source_id IS NOT NULL AND posted_at IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.EntryNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.EntryUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FiscalPeriod)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FiscalPeriodId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

