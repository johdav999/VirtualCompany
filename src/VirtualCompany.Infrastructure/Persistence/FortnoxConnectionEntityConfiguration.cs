using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FortnoxConnectionEntityConfiguration : IEntityTypeConfiguration<FortnoxConnection>
{
    public void Configure(EntityTypeBuilder<FortnoxConnection> builder)
    {
        builder.ToTable("fortnox_connections");
        builder.ToTable(t =>
            t.HasCheckConstraint("CK_fortnox_connections_status", FortnoxConnectionStatusValues.BuildCheckConstraintSql("status")));

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectedByUserId).HasColumnName("connected_by_user_id").IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(status => status.ToStorageValue(), value => FortnoxConnectionStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.EncryptedAccessToken).HasColumnName("encrypted_access_token");
        builder.Property(x => x.EncryptedRefreshToken).HasColumnName("encrypted_refresh_token");
        builder.Property(x => x.AccessTokenExpiresUtc).HasColumnName("access_token_expires_at");
        builder.Property(x => x.GrantedScopes)
            .HasColumnName("granted_scopes_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.ProviderTenantId).HasColumnName("provider_tenant_id").HasMaxLength(256);
        HasJsonObjectConversion(builder.Property(x => x.ProviderMetadata).HasColumnName("provider_metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ConnectedUtc).HasColumnName("connected_at");
        builder.Property(x => x.LastRefreshAttemptUtc).HasColumnName("last_refresh_attempt_at");
        builder.Property(x => x.LastSuccessfulRefreshUtc).HasColumnName("last_successful_refresh_at");
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ConnectedByUser)
            .WithMany()
            .HasForeignKey(x => x.ConnectedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static PropertyBuilder<JsonObject> HasJsonObjectConversion(PropertyBuilder<JsonObject> propertyBuilder)
    {
        var converter = new ValueConverter<JsonObject, string>(
            value => SerializeJsonObject(value),
            value => DeserializeJsonObject(value));

        var comparer = new ValueComparer<JsonObject>(
            (left, right) => SerializeJsonObject(left) == SerializeJsonObject(right),
            value => StringComparer.Ordinal.GetHashCode(SerializeJsonObject(value)),
            value => DeserializeJsonObject(SerializeJsonObject(value)));

        propertyBuilder.HasColumnType("nvarchar(max)");
        propertyBuilder.HasConversion(converter);
        propertyBuilder.Metadata.SetValueComparer(comparer);
        return propertyBuilder;
    }

    private static string SerializeJsonObject(JsonObject? value) =>
        (value ?? new JsonObject()).ToJsonString(null);

    private static JsonObject DeserializeJsonObject(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new JsonObject()
            : JsonNode.Parse(value, null, default(JsonDocumentOptions)) as JsonObject ?? new JsonObject();
}
