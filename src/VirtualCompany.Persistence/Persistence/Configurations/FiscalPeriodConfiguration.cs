using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> builder)
    {
        builder.ToTable("finance_fiscal_periods");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.StartUtc).HasColumnName("start_at").IsRequired();
        builder.Property(x => x.EndUtc).HasColumnName("end_at").IsRequired();
        builder.Property(x => x.IsClosed).HasColumnName("is_closed").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsReportingLocked).HasColumnName("is_reporting_locked").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ReportingLockedUtc).HasColumnName("reporting_locked_at");
        builder.Property(x => x.ReportingLockedByUserId).HasColumnName("reporting_locked_by_user_id");
        builder.Property(x => x.ReportingUnlockedUtc).HasColumnName("reporting_unlocked_at");
        builder.Property(x => x.ReportingUnlockedByUserId).HasColumnName("reporting_unlocked_by_user_id");
        builder.Property(x => x.LastCloseValidatedUtc).HasColumnName("last_close_validated_at");
        builder.Property(x => x.LastCloseValidatedByUserId).HasColumnName("last_close_validated_by_user_id");
        builder.Property(x => x.ClosedUtc).HasColumnName("closed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasIndex(x => new { x.CompanyId, x.StartUtc, x.EndUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IsClosed, x.IsReportingLocked });
        builder.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

