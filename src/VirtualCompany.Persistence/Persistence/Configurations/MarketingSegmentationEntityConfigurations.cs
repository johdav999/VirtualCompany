using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal static class MarketingSegmentationConfiguration
{
    public static void Identity<T>(EntityTypeBuilder<T> b, string table) where T : class, ICompanyOwnedEntity
    {
        b.ToTable(table); b.HasKey("Id"); b.Property<Guid>("Id").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnName("company_id"); b.HasAlternateKey("CompanyId", "Id");
        b.HasIndex(x => x.CompanyId);
    }
    public static void VersionOwner<T>(EntityTypeBuilder<T> b) where T : class, ICompanyOwnedEntity
    {
        b.HasOne<MarketingCustomerSegmentVersion>().WithMany().HasForeignKey("CompanyId", "MarketingCustomerSegmentVersionId")
            .HasPrincipalKey("CompanyId", "Id").OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MarketingSegmentSizeEstimateConfiguration : IEntityTypeConfiguration<MarketingSegmentSizeEstimate>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentSizeEstimate> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_size_estimates"); MarketingSegmentationConfiguration.VersionOwner(b);
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("segment_version_id"); b.Property(x => x.Low).HasColumnName("low").HasPrecision(19,4); b.Property(x => x.High).HasColumnName("high").HasPrecision(19,4);
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(40); b.Property(x => x.Period).HasColumnName("period").HasMaxLength(80); b.Property(x => x.Geography).HasColumnName("geography").HasMaxLength(200); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(8);
        b.Property(x => x.Method).HasColumnName("method").HasMaxLength(40); b.Property(x => x.AssumptionsJson).HasColumnName("assumptions_json").HasColumnType("nvarchar(max)"); b.Property(x => x.SourceIdsJson).HasColumnName("source_ids_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5,4); b.Property(x => x.ObservedUtc).HasColumnName("observed_at"); b.Property(x => x.AsOfUtc).HasColumnName("as_of_at"); b.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(24); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId, x.Method });
    }
}

internal sealed class MarketingSegmentEconomicEstimateConfiguration : IEntityTypeConfiguration<MarketingSegmentEconomicEstimate>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentEconomicEstimate> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_economic_estimates"); MarketingSegmentationConfiguration.VersionOwner(b);
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("segment_version_id"); b.Property(x => x.MetricCode).HasColumnName("metric_code").HasMaxLength(60); b.Property(x => x.Low).HasColumnName("low").HasPrecision(19,4); b.Property(x => x.High).HasColumnName("high").HasPrecision(19,4);
        b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(40); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(8); b.Property(x => x.Method).HasColumnName("method").HasMaxLength(80); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5,4);
        b.Property(x => x.SourceIdsJson).HasColumnName("source_ids_json").HasColumnType("nvarchar(max)"); b.Property(x => x.ObservedUtc).HasColumnName("observed_at"); b.Property(x => x.Classification).HasColumnName("classification").HasMaxLength(24); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId, x.MetricCode });
    }
}

internal sealed class MarketingSegmentScorePolicyConfiguration : IEntityTypeConfiguration<MarketingSegmentScorePolicy>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentScorePolicy> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_score_policies"); MarketingSegmentationConfiguration.VersionOwner(b);
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("segment_version_id"); b.Property(x => x.TargetThreshold).HasColumnName("target_threshold").HasPrecision(5,2); b.Property(x => x.MissingEvidenceBehavior).HasColumnName("missing_evidence_behavior").HasMaxLength(32);
        b.Property(x => x.ExclusionsJson).HasColumnName("exclusions_json").HasColumnType("nvarchar(max)"); b.Property(x => x.RiskJson).HasColumnName("risk_json").HasColumnType("nvarchar(max)"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId }).IsUnique();
    }
}

internal sealed class MarketingSegmentScoreDimensionConfiguration : IEntityTypeConfiguration<MarketingSegmentScoreDimension>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentScoreDimension> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_score_dimensions");
        b.Property(x => x.MarketingSegmentScorePolicyId).HasColumnName("score_policy_id"); b.Property(x => x.Code).HasColumnName("code").HasMaxLength(80); b.Property(x => x.Weight).HasColumnName("weight").HasPrecision(6,5); b.Property(x => x.Score).HasColumnName("score").HasPrecision(5,2); b.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("nvarchar(max)"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasOne<MarketingSegmentScorePolicy>().WithMany().HasForeignKey(x => new { x.CompanyId, x.MarketingSegmentScorePolicyId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CompanyId, x.MarketingSegmentScorePolicyId, x.Code }).IsUnique();
    }
}

internal sealed class MarketingSegmentTargetDecisionConfiguration : IEntityTypeConfiguration<MarketingSegmentTargetDecision>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentTargetDecision> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_target_decisions"); MarketingSegmentationConfiguration.VersionOwner(b);
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("segment_version_id"); b.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(40); b.Property(x => x.Rationale).HasColumnName("rationale").HasMaxLength(4000); b.Property(x => x.ExpectedImpactJson).HasColumnName("expected_impact_json").HasColumnType("nvarchar(max)"); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5,4); b.Property(x => x.RisksJson).HasColumnName("risks_json").HasColumnType("nvarchar(max)"); b.Property(x => x.ReviewUtc).HasColumnName("review_at"); b.Property(x => x.ApprovalStatus).HasColumnName("approval_status").HasMaxLength(32); b.Property(x => x.ActorId).HasColumnName("actor_id"); b.Property(x => x.ApprovalRequestId).HasColumnName("approval_request_id"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.DecidedUtc).HasColumnName("decided_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
    }
}

internal sealed class MarketingSegmentArtifactMappingConfiguration : IEntityTypeConfiguration<MarketingSegmentArtifactMapping>
{
    public void Configure(EntityTypeBuilder<MarketingSegmentArtifactMapping> b)
    {
        MarketingSegmentationConfiguration.Identity(b, "marketing_segment_artifact_mappings"); MarketingSegmentationConfiguration.VersionOwner(b);
        b.Property(x => x.MarketingCustomerSegmentVersionId).HasColumnName("segment_version_id"); b.Property(x => x.MappingType).HasColumnName("mapping_type").HasMaxLength(80); b.Property(x => x.ArtifactId).HasColumnName("artifact_id"); b.Property(x => x.Label).HasColumnName("label").HasMaxLength(300); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.MarketingCustomerSegmentVersionId, x.MappingType, x.ArtifactId }).IsUnique();
    }
}
