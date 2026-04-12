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
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId });
        builder.HasIndex(x => new { x.CompanyId, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ParentTaskId });

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
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");

        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.AssignedAgentId });
        builder.HasIndex(x => new { x.CompanyId, x.DueUtc });
        builder.HasIndex(x => new { x.CompanyId, x.ParentTaskId });

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
