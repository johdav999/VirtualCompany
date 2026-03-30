using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AuthProvider).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AuthSubject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();

        builder.HasIndex(x => new { x.AuthProvider, x.AuthSubject }).IsUnique();
        builder.HasIndex(x => x.Email);
    }
}

internal static class CompanyJsonColumnConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> HasJsonbConversion<T>(this PropertyBuilder<T> propertyBuilder)
        where T : class, new()
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value ?? new T(), SerializerOptions),
            value => DeserializeOrDefault<T>(value));

        var comparer = new ValueComparer<T>(
            (left, right) => Serialize(left) == Serialize(right),
            value => StringComparer.Ordinal.GetHashCode(Serialize(value)),
            value => DeserializeOrDefault<T>(Serialize(value)));

        propertyBuilder.HasColumnType("jsonb");
        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        return propertyBuilder;
    }

    private static string Serialize<T>(T? value)
        where T : class, new() =>
        JsonSerializer.Serialize(value ?? new T(), SerializerOptions);

    private static T DeserializeOrDefault<T>(string? json)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new T();
        }

        return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
    }
}

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("companies");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Industry).HasMaxLength(100);
        builder.Property(x => x.BusinessType).HasMaxLength(100);
        builder.Property(x => x.Timezone).HasMaxLength(100);
        builder.Property(x => x.Currency).HasMaxLength(16);
        builder.Property(x => x.Language).HasMaxLength(16);
        builder.Property(x => x.ComplianceRegion).HasMaxLength(50);
        builder.Property(x => x.Branding)
            .HasColumnName("branding_json")
            .HasJsonbConversion()
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(x => x.Settings)
            .HasColumnName("settings_json")
            .HasJsonbConversion()
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(x => x.OnboardingStateJson);
        builder.Property(x => x.OnboardingCurrentStep);
        builder.Property(x => x.OnboardingTemplateId).HasMaxLength(100);
        builder.Property(x => x.OnboardingStatus)
            .HasConversion(status => status.ToStorageValue(), value => CompanyOnboardingStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(CompanyOnboardingStatus.NotStarted.ToStorageValue())
            .IsRequired();
        builder.Property(x => x.OnboardingLastSavedUtc);
        builder.Property(x => x.OnboardingCompletedUtc);
        builder.Property(x => x.OnboardingAbandonedUtc);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => x.OnboardingCompletedUtc);
        builder.HasIndex(x => x.OnboardingStatus);

        builder.HasMany(x => x.Memberships)
            .WithOne(x => x.Company)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Notes)
            .WithOne(x => x.Company)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanySetupTemplateConfiguration : IEntityTypeConfiguration<CompanySetupTemplate>
{
    public void Configure(EntityTypeBuilder<CompanySetupTemplate> builder)
    {
        builder.ToTable("company_setup_templates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TemplateId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasMaxLength(100);
        builder.Property(x => x.IndustryTag).HasMaxLength(100);
        builder.Property(x => x.BusinessTypeTag).HasMaxLength(100);
        builder.Property(x => x.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Defaults)
            .HasColumnName("defaults_json")
            .HasJsonbConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonbConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => x.TemplateId).IsUnique();
    }
}

internal sealed class CompanyMembershipConfiguration : IEntityTypeConfiguration<CompanyMembership>
{
    public void Configure(EntityTypeBuilder<CompanyMembership> builder)
    {
        builder.ToTable("company_memberships");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Role)
            .HasConversion(role => role.ToStorageValue(), value => CompanyMembershipRoleValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(status => status.ToStorageValue(), value => CompanyMembershipStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.PermissionsJson);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Status });

        builder.HasOne(x => x.Company)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyOwnedNoteConfiguration : IEntityTypeConfiguration<CompanyOwnedNote>
{
    public void Configure(EntityTypeBuilder<CompanyOwnedNote> builder)
    {
        builder.ToTable("company_notes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Id });
    }
}