using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanyBriefingDeliveryPreferenceConfiguration : IEntityTypeConfiguration<CompanyBriefingDeliveryPreference>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingDeliveryPreference> builder)
    {
        builder.ToTable("company_briefing_delivery_preferences");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.InAppEnabled).HasColumnName("in_app_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.MobileEnabled).HasColumnName("mobile_enabled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.DailyEnabled).HasColumnName("daily_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.WeeklyEnabled).HasColumnName("weekly_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.PreferredDeliveryTime).HasColumnName("preferred_delivery_time").HasDefaultValue(new TimeOnly(8, 0)).IsRequired();
        builder.Property(x => x.PreferredTimezone).HasColumnName("preferred_timezone").HasMaxLength(100);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

