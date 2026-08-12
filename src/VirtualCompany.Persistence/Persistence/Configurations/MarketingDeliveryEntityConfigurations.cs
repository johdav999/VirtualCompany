using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class MarketingChannelOAuthSessionConfiguration : IEntityTypeConfiguration<MarketingChannelOAuthSession>
{
    public void Configure(EntityTypeBuilder<MarketingChannelOAuthSession> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_channel_oauth_sessions");
        MarketingCreativeAssetConfiguration.S(b,x=>x.Provider,"provider",32);
        b.Property(x=>x.UserId).HasColumnName("user_id");
        MarketingCreativeAssetConfiguration.S(b,x=>x.StateHash,"state_hash",128);
        MarketingCreativeAssetConfiguration.S(b,x=>x.RedirectUri,"redirect_uri",2000);
        MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32);
        b.Property(x=>x.ExpiresUtc).HasColumnName("expires_at"); b.Property(x=>x.CreatedUtc).HasColumnName("created_at"); b.Property(x=>x.ConsumedUtc).HasColumnName("consumed_at");
        b.HasIndex(x=>new{x.CompanyId,x.StateHash}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.Status,x.ExpiresUtc});
    }
}

internal sealed class MarketingChannelDestinationConfiguration : IEntityTypeConfiguration<MarketingChannelDestination>
{
    public void Configure(EntityTypeBuilder<MarketingChannelDestination> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_channel_destinations");
        b.Property(x=>x.MarketingChannelConnectionId).HasColumnName("marketing_channel_connection_id");
        MarketingCreativeAssetConfiguration.S(b,x=>x.ProviderReference,"provider_reference",500);
        MarketingCreativeAssetConfiguration.S(b,x=>x.DisplayName,"display_name",200);
        MarketingCreativeAssetConfiguration.S(b,x=>x.DestinationType,"destination_type",64);
        b.Property(x=>x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("nvarchar(max)");
        b.Property(x=>x.SecretReference).HasColumnName("secret_reference").HasMaxLength(500);
        MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32);
        b.Property(x=>x.LastDiscoveredUtc).HasColumnName("last_discovered_at"); MarketingCreativeAssetConfiguration.Times(b);
        b.HasIndex(x=>new{x.CompanyId,x.MarketingChannelConnectionId,x.ProviderReference}).IsUnique();
        b.HasIndex(x=>new{x.CompanyId,x.Status});
        b.HasOne<MarketingChannelConnection>().WithMany()
            .HasForeignKey(x=>new{x.CompanyId,x.MarketingChannelConnectionId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingCreativeAssetConfiguration : IEntityTypeConfiguration<MarketingCreativeAsset>
{
    public void Configure(EntityTypeBuilder<MarketingCreativeAsset> b) { MarketingStrategicConfiguration.Identity(b, "marketing_creative_assets"); S(b, x => x.Name, "name", 200); S(b, x => x.MediaType, "media_type", 80); S(b, x => x.Dimensions, "dimensions", 40); S(b, x => x.Language, "language", 20); S(b, x => x.PromptVersion, "prompt_version", 64); S(b, x => x.BrandProfileVersion, "brand_profile_version", 64); S(b, x => x.Checksum, "checksum", 128); S(b, x => x.Status, "status", 32); S(b, x => x.IdempotencyKey, "idempotency_key", 160); b.Property(x => x.MarketingContentBriefId).HasColumnName("marketing_content_brief_id"); b.Property(x => x.MarketingContentVariantId).HasColumnName("marketing_content_variant_id"); b.Property(x => x.SalesCampaignId).HasColumnName("sales_campaign_id"); b.Property(x => x.AssetFamilyId).HasColumnName("asset_family_id"); b.Property(x => x.VersionNumber).HasColumnName("version_number"); b.Property(x => x.GenerationSummary).HasColumnName("generation_summary").HasMaxLength(4000); b.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(500); b.Property(x => x.SafetyResult).HasColumnName("safety_result").HasMaxLength(1000); b.Property(x => x.AltText).HasColumnName("alt_text").HasMaxLength(1000); b.Property(x => x.StorageReference).HasColumnName("storage_reference").HasMaxLength(2000); b.Property(x => x.SourceAssetIdsJson).HasColumnName("source_asset_ids_json").HasColumnType("nvarchar(max)"); b.Property(x => x.ProvenanceJson).HasColumnName("provenance_json").HasColumnType("nvarchar(max)"); S(b, x => x.AuditReference, "audit_reference", 200); b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); Times(b); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.AssetFamilyId, x.VersionNumber }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.MarketingContentVariantId }); }
    internal static void S<T>(EntityTypeBuilder<T> b, System.Linq.Expressions.Expression<Func<T,string>> p, string name, int max) where T:class => b.Property(p).HasColumnName(name).HasMaxLength(max).IsRequired();
    internal static void Times<T>(EntityTypeBuilder<T> b) where T:class { b.Property("CreatedUtc").HasColumnName("created_at"); b.Property("UpdatedUtc").HasColumnName("updated_at"); }
}
internal sealed class MarketingCreativeAssetScanConfiguration : IEntityTypeConfiguration<MarketingCreativeAssetScan>
{
    public void Configure(EntityTypeBuilder<MarketingCreativeAssetScan> b)
    {
        MarketingStrategicConfiguration.Identity(b, "marketing_creative_asset_scans");
        b.Property(x=>x.MarketingCreativeAssetId).HasColumnName("marketing_creative_asset_id");
        MarketingCreativeAssetConfiguration.S(b,x=>x.Provider,"provider",100);
        MarketingCreativeAssetConfiguration.S(b,x=>x.ProviderReference,"provider_reference",300);
        MarketingCreativeAssetConfiguration.S(b,x=>x.ScannerVersion,"scanner_version",100);
        MarketingCreativeAssetConfiguration.S(b,x=>x.Result,"result",20);
        MarketingCreativeAssetConfiguration.S(b,x=>x.ReasonCode,"reason_code",100);
        b.Property(x=>x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)");
        b.Property(x=>x.ScannedUtc).HasColumnName("scanned_at"); b.Property(x=>x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x=>new{x.CompanyId,x.MarketingCreativeAssetId,x.ScannedUtc});
        b.HasOne<MarketingCreativeAsset>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.MarketingCreativeAssetId})
            .HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Cascade);
    }
}
internal sealed class MarketingChannelConnectionConfiguration : IEntityTypeConfiguration<MarketingChannelConnection>
{ public void Configure(EntityTypeBuilder<MarketingChannelConnection> b) { MarketingStrategicConfiguration.Identity(b,"marketing_channel_connections"); MarketingCreativeAssetConfiguration.S(b,x=>x.Provider,"provider",32); MarketingCreativeAssetConfiguration.S(b,x=>x.ExternalAccountReference,"external_account_reference",500); MarketingCreativeAssetConfiguration.S(b,x=>x.DisplayName,"display_name",200); b.Property(x=>x.CapabilitiesJson).HasColumnName("capabilities_json").HasColumnType("nvarchar(max)"); b.Property(x=>x.SecretReference).HasColumnName("secret_reference").HasMaxLength(500); b.Property(x=>x.OwnerUserId).HasColumnName("owner_user_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32); MarketingCreativeAssetConfiguration.S(b,x=>x.HealthStatus,"health_status",40); b.Property(x=>x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x=>x.LastCheckedUtc).HasColumnName("last_checked_at"); MarketingCreativeAssetConfiguration.Times(b); b.HasIndex(x=>new{x.CompanyId,x.Provider,x.ExternalAccountReference}).IsUnique(); } }
internal sealed class MarketingChannelActionConfiguration : IEntityTypeConfiguration<MarketingChannelAction>
{ public void Configure(EntityTypeBuilder<MarketingChannelAction> b) { MarketingStrategicConfiguration.Identity(b,"marketing_channel_actions"); b.Property(x=>x.MarketingChannelConnectionId).HasColumnName("marketing_channel_connection_id"); b.Property(x=>x.SalesCampaignId).HasColumnName("sales_campaign_id"); b.Property(x=>x.MarketingContentBriefId).HasColumnName("marketing_content_brief_id"); b.Property(x=>x.ContentBriefVersion).HasColumnName("content_brief_version"); MarketingCreativeAssetConfiguration.S(b,x=>x.DestinationReference,"destination_reference",500); MarketingCreativeAssetConfiguration.S(b,x=>x.ActionType,"action_type",80); b.Property(x=>x.PayloadJson).HasColumnName("payload_json").HasColumnType("nvarchar(max)"); b.Property(x=>x.ScheduledUtc).HasColumnName("scheduled_at"); MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",200); b.Property(x=>x.ApprovalRequestId).HasColumnName("approval_request_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32); b.Property(x=>x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x=>x.AttemptCount).HasColumnName("attempt_count"); b.Property(x=>x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(500); b.Property(x=>x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); MarketingCreativeAssetConfiguration.Times(b); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.Status,x.ScheduledUtc}); } }
internal sealed class MarketingLifecycleJourneyConfiguration : IEntityTypeConfiguration<MarketingLifecycleJourney>
{ public void Configure(EntityTypeBuilder<MarketingLifecycleJourney> b) { MarketingStrategicConfiguration.Identity(b,"marketing_lifecycle_journeys"); MarketingCreativeAssetConfiguration.S(b,x=>x.Name,"name",200); Json(b,x=>x.AudienceEligibilityJson,"audience_eligibility_json"); Json(b,x=>x.EntryExitCriteriaJson,"entry_exit_criteria_json"); Json(b,x=>x.StepsJson,"steps_json"); Json(b,x=>x.GuardrailsJson,"guardrails_json"); b.Property(x=>x.FrequencyCap).HasColumnName("frequency_cap"); b.Property(x=>x.ValidFromUtc).HasColumnName("valid_from_at"); b.Property(x=>x.ValidToUtc).HasColumnName("valid_to_at"); b.Property(x=>x.OwnerUserId).HasColumnName("owner_user_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",160); b.Property(x=>x.SupersedesJourneyId).HasColumnName("supersedes_journey_id"); b.Property(x=>x.MarketingCustomerSegmentVersionId).HasColumnName("marketing_customer_segment_version_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32); b.Property(x=>x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x=>x.Version).HasColumnName("version"); b.Property(x=>x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken(); MarketingCreativeAssetConfiguration.Times(b); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.SupersedesJourneyId,x.Version}); b.HasOne<MarketingLifecycleJourney>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.SupersedesJourneyId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict); b.HasOne<MarketingCustomerSegmentVersion>().WithMany().HasForeignKey(x=>new{x.CompanyId,x.MarketingCustomerSegmentVersionId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.Restrict); } private static void Json(EntityTypeBuilder<MarketingLifecycleJourney>b,System.Linq.Expressions.Expression<Func<MarketingLifecycleJourney,string>>p,string n)=>b.Property(p).HasColumnName(n).HasColumnType("nvarchar(max)"); }
internal sealed class MarketingJourneyEnrollmentConfiguration : IEntityTypeConfiguration<MarketingJourneyEnrollment>
{
    public void Configure(EntityTypeBuilder<MarketingJourneyEnrollment> b)
    {
        MarketingStrategicConfiguration.Identity(b,"marketing_journey_enrollments");
        b.Property(x=>x.MarketingLifecycleJourneyId).HasColumnName("marketing_lifecycle_journey_id");
        b.Property(x=>x.ContactId).HasColumnName("contact_id"); b.Property(x=>x.JourneyVersion).HasColumnName("journey_version");
        MarketingCreativeAssetConfiguration.S(b,x=>x.ConsentEvidenceReference,"consent_evidence_reference",500);
        MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",200);
        MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32); b.Property(x=>x.NextStepIndex).HasColumnName("next_step_index");
        b.Property(x=>x.NextStepUtc).HasColumnName("next_step_at"); b.Property(x=>x.ActionsInWindow).HasColumnName("actions_in_window");
        b.Property(x=>x.WindowStartedUtc).HasColumnName("window_started_at"); b.Property(x=>x.LastChannelActionId).HasColumnName("last_channel_action_id");
        b.Property(x=>x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); MarketingCreativeAssetConfiguration.Times(b);
        b.Property(x=>x.LeaseOwner).HasColumnName("lease_owner").HasMaxLength(128); b.Property(x=>x.LeaseExpiresUtc).HasColumnName("lease_expires_at");
        b.Property(x=>x.AttemptCount).HasColumnName("attempt_count"); b.Property(x=>x.MaximumAttempts).HasColumnName("maximum_attempts");
        b.Property(x=>x.NextAttemptUtc).HasColumnName("next_attempt_at"); b.Property(x=>x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        b.Property(x=>x.LastEvaluationJson).HasColumnName("last_evaluation_json").HasColumnType("nvarchar(max)");
        b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.Status,x.NextStepUtc});
        b.HasIndex(x=>new{x.CompanyId,x.MarketingLifecycleJourneyId,x.ContactId});
    }
}
internal sealed class MarketingJourneyInboundEventConfiguration : IEntityTypeConfiguration<MarketingJourneyInboundEvent>
{ public void Configure(EntityTypeBuilder<MarketingJourneyInboundEvent> b) { MarketingStrategicConfiguration.Identity(b,"marketing_journey_inbound_events"); b.Property(x=>x.MarketingLifecycleJourneyId).HasColumnName("journey_id"); b.Property(x=>x.JourneyVersion).HasColumnName("journey_version"); b.Property(x=>x.ContactId).HasColumnName("contact_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.EventType,"event_type",80); MarketingCreativeAssetConfiguration.S(b,x=>x.EventReference,"event_reference",300); b.Property(x=>x.OccurrenceVersion).HasColumnName("occurrence_version"); b.Property(x=>x.OccurredUtc).HasColumnName("occurred_at"); b.Property(x=>x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",240); MarketingCreativeAssetConfiguration.S(b,x=>x.Outcome,"outcome",40); b.Property(x=>x.ProcessedUtc).HasColumnName("processed_at"); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.MarketingLifecycleJourneyId,x.JourneyVersion,x.ContactId,x.EventType,x.EventReference,x.OccurrenceVersion}).IsUnique(); } }
internal sealed class MarketingJourneyStepAttemptConfiguration : IEntityTypeConfiguration<MarketingJourneyStepAttempt>
{ public void Configure(EntityTypeBuilder<MarketingJourneyStepAttempt> b) { MarketingStrategicConfiguration.Identity(b,"marketing_journey_step_attempts"); b.Property(x=>x.MarketingJourneyEnrollmentId).HasColumnName("enrollment_id"); b.Property(x=>x.JourneyVersion).HasColumnName("journey_version"); b.Property(x=>x.StepIndex).HasColumnName("step_index"); b.Property(x=>x.Attempt).HasColumnName("attempt"); MarketingCreativeAssetConfiguration.S(b,x=>x.Outcome,"outcome",40); b.Property(x=>x.PolicyEvidenceJson).HasColumnName("policy_evidence_json").HasColumnType("nvarchar(max)"); b.Property(x=>x.MarketingChannelActionId).HasColumnName("channel_action_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.CorrelationId,"correlation_id",128); b.Property(x=>x.CreatedUtc).HasColumnName("created_at"); b.HasIndex(x=>new{x.CompanyId,x.MarketingJourneyEnrollmentId,x.JourneyVersion,x.StepIndex,x.Attempt}).IsUnique(); } }
internal sealed class MarketingAttributionResultConfiguration : IEntityTypeConfiguration<MarketingAttributionResult>
{ public void Configure(EntityTypeBuilder<MarketingAttributionResult> b) { MarketingStrategicConfiguration.Identity(b,"marketing_attribution_results"); MarketingCreativeAssetConfiguration.S(b,x=>x.SubjectType,"subject_type",80); b.Property(x=>x.SubjectId).HasColumnName("subject_id"); MarketingCreativeAssetConfiguration.S(b,x=>x.Model,"model",80); MarketingCreativeAssetConfiguration.S(b,x=>x.Classification,"classification",32); b.Property(x=>x.AttributedValue).HasColumnName("attributed_value").HasPrecision(19,4); MarketingCreativeAssetConfiguration.S(b,x=>x.Unit,"unit",40); b.Property(x=>x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); b.Property(x=>x.Confidence).HasColumnName("confidence").HasPrecision(5,4); b.Property(x=>x.PeriodStartUtc).HasColumnName("period_start_at"); b.Property(x=>x.PeriodEndUtc).HasColumnName("period_end_at"); MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",200); b.Property(x=>x.CreatedUtc).HasColumnName("created_at"); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); } }
internal sealed class MarketingEventTriggerConfiguration : IEntityTypeConfiguration<MarketingEventTrigger>
{ public void Configure(EntityTypeBuilder<MarketingEventTrigger> b) { MarketingStrategicConfiguration.Identity(b,"marketing_event_triggers"); MarketingCreativeAssetConfiguration.S(b,x=>x.EventType,"event_type",100); MarketingCreativeAssetConfiguration.S(b,x=>x.SourceType,"source_type",80); MarketingCreativeAssetConfiguration.S(b,x=>x.SourceId,"source_id",200); b.Property(x=>x.SourceVersion).HasColumnName("source_version"); MarketingCreativeAssetConfiguration.S(b,x=>x.Severity,"severity",32); b.Property(x=>x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); MarketingCreativeAssetConfiguration.S(b,x=>x.IdempotencyKey,"idempotency_key",200); MarketingCreativeAssetConfiguration.S(b,x=>x.CorrelationId,"correlation_id",128); MarketingCreativeAssetConfiguration.S(b,x=>x.Status,"status",32); b.Property(x=>x.OperatingRunId).HasColumnName("operating_run_id"); b.Property(x=>x.RelatedTaskId).HasColumnName("related_task_id"); b.Property(x=>x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(2000); MarketingCreativeAssetConfiguration.Times(b); b.HasIndex(x=>new{x.CompanyId,x.IdempotencyKey}).IsUnique(); b.HasIndex(x=>new{x.CompanyId,x.Status,x.Severity}); b.HasIndex(x=>new{x.CompanyId,x.RelatedTaskId}); } }
