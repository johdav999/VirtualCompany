using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("user_preferences");
        builder.HasKey(x => x.UserId);
        builder.Property(x => x.UiCulture).HasColumnName("ui_culture").HasMaxLength(20).IsRequired().HasDefaultValue("en-GB");
        builder.Property(x => x.FormattingCulture).HasColumnName("formatting_culture").HasMaxLength(20);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.HasOne(x => x.User)
            .WithOne()
            .HasForeignKey<UserPreference>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserPreferenceChangeConfiguration : IEntityTypeConfiguration<UserPreferenceChange>
{
    public void Configure(EntityTypeBuilder<UserPreferenceChange> builder)
    {
        builder.ToTable("user_preference_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PreviousUiCulture).HasColumnName("previous_ui_culture").HasMaxLength(20);
        builder.Property(x => x.NewUiCulture).HasColumnName("new_ui_culture").HasMaxLength(20).IsRequired();
        builder.Property(x => x.PreviousFormattingCulture).HasColumnName("previous_formatting_culture").HasMaxLength(20);
        builder.Property(x => x.NewFormattingCulture).HasColumnName("new_formatting_culture").HasMaxLength(20);
        builder.Property(x => x.ChangedUtc).HasColumnName("changed_utc").IsRequired();
        builder.HasIndex(x => new { x.UserId, x.ChangedUtc });
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
