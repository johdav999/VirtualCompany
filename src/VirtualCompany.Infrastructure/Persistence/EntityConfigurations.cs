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
    private const string JsonObjectDefaultSql = "'{}'";
    private const string JsonArrayDefaultSql = "'[]'";

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

        propertyBuilder.HasColumnType("jsonb");
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
            .OnDelete(DeleteBehavior.Cascade);

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
        builder.Property(x => x.OccurredUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.AvailableUtc).IsRequired();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastAttemptUtc);
        builder.Property(x => x.LastError).HasMaxLength(4000);
        builder.Property(x => x.ClaimToken).HasMaxLength(64).IsConcurrencyToken();
        builder.Property(x => x.ProcessedUtc).IsConcurrencyToken();

        builder.HasIndex(x => new { x.ProcessedUtc, x.AvailableUtc, x.AttemptCount });
        builder.HasIndex(x => new { x.ProcessedUtc, x.ClaimedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Topic, x.IdempotencyKey }).HasFilter("\"IdempotencyKey\" IS NOT NULL").IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ProcessedUtc });

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
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
        builder.Property(x => x.DataSources)
            .HasColumnName("data_sources_json")
            .HasJsonConversion<List<string>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault)
            .IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, string?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.OccurredUtc });
        builder.HasIndex(x => new { x.CompanyId, x.TargetType, x.TargetId, x.OccurredUtc });

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

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.ToTable("agents");

        builder.HasKey(x => x.Id);
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
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.Department });
        builder.HasIndex(x => new { x.CompanyId, x.DisplayName });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ToolExecutionAttemptConfiguration : IEntityTypeConfiguration<ToolExecutionAttempt>
{
    public void Configure(EntityTypeBuilder<ToolExecutionAttempt> builder)
    {
        builder.ToTable("tool_execution_attempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActionType)
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(100);
        builder.Property(x => x.Status)
            .HasConversion(value => value.ToStorageValue(), value => ToolExecutionStatusValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.RequestPayload)
            .HasColumnName("request_payload_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.PolicyDecision)
            .HasColumnName("policy_decision_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.ResultPayload)
            .HasColumnName("result_payload_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.Property(x => x.ExecutedUtc);
        builder.HasIndex(x => new { x.CompanyId, x.AgentId, x.CreatedUtc });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("approval_requests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AgentId).IsRequired();
        builder.Property(x => x.ToolExecutionAttemptId).IsRequired();
        builder.Property(x => x.RequestedByUserId).IsRequired();
        builder.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ActionType)
            .HasConversion(value => value.ToStorageValue(), value => ToolActionTypeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
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
        builder.Property(x => x.CreatedUtc).IsRequired();
        builder.Property(x => x.UpdatedUtc).IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
        builder.HasIndex(x => x.ToolExecutionAttemptId).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}
