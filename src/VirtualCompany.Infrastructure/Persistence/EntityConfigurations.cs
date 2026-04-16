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

    public static string JsonObjectDefault => JsonObjectDefaultSql;
    public static string JsonArrayDefault => JsonArrayDefaultSql;

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
            .HasJsonConversion()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Settings)
            .HasColumnName("settings_json")
            .HasJsonConversion()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OnboardingStateJson);
        builder.Property(x => x.OnboardingCurrentStep);
        builder.Property(x => x.OnboardingTemplateId).HasMaxLength(100);
        builder.Property(x => x.OnboardingStatus)
            .HasConversion(status => status.ToStorageValue(), value => CompanyOnboardingStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(CompanyOnboardingStatus.NotStarted)
            .HasSentinel((CompanyOnboardingStatus)0)
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

        builder.HasMany(x => x.Documents)
            .WithOne(x => x.Company)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.KnowledgeChunks)
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
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => x.TemplateId).IsUnique();
    }
}

internal sealed class DashboardDepartmentConfigConfiguration : IEntityTypeConfiguration<DashboardDepartmentConfig>
{
    public void Configure(EntityTypeBuilder<DashboardDepartmentConfig> builder)
    {
        builder.ToTable("dashboard_department_configs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Icon).HasColumnName("icon").HasMaxLength(64);
        builder.Property(x => x.Navigation)
            .HasColumnName("navigation_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Visibility)
            .HasColumnName("visibility_roles_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.EmptyState)
            .HasColumnName("empty_state_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.Department }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.DisplayOrder, x.Department });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Widgets).WithOne(x => x.DepartmentConfig).HasForeignKey(x => x.DepartmentConfigId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DashboardWidgetConfigConfiguration : IEntityTypeConfiguration<DashboardWidgetConfig>
{
    public void Configure(EntityTypeBuilder<DashboardWidgetConfig> builder)
    {
        builder.ToTable("dashboard_widget_configs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DepartmentConfigId).HasColumnName("department_config_id").IsRequired();
        builder.Property(x => x.WidgetKey).HasColumnName("widget_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.WidgetType).HasColumnName("widget_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.SummaryBinding).HasColumnName("summary_binding").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Navigation).HasColumnName("navigation_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.Visibility).HasColumnName("visibility_roles_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.EmptyState).HasColumnName("empty_state_json").HasJsonConversion<Dictionary<string, JsonNode?>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.DepartmentConfigId, x.DisplayOrder, x.WidgetKey });
        builder.HasIndex(x => new { x.CompanyId, x.DepartmentConfigId, x.WidgetKey }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AgentTemplateConfiguration : IEntityTypeConfiguration<AgentTemplate>
{
    public void Configure(EntityTypeBuilder<AgentTemplate> builder)
    {
        builder.ToTable("agent_templates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TemplateId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Department).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PersonaSummary).HasMaxLength(1000);
        builder.Property(x => x.AvatarUrl).HasMaxLength(2048);
        builder.Property(x => x.DefaultSeniority)
            .HasConversion(value => value.ToStorageValue(), value => AgentSeniorityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SortOrder).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Personality)
            .HasColumnName("personality_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Objectives)
            .HasColumnName("objectives_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Kpis)
            .HasColumnName("kpis_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Tools)
            .HasColumnName("tool_permissions_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Scopes)
            .HasColumnName("data_scopes_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Thresholds)
            .HasColumnName("approval_thresholds_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.EscalationRules)
            .HasColumnName("escalation_rules_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => x.TemplateId).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.SortOrder });

        // Template default changes should ship in new EF migrations so historical revisions stay reviewable.
        builder.HasData(AgentTemplateSeedData.GetModelSeeds());
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
        builder.Property(x => x.InvitedEmail).HasMaxLength(256);
        builder.Property(x => x.MembershipAccessConfigurationJson)
            .HasColumnName("permissions_json");

        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId }).HasFilter("\"UserId\" IS NOT NULL").IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.InvitedEmail }).HasFilter("\"InvitedEmail\" IS NOT NULL").IsUnique();
        builder.HasIndex(x => new { x.UserId, x.Status }).HasFilter("\"UserId\" IS NOT NULL");

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

internal sealed class CompanyInvitationConfiguration : IEntityTypeConfiguration<CompanyInvitation>
{
    public void Configure(EntityTypeBuilder<CompanyInvitation> builder)
    {
        builder.ToTable("company_invitations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Role)
            .HasConversion(role => role.ToStorageValue(), value => CompanyMembershipRoleValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(status => status.ToStorageValue(), value => CompanyInvitationStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeliveryStatus)
            .HasConversion(status => status.ToStorageValue(), value => CompanyInvitationDeliveryStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(CompanyInvitationDeliveryStatus.Pending)
            .HasSentinel((CompanyInvitationDeliveryStatus)0)
            .IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.LastSentUtc);
        builder.Property(x => x.AcceptedUtc);
        builder.Property(x => x.LastDeliveredTokenHash).HasMaxLength(128);
        builder.Property(x => x.DeliveryError).HasMaxLength(2000);
        builder.Property(x => x.LastDeliveryCorrelationId).HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ExpiresAtUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Email }).HasFilter("\"Status\" = 'pending'").IsUnique();

        builder.HasIndex(x => new { x.CompanyId, x.DeliveryStatus });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.InvitedByUser)
            .WithMany()
            .HasForeignKey(x => x.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AcceptedByUser)
            .WithMany()
            .HasForeignKey(x => x.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyOutboxMessageConfiguration : IEntityTypeConfiguration<CompanyOutboxMessage>
{
    public void Configure(EntityTypeBuilder<CompanyOutboxMessage> builder)
    {
        builder.ToTable("company_outbox_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Topic).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MessageType).HasMaxLength(1000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.CausationId).HasMaxLength(128);
        builder.Property(x => x.HeadersJson).HasMaxLength(4000);
        builder.Property(x => x.OccurredUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.AvailableUtc).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(status => status.ToStorageValue(), value => CompanyOutboxMessageStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(CompanyOutboxMessageStatusValues.DefaultStatus)
            .HasSentinel((CompanyOutboxMessageStatus)0)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastAttemptUtc);
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.Property(x => x.ClaimToken).HasMaxLength(64).IsConcurrencyToken();
        builder.Property(x => x.ProcessedUtc).IsConcurrencyToken();

        builder.HasIndex(x => new { x.ProcessedUtc, x.AvailableUtc, x.AttemptCount });
        builder.HasIndex(x => new { x.ProcessedUtc, x.ClaimedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.AvailableUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Topic, x.IdempotencyKey }).HasFilter("\"IdempotencyKey\" IS NOT NULL").IsUnique();
        builder.HasIndex(x => new { x.Status, x.AvailableUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProcessedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BackgroundExecutionConfiguration : IEntityTypeConfiguration<BackgroundExecution>
{
    public void Configure(EntityTypeBuilder<BackgroundExecution> builder)
    {
        builder.ToTable("background_executions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ExecutionType)
            .HasColumnName("execution_type")
            .HasConversion(value => value.ToStorageValue(), value => BackgroundExecutionTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => BackgroundExecutionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BackgroundExecutionStatusValues.DefaultStatus)
            .HasSentinel((BackgroundExecutionStatus)0)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(x => x.NextRetryUtc).HasColumnName("next_retry_at");
        builder.Property(x => x.StartedUtc).HasColumnName("started_at");
        builder.Property(x => x.HeartbeatUtc).HasColumnName("heartbeat_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.FailureCategory)
            .HasColumnName("failure_category")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : BackgroundExecutionFailureCategoryValues.Parse(value));
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
        builder.Property(x => x.FailureMessage).HasColumnName("failure_message").HasMaxLength(4000);
        builder.Property(x => x.EscalationId).HasColumnName("escalation_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.NextRetryUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedEntityType, x.RelatedEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.ExecutionType, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.HeartbeatUtc });
        builder.HasIndex(x => x.CorrelationId);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExecutionExceptionRecordConfiguration : IEntityTypeConfiguration<ExecutionExceptionRecord>
{
    public void Configure(EntityTypeBuilder<ExecutionExceptionRecord> builder)
    {
        builder.ToTable("execution_exceptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionKindValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionSeverityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(ExecutionExceptionStatusValues.DefaultStatus)
            .HasSentinel((ExecutionExceptionStatus)0)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(value => value.ToStorageValue(), value => ExecutionExceptionSourceTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.BackgroundExecutionId).HasColumnName("background_execution_id");
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100);
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id").HasMaxLength(128);
        builder.Property(x => x.IncidentKey).HasColumnName("incident_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(200);
        builder.Property(x => x.Details)
            .HasColumnName("details_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Kind, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IncidentKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId });
        builder.HasIndex(x => x.BackgroundExecutionId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.BackgroundExecution)
            .WithMany()
            .HasForeignKey(x => x.BackgroundExecutionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ActorType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TargetType).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TargetId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RationaleSummary).HasMaxLength(512);
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.OccurredUtc).IsRequired();
        builder.Property(x => x.PayloadDiffJson).HasColumnName("payload_diff_json").HasMaxLength(16000);
        builder.Property(x => x.AgentName).HasColumnName("agent_name").HasMaxLength(200);
        builder.Property(x => x.AgentRole).HasColumnName("agent_role").HasMaxLength(128);
        builder.Property(x => x.ResponsibilityDomain).HasColumnName("responsibility_domain").HasMaxLength(128);
        builder.Property(x => x.PromptProfileVersion).HasColumnName("prompt_profile_version").HasMaxLength(64);
        builder.Property(x => x.BoundaryDecisionOutcome).HasColumnName("boundary_decision_outcome").HasMaxLength(64);
        builder.Property(x => x.IdentityReasonCode).HasColumnName("identity_reason_code").HasMaxLength(128);
        builder.Property(x => x.BoundaryReasonCode).HasColumnName("boundary_reason_code").HasMaxLength(128);
        builder.Property(x => x.DataSources)
            .HasColumnName("data_sources_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.DataSourcesUsed)
            .HasColumnName("data_sources_used_json")
            .HasJsonConversion<List<AuditDataSourceUsed>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.RelatedAgentId);
        builder.Property(x => x.RelatedTaskId);
        builder.Property(x => x.RelatedWorkflowInstanceId);
        builder.Property(x => x.RelatedApprovalRequestId);
        builder.Property(x => x.RelatedToolExecutionAttemptId);

        builder.HasIndex(x => new { x.CompanyId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ActorType, x.ActorId });
        builder.HasIndex(x => new { x.CompanyId, x.TargetType, x.TargetId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedAgentId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedAgentId, x.BoundaryDecisionOutcome, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedTaskId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedWorkflowInstanceId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedApprovalRequestId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RelatedToolExecutionAttemptId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
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

internal sealed class AgentTaskCreationDedupeRecordConfiguration : IEntityTypeConfiguration<AgentTaskCreationDedupeRecord>
{
    public void Configure(EntityTypeBuilder<AgentTaskCreationDedupeRecord> builder)
    {
        builder.ToTable("agent_task_creation_dedupe");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(128).IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.TriggerSource).HasColumnName("trigger_source").HasMaxLength(128).IsRequired();
        builder.Property(x => x.TriggerEventId).HasColumnName("trigger_event_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ExpiresUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerSource, x.TriggerEventId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Task)
            .WithMany()
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("AK_agents_company_id_id");
        builder.Property(x => x.TemplateId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RoleName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RoleBrief).HasMaxLength(4000).HasColumnName("role_brief");
        builder.Property(x => x.Department).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AvatarUrl).HasMaxLength(2048);
        builder.Property(x => x.Seniority)
            .HasConversion(value => value.ToStorageValue(), value => AgentSeniorityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => AgentStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(AgentStatusValues.DefaultStatus)
            .HasSentinel((AgentStatus)0)
            .IsRequired();
        builder.Property(x => x.AutonomyLevel)
            .HasColumnName("autonomy_level")
            .HasConversion(value => value.ToStorageValue(), value => AgentAutonomyLevelValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(AgentAutonomyLevelValues.DefaultLevel)
            .IsRequired();
        builder.Property(x => x.Personality)
            .HasColumnName("personality_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Objectives)
            .HasColumnName("objectives_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Kpis)
            .HasColumnName("kpis_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Tools)
            .HasColumnName("tool_permissions_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Scopes)
            .HasColumnName("data_scopes_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Thresholds)
            .HasColumnName("approval_thresholds_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.EscalationRules)
            .HasColumnName("escalation_rules_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.TriggerLogic)
            .HasColumnName("trigger_logic_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.WorkingHours)
            .HasColumnName("working_hours_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CommunicationProfile)
            .HasColumnName("communication_profile_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CommunicationProfile)
            .HasColumnName("communication_profile_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Department });
        builder.HasIndex(x => new { x.CompanyId, x.DisplayName });
        builder.HasIndex(x => new { x.CompanyId, x.Department, x.Status });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContextRetrievalConfiguration : IEntityTypeConfiguration<ContextRetrieval>
{
    public void Configure(EntityTypeBuilder<ContextRetrieval> builder)
    {
        builder.ToTable("context_retrievals");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.TaskId);
        builder.Property(x => x.QueryText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.QueryHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.RetrievalPurpose).HasMaxLength(256);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId, x.CreatedUtc });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ContextRetrievalSourceConfiguration : IEntityTypeConfiguration<ContextRetrievalSource>
{
    public void Configure(EntityTypeBuilder<ContextRetrievalSource> builder)
    {
        builder.ToTable("context_retrieval_sources");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceEntityId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ParentSourceType).HasMaxLength(64);
        builder.Property(x => x.ParentSourceEntityId).HasMaxLength(128);
        builder.Property(x => x.ParentTitle).HasMaxLength(256);
        builder.Property(x => x.Title).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Snippet).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SectionId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SectionTitle).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SectionRank).IsRequired();
        builder.Property(x => x.Locator).HasMaxLength(512);
        builder.Property(x => x.Rank).IsRequired();
        builder.Property(x => x.Score);
        builder.Property(x => x.TimestampUtc);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.RetrievalId, x.Rank }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.RetrievalId, x.SectionId, x.SectionRank });
        builder.HasIndex(x => new { x.CompanyId, x.ParentSourceType, x.ParentSourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceEntityId });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Retrieval)
            .WithMany(x => x.Sources)
            .HasForeignKey(x => x.RetrievalId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyKnowledgeDocumentConfiguration : IEntityTypeConfiguration<CompanyKnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DocumentType)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceType)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentSourceTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceRef).HasMaxLength(512);
        builder.Property(x => x.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.StorageUrl).HasMaxLength(2048);
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(255);
        builder.Property(x => x.FileExtension).HasMaxLength(16).IsRequired();
        builder.Property(x => x.FileSizeBytes).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.AccessScope)
            .HasColumnName("access_scope_json")
            .HasJsonConversion<CompanyKnowledgeDocumentAccessScope>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.IngestionStatus)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentIngestionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValueSql("'uploaded'")
            .HasSentinel((CompanyKnowledgeDocumentIngestionStatus)0)
            .IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.FailureMessage).HasMaxLength(2000);
        builder.Property(x => x.FailureAction).HasMaxLength(500);
        builder.Property(x => x.FailureTechnicalDetail).HasMaxLength(4000);
        builder.Property(x => x.ExtractedText);
        builder.Property(x => x.IndexingStatus)
            .HasConversion(value => value.ToStorageValue(), value => CompanyKnowledgeDocumentIndexingStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValueSql("'not_indexed'")
            .HasSentinel((CompanyKnowledgeDocumentIndexingStatus)0)
            .IsRequired();
        builder.Property(x => x.IndexingFailureCode).HasMaxLength(100);
        builder.Property(x => x.IndexingFailureMessage).HasMaxLength(2000);
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200);
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.CurrentChunkSetFingerprint).HasMaxLength(128);
        builder.Property(x => x.CurrentChunkSetVersion).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ActiveChunkCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.CanRetry).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.Property(x => x.IndexedUtc);
        builder.Property(x => x.IndexingFailedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IngestionStatus });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus, x.IndexingRequestedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.IndexingStatus, x.IndexingStartedUtc });
        builder.HasOne(x => x.Company).WithMany(x => x.Documents).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyKnowledgeChunkConfiguration : IEntityTypeConfiguration<CompanyKnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<CompanyKnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChunkSetVersion).IsRequired();
        builder.Property(x => x.ChunkIndex).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.Embedding).HasColumnType("vector").IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.SourceReference).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.EmbeddingDimensions).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.IsActive, x.DocumentId });
        builder.HasIndex(x => new { x.CompanyId, x.DocumentId, x.ChunkSetVersion, x.IsActive });
        builder.HasIndex(x => new { x.DocumentId, x.ChunkSetVersion, x.ChunkIndex }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasOne(x => x.Company)
            .WithMany(x => x.KnowledgeChunks)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Document)
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MemoryItemConfiguration : IEntityTypeConfiguration<MemoryItem>
{
    public void Configure(EntityTypeBuilder<MemoryItem> builder)
    {
        builder.ToTable("memory_items", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("CK_memory_items_memory_type", MemoryTypeValues.BuildCheckConstraintSql("\"MemoryType\""));
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.MemoryType)
            .HasConversion(value => value.ToStorageValue(), value => MemoryTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.SourceEntityType).HasMaxLength(100);
        builder.Property(x => x.Salience).HasColumnType("numeric(4,3)").IsRequired();
        builder.Property(x => x.ValidFromUtc).IsRequired();
        builder.Property(x => x.ValidToUtc);
        builder.Property(x => x.DeletedUtc);
        builder.Property(x => x.DeletedByActorType).HasMaxLength(64);
        builder.Property(x => x.DeletedByActorId);
        builder.Property(x => x.DeletionReason).HasMaxLength(512);
        builder.Property(x => x.ExpiredByActorType).HasMaxLength(64);
        builder.Property(x => x.ExpiredByActorId);
        builder.Property(x => x.ExpirationReason).HasMaxLength(512);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Embedding).HasColumnType("vector");
        builder.Property(x => x.EmbeddingProvider).HasMaxLength(100);
        builder.Property(x => x.EmbeddingModel).HasMaxLength(200);
        builder.Property(x => x.EmbeddingModelVersion).HasMaxLength(100);
        builder.Property(x => x.AgentId).IsRequired(false);
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.AgentId });
        builder.HasIndex(x => new { x.CompanyId, x.MemoryType });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DeletedUtc, x.ValidToUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ValidToUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent).WithMany().HasForeignKey(x => x.AgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ToolExecutionAttemptConfiguration : IEntityTypeConfiguration<ToolExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<ToolExecutionAttempt> builder)
    {
        builder.ToTable("tool_executions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.ActionType)
            .HasColumnName("action_type")
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => ToolExecutionStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id");
        builder.Property(x => x.RequestPayload)
            .HasColumnName("request_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ResultPayload)
            .HasColumnName("response_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ExecutedUtc).HasColumnName("executed_at");
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.StartedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class WorkTaskConfiguration : IEntityTypeConfiguration<WorkTask>
{
    public void Configure(EntityTypeBuilder<WorkTask> builder)
    {
        builder.ToTable("tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AssignedAgentId).HasColumnName("assigned_agent_id");
        builder.Property(x => x.ParentTaskId).HasColumnName("parent_task_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.CreatedByActorType).HasColumnName("created_by_actor_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByActorId).HasColumnName("created_by_actor_id");
        builder.Property(x => x.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(4000);
        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasConversion(value => value.ToStorageValue(), value => WorkTaskPriorityValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkTaskPriorityValues.DefaultPriority)
            .HasSentinel((WorkTaskPriority)0)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => WorkTaskStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkTaskStatusValues.DefaultStatus)
            .HasSentinel((WorkTaskStatus)0)
            .IsRequired();
        builder.Property(x => x.DueUtc).HasColumnName("due_at");
        builder.Property(x => x.InputPayload)
            .HasColumnName("input_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OutputPayload)
            .HasColumnName("output_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.RationaleSummary).HasColumnName("rationale_summary").HasMaxLength(2000);
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasColumnType("numeric(5,4)");
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64).HasDefaultValue(WorkTaskSourceTypes.User).IsRequired();
        builder.Property(x => x.OriginatingAgentId).HasColumnName("originating_agent_id");
        builder.Property(x => x.TriggerSource).HasColumnName("trigger_source").HasMaxLength(128);
        builder.Property(x => x.CreationReason).HasColumnName("creation_reason").HasMaxLength(2000);
        builder.Property(x => x.TriggerEventId).HasColumnName("trigger_event_id").HasMaxLength(200);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.SourceLifecycleVersion).HasColumnName("source_lifecycle_version").HasDefaultValue(0).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId });
        builder.HasIndex(x => new { x.CompanyId, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ParentTaskId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId, x.Status, x.CompletedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TriggerSource, x.TriggerEventId, x.CorrelationId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.OriginatingAgentId, x.CreatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AssignedAgent)
            .WithMany()
            .HasForeignKey(x => x.AssignedAgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ParentTask)
            .WithMany(x => x.Subtasks)
            .HasForeignKey(x => x.ParentTaskId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.WorkflowInstance)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ConversationLinks)
            .WithOne(x => x.Task)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ChannelType).HasColumnName("channel_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ChannelType });
        builder.HasIndex(x => new { x.CompanyId, x.ChannelType, x.CreatedByUserId, x.AgentId })
            .IsUnique()
            .HasFilter("\"agent_id\" IS NOT NULL");
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.TaskLinks)
            .WithOne(x => x.Conversation)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.SenderType).HasColumnName("sender_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SenderId).HasColumnName("sender_id");
        builder.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.StructuredPayload)
            .HasColumnName("structured_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConversationId, x.CreatedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.TaskLinks)
            .WithOne(x => x.Message)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConversationTaskLinkConfiguration : IEntityTypeConfiguration<ConversationTaskLink>
{
    public void Configure(EntityTypeBuilder<ConversationTaskLink> builder)
    {
        builder.ToTable("conversation_task_links");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id").IsRequired();
        builder.Property(x => x.LinkType).HasColumnName("link_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ConversationId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.ConversationId, x.TaskId, x.MessageId }).IsUnique();

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation)
            .WithMany(x => x.TaskLinks)
            .HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Message)
            .WithMany(x => x.TaskLinks)
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Task)
            .WithMany(x => x.ConversationLinks)
            .HasForeignKey(x => x.TaskId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyBriefingConfiguration : IEntityTypeConfiguration<CompanyBriefing>
{
    public void Configure(EntityTypeBuilder<CompanyBriefing> builder)
    {
        builder.ToTable("company_briefings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BriefingType)
            .HasColumnName("briefing_type")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.PeriodStartUtc).HasColumnName("period_start_at").IsRequired();
        builder.Property(x => x.PeriodEndUtc).HasColumnName("period_end_at").IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SummaryBody).HasColumnName("summary_body").IsRequired();
        builder.Property(x => x.StructuredPayload)
            .HasColumnName("structured_payload_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.SourceReferences)
            .HasColumnName("source_refs_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PreferenceSnapshot)
            .HasColumnName("preference_snapshot_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.MessageId).HasColumnName("message_id");
        builder.Property(x => x.GeneratedUtc).HasColumnName("generated_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BriefingType, x.GeneratedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.BriefingType, x.PeriodStartUtc, x.PeriodEndUtc }).IsUnique();
        builder.HasIndex(x => x.MessageId).IsUnique().HasFilter("message_id IS NOT NULL");

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Message).WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyBriefingSectionConfiguration : IEntityTypeConfiguration<CompanyBriefingSection>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingSection> builder)
    {
        builder.ToTable("company_briefing_sections");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BriefingId).HasColumnName("briefing_id").IsRequired();
        builder.Property(x => x.SectionKey).HasColumnName("section_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.GroupingType).HasColumnName("grouping_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.GroupingKey).HasColumnName("grouping_key").HasMaxLength(256).IsRequired();
        builder.Property(x => x.CompanyEntityId).HasColumnName("company_entity_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.SectionType).HasColumnName("section_type").HasMaxLength(64).HasDefaultValue("informational").IsRequired();
        builder.Property(x => x.PriorityCategory)
            .HasColumnName("priority_category")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSectionPriorityCategoryValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BriefingSectionPriorityCategory.Informational)
            .HasSentinel((BriefingSectionPriorityCategory)0)
            .IsRequired();
        builder.Property(x => x.PriorityScore).HasColumnName("priority_score").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.PriorityRuleCode).HasColumnName("priority_rule_code").HasMaxLength(100);
        builder.Property(x => x.EventCorrelationId).HasColumnName("event_correlation_id").HasMaxLength(128);
        builder.Property(x => x.Narrative).HasColumnName("narrative").IsRequired();
        builder.Property(x => x.IsConflicting).HasColumnName("is_conflicting").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ConflictSummary).HasColumnName("conflict_summary").HasMaxLength(2000);
        builder.Property(x => x.SourceReferences)
            .HasColumnName("source_refs_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.BriefingId });
        builder.HasIndex(x => new { x.BriefingId, x.SectionKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.GroupingType, x.GroupingKey });
        builder.HasIndex(x => new { x.CompanyId, x.CompanyEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.EventCorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.PriorityScore, x.SectionKey });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Briefing)
            .WithMany(x => x.Sections)
            .HasForeignKey(x => x.BriefingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyBriefingContributionConfiguration : IEntityTypeConfiguration<CompanyBriefingContribution>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingContribution> builder)
    {
        builder.ToTable("company_briefing_contributions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.SectionId).HasColumnName("section_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.SourceLabel).HasColumnName("source_label").HasMaxLength(300).IsRequired();
        builder.Property(x => x.SourceStatus).HasColumnName("source_status").HasMaxLength(100);
        builder.Property(x => x.SourceRoute).HasColumnName("source_route").HasMaxLength(2048);
        builder.Property(x => x.TimestampUtc).HasColumnName("contributed_at").IsRequired();
        builder.Property(x => x.ConfidenceScore).HasColumnName("confidence_score").HasPrecision(5, 4);
        builder.Property(x => x.ConfidenceMetadata)
            .HasColumnName("confidence_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CompanyEntityId).HasColumnName("company_entity_id");
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.EventCorrelationId).HasColumnName("event_correlation_id").HasMaxLength(128);
        builder.Property(x => x.Topic).HasColumnName("topic").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Narrative).HasColumnName("narrative").IsRequired();
        builder.Property(x => x.Assessment).HasColumnName("assessment").HasMaxLength(200);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SectionId });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.TimestampUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.CompanyEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId });
        builder.HasIndex(x => new { x.CompanyId, x.TaskId });
        builder.HasIndex(x => new { x.CompanyId, x.EventCorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Section)
            .WithMany(x => x.Contributions)
            .HasForeignKey(x => x.SectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyBriefingUpdateJobConfiguration : IEntityTypeConfiguration<CompanyBriefingUpdateJob>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingUpdateJob> builder)
    {
        builder.ToTable("company_briefing_update_jobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TriggerType)
            .HasColumnName("trigger_type")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingUpdateJobTriggerTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.BriefingType)
            .HasColumnName("briefing_type")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : CompanyBriefingTypeValues.Parse(value))
            .HasMaxLength(32);
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => CompanyBriefingUpdateJobStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5).IsRequired();
        builder.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        builder.Property(x => x.LastErrorCode).HasColumnName("last_error_code").HasMaxLength(256);
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4000);
        builder.Property(x => x.LastErrorDetails).HasColumnName("last_error_details").HasMaxLength(12000);
        builder.Property(x => x.LastFailureAt).HasColumnName("last_failure_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.FinalFailedAt).HasColumnName("final_failed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.SourceMetadata)
            .HasColumnName("source_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.StartedAt, x.CreatedAt });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.CreatedAt });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

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

internal sealed class TenantBriefingDefaultConfiguration : IEntityTypeConfiguration<TenantBriefingDefault>
{
    public void Configure(EntityTypeBuilder<TenantBriefingDefault> builder)
    {
        builder.ToTable("tenant_briefing_defaults");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
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

        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CompanyNotificationConfiguration : IEntityTypeConfiguration<CompanyNotification>
{
    private static CompanyNotificationType ParseCompanyNotificationType(string value) =>
        value switch
        {
            "approval_requested" => CompanyNotificationType.ApprovalRequested,
            "escalation" => CompanyNotificationType.Escalation,
            "workflow_failure" => CompanyNotificationType.WorkflowFailure,
            "briefing_available" => CompanyNotificationType.BriefingAvailable,
            "proactive_message" => CompanyNotificationType.ProactiveMessage,
            _ => CompanyNotificationType.BriefingAvailable
        };

    public void Configure(EntityTypeBuilder<CompanyNotification> builder)
    {
        builder.ToTable("company_notifications");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.BriefingId).HasColumnName("briefing_id");
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(value => value.ToStorageValue(), value => CompanyNotificationChannelValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(
                value => value.ToStorageValue(),
                value => CompanyNotificationStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Type)
            .HasColumnName("notification_type")
            .HasConversion(value => value.ToStorageValue(), value => ParseCompanyNotificationType(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion(value => value.ToStorageValue(), value => CompanyNotificationPriorityValues.Parse(value)).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RelatedEntityType).HasColumnName("related_entity_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RelatedEntityId).HasColumnName("related_entity_id");
        builder.Property(x => x.ActionUrl).HasColumnName("action_url").HasMaxLength(2048);
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault).IsRequired();
        builder.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ReadUtc).HasColumnName("read_at");
        builder.Property(x => x.ActionedUtc).HasColumnName("actioned_at");
        builder.Property(x => x.ActionedByUserId).HasColumnName("actioned_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Type, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.Priority, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.DedupeKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BriefingId });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Briefing).WithMany().HasForeignKey(x => x.BriefingId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    private static readonly ValueConverter<WorkflowTriggerType, string> TriggerTypeConverter =
        new(
            value => value.ToStorageValue(),
            value => ParseWorkflowTriggerType(value));

    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(100);
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.TriggerType)
            .HasColumnName("trigger_type")
            .HasConversion(TriggerTypeConverter)
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowTriggerTypeValues.DefaultType)
            .HasSentinel((WorkflowTriggerType)0)
            .IsRequired();
        builder.Property(x => x.DefinitionJson)
            .HasColumnName("definition_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Active).HasColumnName("active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Code, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Code });
        builder.HasIndex(x => new { x.CompanyId, x.Active });
        builder.HasIndex(x => new { x.CompanyId, x.Active, x.Department });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static WorkflowTriggerType ParseWorkflowTriggerType(string value) =>
        WorkflowTriggerTypeValues.TryParse(value, out var triggerType)
            ? triggerType
            : WorkflowTriggerTypeValues.DefaultType;
}

internal sealed class WorkflowTriggerConfiguration : IEntityTypeConfiguration<WorkflowTrigger>
{
    public void Configure(EntityTypeBuilder<WorkflowTrigger> builder)
    {
        builder.ToTable("workflow_triggers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CriteriaJson)
            .HasColumnName("criteria_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.EventName });
        builder.HasIndex(x => new { x.CompanyId, x.EventName, x.IsEnabled });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Definition)
            .WithMany(x => x.Triggers)
            .HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.ProcessedEvents)
            .WithOne(x => x.WorkflowTrigger)
            .HasForeignKey(x => x.WorkflowTriggerId);
    }
}

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DefinitionId).HasColumnName("definition_id").IsRequired();
        builder.Property(x => x.TriggerId).HasColumnName("trigger_id");
        builder.Property(x => x.TriggerSource)
            .HasColumnName("trigger_source")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowTriggerTypeValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowTriggerType.Manual)
            .HasSentinel((WorkflowTriggerType)0)
            .IsRequired();
        builder.Property(x => x.TriggerRef).HasColumnName("trigger_ref").HasMaxLength(200);
        builder.Property(x => x.State)
            .HasColumnName("state")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowInstanceStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowInstanceStatusValues.DefaultStatus)
            .HasSentinel((WorkflowInstanceStatus)0)
            .IsRequired();
        builder.Property(x => x.CurrentStep).HasColumnName("current_step").HasMaxLength(200);
        builder.Property(x => x.InputPayload)
            .HasColumnName("input_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OutputPayload)
            .HasColumnName("output_payload")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ContextJson)
            .HasColumnName("context_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.StartedUtc).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.StartedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.State });
        builder.HasIndex(x => new { x.CompanyId, x.State, x.UpdatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.DefinitionId, x.TriggerSource, x.TriggerRef })
            .HasFilter("trigger_ref IS NOT NULL")
            .IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Definition).WithMany(x => x.Instances).HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Trigger).WithMany().HasForeignKey(x => x.TriggerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProcessedWorkflowTriggerEventConfiguration : IEntityTypeConfiguration<ProcessedWorkflowTriggerEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedWorkflowTriggerEvent> builder)
    {
        builder.ToTable("processed_workflow_trigger_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.WorkflowTriggerId).HasColumnName("workflow_trigger_id").IsRequired();
        builder.Property(x => x.EventId).HasColumnName("event_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedWorkflowInstanceId).HasColumnName("created_workflow_instance_id");
        builder.Property(x => x.ProcessedUtc).HasColumnName("processed_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.EventId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ProcessedUtc });
        builder.HasIndex(x => x.WorkflowTriggerId);
        builder.HasIndex(x => x.CreatedWorkflowInstanceId)
            .HasFilter("created_workflow_instance_id IS NOT NULL");

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowTrigger)
            .WithMany(x => x.ProcessedEvents)
            .HasForeignKey(x => x.WorkflowTriggerId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.CreatedWorkflowInstance)
            .WithMany()
            .HasForeignKey(x => x.CreatedWorkflowInstanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ConditionTriggerEvaluationConfiguration : IEntityTypeConfiguration<ConditionTriggerEvaluation>
{
    public void Configure(EntityTypeBuilder<ConditionTriggerEvaluation> builder)
    {
        builder.ToTable("condition_trigger_evaluations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ConditionDefinitionId).HasColumnName("condition_definition_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.WorkflowTriggerId).HasColumnName("workflow_trigger_id");
        builder.Property(x => x.EvaluatedUtc).HasColumnName("evaluated_at").IsRequired();
        builder.Property(x => x.SourceType)
            .HasColumnName("source_type")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseSourceType(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SourceName).HasColumnName("source_name").HasMaxLength(200);
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(100);
        builder.Property(x => x.FieldPath).HasColumnName("field_path").HasMaxLength(200);
        builder.Property(x => x.Operator)
            .HasColumnName("operator")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseOperator(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.ValueType)
            .HasColumnName("value_type")
            .HasConversion(
                value => value.HasValue ? value.Value.ToStorageValue() : null,
                value => string.IsNullOrWhiteSpace(value) ? null : ConditionTriggerStorageValues.ParseValueType(value))
            .HasMaxLength(32);
        builder.Property(x => x.RepeatFiringMode)
            .HasColumnName("repeat_firing_mode")
            .HasConversion(value => value.ToStorageValue(), value => ConditionTriggerStorageValues.ParseRepeatFiringMode(value))
            .HasMaxLength(64)
            .HasDefaultValue(RepeatFiringMode.FalseToTrueTransition)
            .HasSentinel((RepeatFiringMode)0)
            .IsRequired();
        builder.Property(x => x.InputValues)
            .HasColumnName("input_values_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PreviousOutcome).HasColumnName("previous_outcome");
        builder.Property(x => x.CurrentOutcome).HasColumnName("current_outcome").IsRequired();
        builder.Property(x => x.Fired).HasColumnName("fired").IsRequired();
        builder.Property(x => x.Diagnostic).HasColumnName("diagnostic").HasMaxLength(2000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.ConditionDefinitionId, x.EvaluatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowTriggerId, x.EvaluatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Fired, x.EvaluatedUtc });

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowTrigger)
            .WithMany()
            .HasForeignKey(x => x.WorkflowTriggerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class WorkflowExceptionConfiguration : IEntityTypeConfiguration<WorkflowException>
{
    public void Configure(EntityTypeBuilder<WorkflowException> builder)
    {
        builder.ToTable("workflow_exceptions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(x => x.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExceptionType)
            .HasColumnName("exception_type")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowExceptionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => WorkflowExceptionStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(WorkflowExceptionStatusValues.DefaultStatus)
            .HasSentinel((WorkflowExceptionStatus)0)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Details).HasColumnName("details").HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
        builder.Property(x => x.TechnicalDetailsJson)
            .HasColumnName("technical_details_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.ReviewedUtc).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ResolutionNotes).HasColumnName("resolution_notes").HasMaxLength(2000);

        builder.HasIndex(x => new { x.CompanyId, x.Status, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.WorkflowInstanceId, x.StepKey, x.ExceptionType, x.Status })
            .HasFilter("\"status\" = 'open'")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkflowInstance)
            .WithMany(x => x.Exceptions)
            .HasForeignKey(x => x.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Definition)
            .WithMany(x => x.Exceptions)
            .HasForeignKey(x => x.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProactiveMessageConfiguration : IEntityTypeConfiguration<ProactiveMessage>
{
    public void Configure(EntityTypeBuilder<ProactiveMessage> builder)
    {
        builder.ToTable("proactive_messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageChannelValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.SourceEntityType)
            .HasColumnName("source_entity_type")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageSourceEntityTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.OriginatingAgentId).HasColumnName("originating_agent_id").IsRequired();
        builder.Property(x => x.NotificationId).HasColumnName("notification_id");
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => value == "delivered" ? ProactiveMessageDeliveryStatus.Delivered : ProactiveMessageDeliveryStatus.Blocked)
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.SentUtc).HasColumnName("sent_at").IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecisionReason).HasColumnName("policy_decision_reason").HasMaxLength(200);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.RecipientUserId, x.SentUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Channel, x.SentUtc });
        builder.HasIndex(x => x.NotificationId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.RecipientUser).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginatingAgent).WithMany().HasForeignKey(x => x.OriginatingAgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Notification).WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ProactiveMessagePolicyDecisionConfiguration : IEntityTypeConfiguration<ProactiveMessagePolicyDecision>
{
    public void Configure(EntityTypeBuilder<ProactiveMessagePolicyDecision> builder)
    {
        builder.ToTable("proactive_message_policy_decisions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProactiveMessageId).HasColumnName("proactive_message_id");
        builder.Property(x => x.Channel)
            .HasColumnName("channel")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageChannelValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id").IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.SourceEntityType)
            .HasColumnName("source_entity_type")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessageSourceEntityTypeValues.Parse(value))
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.OriginatingAgentId).HasColumnName("originating_agent_id").IsRequired();
        builder.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasConversion(value => value.ToStorageValue(), value => ProactiveMessagePolicyDecisionOutcomeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(200);
        builder.Property(x => x.ReasonSummary).HasColumnName("reason_summary").HasMaxLength(2000);
        builder.Property(x => x.EvaluatedAutonomyLevel)
            .HasColumnName("evaluated_autonomy_level")
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Outcome, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Channel, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.RecipientUserId, x.CreatedUtc });
        builder.HasIndex(x => x.ProactiveMessageId);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ProactiveMessage).WithMany().HasForeignKey(x => x.ProactiveMessageId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.RecipientUser).WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OriginatingAgent).WithMany().HasForeignKey(x => x.OriginatingAgentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CompanyBriefingSeverityRuleConfiguration : IEntityTypeConfiguration<CompanyBriefingSeverityRule>
{
    public void Configure(EntityTypeBuilder<CompanyBriefingSeverityRule> builder)
    {
        builder.ToTable("company_briefing_severity_rules");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RuleCode).HasColumnName("rule_code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.SectionType).HasColumnName("section_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ConditionKey).HasColumnName("condition_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConditionValue).HasColumnName("condition_value").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriorityCategory)
            .HasColumnName("priority_category")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSectionPriorityCategoryValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.PriorityScore).HasColumnName("priority_score").IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => BriefingSeverityRuleStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(BriefingSeverityRuleStatus.Active)
            .HasSentinel((BriefingSeverityRuleStatus)0)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.RuleCode }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.SectionType, x.EntityType, x.ConditionKey, x.ConditionValue });
        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ToolExecutionAttemptId);
        builder.Property(x => x.RequestedByUserId).IsRequired();
        builder.Property(x => x.TargetEntityType).HasColumnName("entity_type").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TargetEntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.RequestedByActorType).HasColumnName("requested_by_actor_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequestedByActorId).HasColumnName("requested_by_actor_id").IsRequired();
        builder.Property(x => x.ApprovalType).HasColumnName("approval_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActionType)
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RequiredRole).HasColumnName("required_role").HasMaxLength(100);
        builder.Property(x => x.RequiredUserId).HasColumnName("required_user_id");
        builder.Property(x => x.DecisionSummary).HasColumnName("decision_summary").HasMaxLength(2000);
        builder.Property(x => x.ApprovalTarget).HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => ApprovalRequestStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ThresholdContext)
            .HasColumnName("threshold_context_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.DecisionChain)
            .HasColumnName("decision_chain_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.HasMany(x => x.Steps)
            .WithOne(x => x.Approval)
            .HasForeignKey(x => x.ApprovalId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TargetEntityType, x.TargetEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => x.ToolExecutionAttemptId).IsUnique().HasFilter("[ToolExecutionAttemptId] IS NOT NULL");
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApprovalStepConfiguration : IEntityTypeConfiguration<ApprovalStep>
{
    public void Configure(EntityTypeBuilder<ApprovalStep> builder)
    {
        builder.ToTable("approval_steps");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ApprovalId).IsRequired();
        builder.Property(x => x.SequenceNo).HasColumnName("sequence_no").IsRequired();
        builder.Property(x => x.ApproverType)
            .HasColumnName("approver_type")
            .HasConversion(value => value.ToStorageValue(), value => ApprovalStepApproverTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.ApproverRef)
            .HasColumnName("approver_ref")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => ApprovalStepStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.DecidedByUserId).HasColumnName("decided_by_user_id");
        builder.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        builder.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(2000);

        builder.HasIndex(x => new { x.ApprovalId, x.SequenceNo }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}

internal sealed class AgentScheduledTriggerConfiguration : IEntityTypeConfiguration<AgentScheduledTrigger>
{
    public void Configure(EntityTypeBuilder<AgentScheduledTrigger> builder)
    {
        builder.ToTable("agent_scheduled_triggers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id }).HasName("AK_agent_scheduled_triggers_company_id_id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CronExpression).HasColumnName("cron_expression").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TimeZoneId).HasColumnName("timezone").HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.NextRunUtc).HasColumnName("next_run_at");
        builder.Property(x => x.EnabledUtc).HasColumnName("enabled_at");
        builder.Property(x => x.LastEvaluatedUtc).HasColumnName("last_evaluated_at");
        builder.Property(x => x.LastEnqueuedUtc).HasColumnName("last_enqueued_at");
        builder.Property(x => x.LastRunUtc).HasColumnName("last_run_at");
        builder.Property(x => x.DisabledUtc).HasColumnName("disabled_at");
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.AgentId });
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.IsEnabled, x.NextRunUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.IsEnabled });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.EnqueueWindows)
            .WithOne(x => x.ScheduledTrigger)
            .HasForeignKey(x => new { x.CompanyId, x.ScheduledTriggerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AgentScheduledTriggerEnqueueWindowConfiguration : IEntityTypeConfiguration<AgentScheduledTriggerEnqueueWindow>
{
    public void Configure(EntityTypeBuilder<AgentScheduledTriggerEnqueueWindow> builder)
    {
        builder.ToTable("agent_scheduled_trigger_enqueue_windows");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ScheduledTriggerId).HasColumnName("scheduled_trigger_id").IsRequired();
        builder.Property(x => x.WindowStartUtc).HasColumnName("window_start_at").IsRequired();
        builder.Property(x => x.WindowEndUtc).HasColumnName("window_end_at").IsRequired();
        builder.Property(x => x.EnqueuedUtc).HasColumnName("enqueued_at").IsRequired();
        builder.Property(x => x.ExecutionRequestId).HasColumnName("execution_request_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.ScheduledTriggerId, x.WindowStartUtc, x.WindowEndUtc }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.EnqueuedUtc });
        builder.HasIndex(x => x.ExecutionRequestId)
            .HasFilter("execution_request_id IS NOT NULL");

        builder.HasCheckConstraint(
            "CK_agent_scheduled_trigger_enqueue_windows_window_order",
            ActiveProviderConstraint.WindowEndAfterStart);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.ScheduledTrigger)
            .WithMany(x => x.EnqueueWindows)
            .HasForeignKey(x => new { x.CompanyId, x.ScheduledTriggerId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TriggerExecutionAttemptConfiguration : IEntityTypeConfiguration<TriggerExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<TriggerExecutionAttempt> builder)
    {
        builder.ToTable("trigger_execution_attempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.TriggerId).HasColumnName("trigger_id").IsRequired();
        builder.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.OccurrenceUtc).HasColumnName("occurrence_at").IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => TriggerExecutionAttemptStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(TriggerExecutionAttemptStatusValues.DefaultStatus)
            .HasSentinel((TriggerExecutionAttemptStatus)0)
            .IsRequired();
        builder.Property(x => x.DenialReason).HasColumnName("denial_reason").HasMaxLength(2000);
        builder.Property(x => x.RetryAttemptCount).HasColumnName("retry_attempt_count").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.FailureDetails).HasColumnName("failure_details").HasMaxLength(4000);
        builder.Property(x => x.DispatchReferenceType).HasColumnName("dispatch_reference_type").HasMaxLength(100);
        builder.Property(x => x.DispatchReferenceId).HasColumnName("dispatch_reference_id").HasMaxLength(128);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.NextRetryUtc).HasColumnName("next_retry_at");
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.TriggerType, x.Status, x.OccurrenceUtc });
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.OccurrenceUtc });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => x.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion(value => value.ToStorageValue(), value => AlertTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Severity)
            .HasColumnName("severity")
            .HasConversion(value => value.ToStorageValue(), value => AlertSeverityValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Evidence)
            .HasColumnName("evidence_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => AlertStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(AlertStatusValues.DefaultStatus)
            .HasSentinel((AlertStatus)0)
            .IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(256).IsRequired();
        builder.Property(x => x.SourceAgentId).HasColumnName("source_agent_id");
        builder.Property(x => x.OccurrenceCount).HasColumnName("occurrence_count").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.SourceLifecycleVersion).HasColumnName("source_lifecycle_version").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.LastDetectedUtc).HasColumnName("last_detected_at");
        builder.Property(x => x.ResolvedUtc).HasColumnName("resolved_at");
        builder.Property(x => x.ClosedUtc).HasColumnName("closed_at");

        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Type, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Severity, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Fingerprint });
        builder.HasIndex(x => new { x.CompanyId, x.Fingerprint })
            .HasFilter("\"status\" IN ('open', 'acknowledged')")
            .IsUnique();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.SourceAgent).WithMany().HasForeignKey(x => x.SourceAgentId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.ToTable("activity_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.AgentId).HasColumnName("agent_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.OccurredUtc).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.SourceMetadata)
            .HasColumnName("source_metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.Department).HasColumnName("department").HasMaxLength(100);
        builder.Property(x => x.TaskId).HasColumnName("task_id");
        builder.Property(x => x.AuditEventId).HasColumnName("audit_event_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.OccurredUtc, x.Id }).IsDescending(false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.Department, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.TaskId, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.EventType, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.OccurredUtc, x.Id }).IsDescending(false, false, true, true);
        builder.HasIndex(x => new { x.CompanyId, x.AuditEventId });
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId, x.OccurredUtc, x.Id });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Agent)
            .WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.AgentId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class EscalationConfiguration : IEntityTypeConfiguration<Escalation>
{
    public void Configure(EntityTypeBuilder<Escalation> builder)
    {
        builder.ToTable("escalations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.PolicyId).HasColumnName("policy_id").IsRequired();
        builder.Property(x => x.SourceEntityId).HasColumnName("source_entity_id").IsRequired();
        builder.Property(x => x.SourceEntityType).HasColumnName("source_entity_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.EscalationLevel).HasColumnName("escalation_level").IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.TriggeredUtc).HasColumnName("triggered_at").IsRequired();
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128);
        builder.Property(x => x.LifecycleVersion).HasColumnName("lifecycle_version").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(value => value.ToStorageValue(), value => EscalationStatusValues.Parse(value))
            .HasMaxLength(32)
            .HasDefaultValue(EscalationStatusValues.DefaultStatus)
            .HasSentinel((EscalationStatus)0)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SourceEntityType, x.SourceEntityId });
        builder.HasIndex(x => new { x.CompanyId, x.PolicyId, x.SourceEntityType, x.SourceEntityId, x.EscalationLevel, x.LifecycleVersion })
            .IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        builder.HasIndex(x => new { x.CompanyId, x.TriggeredUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class InsightAcknowledgmentConfiguration : IEntityTypeConfiguration<InsightAcknowledgment>
{
    public void Configure(EntityTypeBuilder<InsightAcknowledgment> builder)
    {
        builder.ToTable("insight_acknowledgments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.InsightKey).HasColumnName("insight_key").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AcknowledgedUtc).HasColumnName("acknowledged_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.InsightKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.UserId, x.AcknowledgedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal static class ActiveProviderConstraint
{
    // EF model configuration is provider-agnostic here; the expression is valid for SQL Server and SQLite.
    // The migration supplies a PostgreSQL-specific expression at apply time.
    public const string WindowEndAfterStart = "window_end_at > window_start_at";
}
