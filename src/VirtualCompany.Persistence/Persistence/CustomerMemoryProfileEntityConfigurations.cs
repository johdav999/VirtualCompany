using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CustomerMemoryProfileConfiguration : IEntityTypeConfiguration<CustomerMemoryProfile>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfile> builder)
    {
        builder.ToTable("customer_memory_profiles");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ContactId).HasColumnName("contact_id").IsRequired();
        builder.Property(x => x.AiSummary).HasColumnName("ai_summary").HasMaxLength(4000);
        builder.Property(x => x.RelationshipMemory).HasColumnName("relationship_memory").HasMaxLength(4000);
        builder.Property(x => x.LastOutreachSummary).HasColumnName("last_outreach_summary").HasMaxLength(4000);
        builder.Property(x => x.EngagementScore).HasColumnName("engagement_score").HasColumnType("decimal(5,2)");
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_customer_memory_profiles_engagement_score_range", "engagement_score IS NULL OR (engagement_score >= 0 AND engagement_score <= 100)");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => x.ContactId);
        builder.HasIndex(x => new { x.CompanyId, x.ContactId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Contact)
            .WithMany()
            .HasForeignKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.ContactId))
            .HasPrincipalKey(nameof(Contact.CompanyId), nameof(Contact.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerMemoryProfileConversationConfiguration : IEntityTypeConfiguration<CustomerMemoryProfileConversation>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfileConversation> builder)
    {
        builder.ToTable("customer_memory_profile_conversations");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.ConversationId).HasColumnName("conversation_id").IsRequired();
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000);
        builder.Property(x => x.LastMessageUtc).HasColumnName("last_message_at");
        builder.Property(x => x.Relevance).HasColumnName("relevance").HasColumnType("decimal(5,3)");
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_customer_memory_profile_conversations_relevance_range", "relevance IS NULL OR (relevance >= 0 AND relevance <= 1)");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.ConversationId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.ConversationId });
        builder.HasIndex(x => new { x.CompanyId, x.LastMessageUtc });
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.Conversations)
            .HasForeignKey(nameof(CustomerMemoryProfileConversation.CompanyId), nameof(CustomerMemoryProfileConversation.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Conversation)
            .WithMany()
            .HasForeignKey(nameof(CustomerMemoryProfileConversation.CompanyId), nameof(CustomerMemoryProfileConversation.ConversationId))
            .HasPrincipalKey(nameof(Conversation.CompanyId), nameof(Conversation.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerMemoryProfileDealConfiguration : IEntityTypeConfiguration<CustomerMemoryProfileDeal>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfileDeal> builder)
    {
        builder.ToTable("customer_memory_profile_deals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.DealId).HasColumnName("deal_id").IsRequired();
        builder.Property(x => x.DealRole).HasColumnName("deal_role").HasMaxLength(80);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(80);
        builder.Property(x => x.ClosedUtc).HasColumnName("closed_at");
        builder.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(2000);
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.DealId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.DealId });
        builder.HasIndex(x => new { x.CompanyId, x.Outcome, x.ClosedUtc });
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.Deals)
            .HasForeignKey(nameof(CustomerMemoryProfileDeal.CompanyId), nameof(CustomerMemoryProfileDeal.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Deal)
            .WithMany()
            .HasForeignKey(nameof(CustomerMemoryProfileDeal.CompanyId), nameof(CustomerMemoryProfileDeal.DealId))
            .HasPrincipalKey(nameof(Deal.CompanyId), nameof(Deal.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerMemoryProfileEngagementAttributeConfiguration : IEntityTypeConfiguration<CustomerMemoryProfileEngagementAttribute>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfileEngagementAttribute> builder)
    {
        builder.ToTable("customer_memory_profile_engagement_attributes");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.AttributeType).HasColumnName("attribute_type").HasMaxLength(80).IsRequired();
        builder.Property(x => x.AttributeKey).HasColumnName("attribute_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.AttributeValue).HasColumnName("attribute_value").HasMaxLength(1000);
        builder.Property(x => x.ScoreImpact).HasColumnName("score_impact").HasColumnType("decimal(6,3)");
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.Metadata)
            .HasColumnName("metadata_json")
            .HasJsonConversion<Dictionary<string, JsonNode?>>()
            .HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonObjectDefault)
            .IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_customer_memory_profile_engagement_attributes_score_impact_range", "score_impact IS NULL OR (score_impact >= -100 AND score_impact <= 100)");
        });

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.AttributeType, x.AttributeKey });
        builder.HasIndex(x => new { x.CompanyId, x.AttributeType, x.ObservedUtc });
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.EngagementAttributes)
            .HasForeignKey(nameof(CustomerMemoryProfileEngagementAttribute.CompanyId), nameof(CustomerMemoryProfileEngagementAttribute.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CustomerMemoryProfilePreferenceConfiguration : IEntityTypeConfiguration<CustomerMemoryProfilePreference>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfilePreference> builder)
    {
        builder.ToTable("customer_memory_profile_preferences");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        ConfigureSignalColumns(builder);
        builder.Property(x => x.PreferenceKey).HasColumnName("preference_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.PreferenceValue).HasColumnName("preference_value").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SourceSummary).HasColumnName("source_summary").HasMaxLength(1000);

        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.PreferenceKey });
        builder.HasIndex(x => new { x.CompanyId, x.PreferenceKey, x.ObservedUtc });
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.Preferences)
            .HasForeignKey(nameof(CustomerMemoryProfilePreference.CompanyId), nameof(CustomerMemoryProfilePreference.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSignalColumns(EntityTypeBuilder<CustomerMemoryProfilePreference> builder)
    {
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,3)");
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_customer_memory_profile_preferences_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)"));
    }
}

internal sealed class CustomerMemoryProfilePriceSignalConfiguration : IEntityTypeConfiguration<CustomerMemoryProfilePriceSignal>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfilePriceSignal> builder)
    {
        builder.ToTable("customer_memory_profile_price_signals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        ConfigureSignalColumns(builder);
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.PriceSignals)
            .HasForeignKey(nameof(CustomerMemoryProfilePriceSignal.CompanyId), nameof(CustomerMemoryProfilePriceSignal.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSignalColumns(EntityTypeBuilder<CustomerMemoryProfilePriceSignal> builder)
    {
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.SignalKey).HasColumnName("signal_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.SignalValue).HasColumnName("signal_value").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,3)");
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.SourceSummary).HasColumnName("source_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_customer_memory_profile_price_signals_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)"));
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.SignalKey });
        builder.HasIndex(x => new { x.CompanyId, x.SignalKey, x.ObservedUtc });
    }
}

internal sealed class CustomerMemoryProfileIndustrySignalConfiguration : IEntityTypeConfiguration<CustomerMemoryProfileIndustrySignal>
{
    public void Configure(EntityTypeBuilder<CustomerMemoryProfileIndustrySignal> builder)
    {
        builder.ToTable("customer_memory_profile_industry_signals");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        ConfigureSignalColumns(builder);
        builder.HasOne(x => x.CustomerMemoryProfile)
            .WithMany(x => x.IndustrySignals)
            .HasForeignKey(nameof(CustomerMemoryProfileIndustrySignal.CompanyId), nameof(CustomerMemoryProfileIndustrySignal.CustomerMemoryProfileId))
            .HasPrincipalKey(nameof(CustomerMemoryProfile.CompanyId), nameof(CustomerMemoryProfile.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureSignalColumns(EntityTypeBuilder<CustomerMemoryProfileIndustrySignal> builder)
    {
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.CustomerMemoryProfileId).HasColumnName("customer_memory_profile_id").IsRequired();
        builder.Property(x => x.SignalKey).HasColumnName("signal_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.SignalValue).HasColumnName("signal_value").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Confidence).HasColumnName("confidence").HasColumnType("decimal(5,3)");
        builder.Property(x => x.ObservedUtc).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.SourceSummary).HasColumnName("source_summary").HasMaxLength(1000);
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.ToTable(t => t.HasCheckConstraint("CK_customer_memory_profile_industry_signals_confidence_range", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)"));
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.CompanyId, x.CustomerMemoryProfileId, x.SignalKey });
        builder.HasIndex(x => new { x.CompanyId, x.SignalKey, x.ObservedUtc });
    }
}
