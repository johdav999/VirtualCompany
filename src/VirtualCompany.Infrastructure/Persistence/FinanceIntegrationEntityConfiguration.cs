using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class FinanceIntegrationConnectionEntityConfiguration : IEntityTypeConfiguration<FinanceIntegrationConnection>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationConnection> builder)
    {
        builder.ToTable("finance_integration_connections");
        builder.ToTable(t => t.HasCheckConstraint("CK_finance_integration_connections_status", FinanceIntegrationConnectionStatuses.BuildCheckConstraintSql("status")));

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(FinanceIntegrationConnectionStatuses.Pending).IsRequired();
        builder.Property(x => x.ConnectedByUserId).HasColumnName("connected_by_user_id");
        builder.Property(x => x.ProviderTenantId).HasColumnName("provider_tenant_id").HasMaxLength(256);
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(160);
        builder.Property(x => x.Scopes)
            .HasColumnName("scopes_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ConnectedUtc).HasColumnName("connected_at");
        builder.Property(x => x.LastSyncUtc).HasColumnName("last_sync_at");
        builder.Property(x => x.DisabledUtc).HasColumnName("disabled_at");
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.ProviderKey, x.ProviderTenantId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ConnectedByUser).WithMany().HasForeignKey(x => x.ConnectedByUserId).OnDelete(DeleteBehavior.SetNull);
    }

    internal static PropertyBuilder<JsonObject> HasJsonObjectConversion(PropertyBuilder<JsonObject> propertyBuilder)
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

internal sealed class FinanceIntegrationTokenEntityConfiguration : IEntityTypeConfiguration<FinanceIntegrationToken>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationToken> builder)
    {
        builder.ToTable("finance_integration_tokens");
        builder.ToTable(t => t.HasCheckConstraint("CK_finance_integration_tokens_token_type", FinanceIntegrationTokenTypes.BuildCheckConstraintSql("token_type")));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.TokenType).HasColumnName("token_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EncryptedToken).HasColumnName("encrypted_token").HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.TokenType });
        builder.HasIndex(x => new { x.ConnectionId, x.TokenType }).IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Connection)
            .WithMany(x => x.Tokens)
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceIntegrationSyncStateEntityConfiguration : IEntityTypeConfiguration<FinanceIntegrationSyncState>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationSyncState> builder)
    {
        builder.ToTable("finance_integration_sync_states");
        builder.ToTable(t => t.HasCheckConstraint("CK_finance_integration_sync_states_status", FinanceIntegrationSyncStatuses.BuildCheckConstraintSql("status")));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(128).HasDefaultValue("default").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).HasDefaultValue(FinanceIntegrationSyncStatuses.Pending).IsRequired();
        builder.Property(x => x.Cursor).HasColumnName("cursor").HasMaxLength(1024);
        builder.Property(x => x.LastStartedUtc).HasColumnName("last_started_at");
        builder.Property(x => x.LastCompletedUtc).HasColumnName("last_completed_at");
        builder.Property(x => x.LastErrorSummary).HasColumnName("last_error_summary").HasMaxLength(1000);
        builder.Property(x => x.ConsecutiveFailureCount).HasColumnName("consecutive_failure_count").HasDefaultValue(0).IsRequired();
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.EntityType, x.ScopeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.LastStartedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Connection)
            .WithMany(x => x.SyncStates)
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FinanceExternalReferenceEntityConfiguration : IEntityTypeConfiguration<FinanceExternalReference>
{
    public void Configure(EntityTypeBuilder<FinanceExternalReference> builder)
    {
        builder.ToTable("finance_external_references");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InternalRecordId).HasColumnName("internal_record_id").IsRequired();
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ExternalNumber).HasColumnName("external_number").HasMaxLength(128);
        builder.Property(x => x.ExternalUpdatedUtc).HasColumnName("external_updated_at");
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId });
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.EntityType, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.EntityType, x.InternalRecordId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.EntityType, x.ExternalNumber });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Connection)
            .WithMany(x => x.ExternalReferences)
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FinanceIntegrationAuditEventEntityConfiguration : IEntityTypeConfiguration<FinanceIntegrationAuditEvent>
{
    public void Configure(EntityTypeBuilder<FinanceIntegrationAuditEvent> builder)
    {
        builder.ToTable("finance_integration_audit_events");
        builder.ToTable(t => t.HasCheckConstraint("CK_finance_integration_audit_events_outcome", FinanceIntegrationAuditOutcomes.BuildCheckConstraintSql("outcome")));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64);
        builder.Property(x => x.InternalRecordId).HasColumnName("internal_record_id");
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(256);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedCount).HasColumnName("created_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.UpdatedCount).HasColumnName("updated_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.SkippedCount).HasColumnName("skipped_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ErrorCount).HasColumnName("error_count").HasDefaultValue(0).IsRequired();
        FinanceIntegrationConnectionEntityConfiguration.HasJsonObjectConversion(builder.Property(x => x.Metadata).HasColumnName("metadata_json"))
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.EntityType, x.InternalRecordId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Connection)
            .WithMany(x => x.AuditEvents)
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FortnoxWriteCommandEntityConfiguration : IEntityTypeConfiguration<FortnoxWriteCommand>
{
    public void Configure(EntityTypeBuilder<FortnoxWriteCommand> builder)
    {
        builder.ToTable("fortnox_write_commands");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConnectionId).HasColumnName("connection_id");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.ApprovalId).HasColumnName("approval_id");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.HttpMethod).HasColumnName("http_method").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Path).HasColumnName("path").HasMaxLength(512).IsRequired();
        builder.Property(x => x.TargetCompany).HasColumnName("target_company").HasMaxLength(160).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayloadSummary).HasColumnName("payload_summary").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.SanitizedPayloadJson).HasColumnName("sanitized_payload_json").HasMaxLength(8000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCategory).HasColumnName("failure_category").HasMaxLength(64);
        builder.Property(x => x.SafeFailureSummary).HasColumnName("safe_failure_summary").HasMaxLength(1000);
        builder.Property(x => x.ExternalId).HasColumnName("external_id").HasMaxLength(256);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ApprovedUtc).HasColumnName("approved_at");
        builder.Property(x => x.ExecutionStartedUtc).HasColumnName("execution_started_at");
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at");
        builder.Property(x => x.FailedUtc).HasColumnName("failed_at");

        builder.HasIndex(x => new { x.CompanyId, x.PayloadHash, x.HttpMethod, x.Path, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.ApprovalId }).IsUnique().HasFilter("approval_id IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.CreatedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Connection)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ConnectionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class FinanceSourceTrackingConfiguration :
    IEntityTypeConfiguration<FinanceAccount>,
    IEntityTypeConfiguration<FinanceCounterparty>,
    IEntityTypeConfiguration<FinanceInvoice>,
    IEntityTypeConfiguration<FinanceBill>,
    IEntityTypeConfiguration<FinanceTransaction>,
    IEntityTypeConfiguration<FinanceBalance>,
    IEntityTypeConfiguration<Payment>,
    IEntityTypeConfiguration<CompanyBankAccount>,
    IEntityTypeConfiguration<BankTransaction>,
    IEntityTypeConfiguration<FinanceAsset>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceCounterparty> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceInvoice> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceBill> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceTransaction> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceBalance> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<Payment> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<CompanyBankAccount> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<BankTransaction> builder) => ConfigureSourceTracking(builder);
    public void Configure(EntityTypeBuilder<FinanceAsset> builder) => ConfigureSourceTracking(builder);

    private static void ConfigureSourceTracking<TEntity>(EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<string>("SourceType")
            .HasColumnName("source_type")
            .HasMaxLength(32)
            .HasDefaultValue(FinanceRecordSourceTypes.Manual)
            .IsRequired();
        builder.Property<string?>("ProviderKey")
            .HasColumnName("provider_key")
            .HasMaxLength(64);
        builder.Property<string?>("ProviderExternalId")
            .HasColumnName("provider_external_id")
            .HasMaxLength(256);
        builder.Property<Guid?>("FinanceExternalReferenceId")
            .HasColumnName("finance_external_reference_id");

        builder.ToTable(t => t.HasCheckConstraint($"CK_{builder.Metadata.GetTableName()}_source_type", FinanceRecordSourceTypes.BuildCheckConstraintSql("source_type")));
        builder.HasIndex("CompanyId", "SourceType");
        builder.HasIndex("CompanyId", "ProviderKey", "ProviderExternalId")
            .HasFilter("provider_key IS NOT NULL AND provider_external_id IS NOT NULL");
        builder.HasOne(typeof(FinanceExternalReference))
            .WithMany()
            .HasForeignKey("FinanceExternalReferenceId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
