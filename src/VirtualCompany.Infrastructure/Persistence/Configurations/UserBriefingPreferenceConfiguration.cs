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
internal sealed class UserBriefingPreferenceConfiguration : IEntityTypeConfiguration<UserBriefingPreference>
{
    public void Configure(EntityTypeBuilder<UserBriefingPreference> builder)
    {
        builder.ToTable("user_briefing_preferences");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.DeliveryFrequency)
            .HasColumnName("delivery_frequency")
            .HasConversion(value => value.ToStorageValue(), value => BriefingDeliveryFrequencyValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.IncludedFocusAreas)
            .HasColumnName("included_focus_areas_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.PriorityThreshold)
            .HasColumnName("priority_threshold")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSectionPriorityCategoryValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

