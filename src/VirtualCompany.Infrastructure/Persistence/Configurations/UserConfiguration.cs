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
    private const string JsonObjectDefaultSql = "N'{}'";
    private const string JsonArrayDefaultSql = "N'[]'";

    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> propertyBuilder)
        where T : class, new()
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value ?? new T(), SerializerOptions),
            value => DeserializeOrDefault<T>(value));

        var comparer = new ValueComparer<T>(
            (left, right) => Serialize(left) == Serialize(right),
            value => StringComparer.Ordinal.GetHashCode(Serialize(value)),
            value => DeserializeOrDefault<T>(Serialize(value)));

        propertyBuilder.HasColumnType("nvarchar(max)");
        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        return propertyBuilder;
    }

    public static PropertyBuilder<JsonArray> HasJsonArrayConversion(this PropertyBuilder<JsonArray> propertyBuilder)
    {
        var converter = new ValueConverter<JsonArray, string>(
            value => SerializeArray(value),
            value => DeserializeArray(value));

        var comparer = new ValueComparer<JsonArray>(
            (left, right) => SerializeArray(left) == SerializeArray(right),
            value => StringComparer.Ordinal.GetHashCode(SerializeArray(value)),
            value => DeserializeArray(SerializeArray(value)));

        propertyBuilder.HasColumnType("nvarchar(max)");
        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        return propertyBuilder;
    }

    public static string JsonObjectDefault => JsonObjectDefaultSql;
    public static string JsonArrayDefault => JsonArrayDefaultSql;

    private static string SerializeArray(JsonArray? value) =>
        JsonSerializer.Serialize(value ?? new JsonArray(), SerializerOptions);

    private static JsonArray DeserializeArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonArray();
        }

        var node = JsonNode.Parse(json);
        return node is JsonArray array ? array : new JsonArray();
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

