namespace VirtualCompany.Domain.Entities;

public sealed class MarketingSegmentSizeEstimate : ICompanyOwnedEntity
{
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase) { "top_down", "bottom_up", "triangulated", "legacy_unverified" };
    private MarketingSegmentSizeEstimate() { }
    public MarketingSegmentSizeEstimate(Guid id, Guid companyId, Guid versionId, decimal? low, decimal? high,
        string unit, string period, string geography, string? currency, string method, string assumptionsJson,
        string sourceIdsJson, decimal confidence, DateTime observedUtc, DateTime asOfUtc, string classification)
    {
        MarketingSegmentationValidation.Version(companyId, versionId);
        if (low < 0 || high < low) throw new ArgumentException("Segment size range is invalid.");
        if (!Methods.Contains(method)) throw new ArgumentException("Segment size method is unsupported.");
        MarketingSegmentationValidation.Confidence(confidence);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentVersionId = versionId;
        Low = low; High = high; Unit = MarketingSegmentationValidation.Required(unit, 40); Period = MarketingSegmentationValidation.Required(period, 80);
        Geography = MarketingSegmentationValidation.Required(geography, 200); Currency = currency;
        Method = method.ToLowerInvariant(); AssumptionsJson = MarketingSegmentationValidation.Json(assumptionsJson);
        SourceIdsJson = MarketingSegmentationValidation.Json(sourceIdsJson); Confidence = confidence;
        ObservedUtc = MarketingSegmentationValidation.Utc(observedUtc); AsOfUtc = MarketingSegmentationValidation.Utc(asOfUtc);
        Classification = MarketingSegmentationValidation.Required(classification, 24).ToLowerInvariant(); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid MarketingCustomerSegmentVersionId { get; private set; } public decimal? Low { get; private set; }
    public decimal? High { get; private set; } public string Unit { get; private set; } = null!;
    public string Period { get; private set; } = null!; public string Geography { get; private set; } = null!;
    public string? Currency { get; private set; } public string Method { get; private set; } = null!;
    public string AssumptionsJson { get; private set; } = "[]"; public string SourceIdsJson { get; private set; } = "[]";
    public decimal Confidence { get; private set; } public DateTime ObservedUtc { get; private set; }
    public DateTime AsOfUtc { get; private set; } public string Classification { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingSegmentEconomicEstimate : ICompanyOwnedEntity
{
    private static readonly HashSet<string> Metrics = new(StringComparer.OrdinalIgnoreCase)
    { "revenue", "gross_margin", "acquisition_cost", "sales_cycle_length", "cost_to_serve", "retention", "lifetime_value", "expansion" };
    private MarketingSegmentEconomicEstimate() { }
    public MarketingSegmentEconomicEstimate(Guid id, Guid companyId, Guid versionId, string metricCode,
        decimal? low, decimal? high, string unit, string? currency, string method, decimal confidence,
        string sourceIdsJson, DateTime observedUtc, string classification)
    {
        MarketingSegmentationValidation.Version(companyId, versionId);
        if (!Metrics.Contains(metricCode)) throw new ArgumentException("Segment economics metric is unsupported.");
        if (low < 0 || high < low) throw new ArgumentException("Segment economics range is invalid.");
        MarketingSegmentationValidation.Confidence(confidence);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentVersionId = versionId;
        MetricCode = metricCode.ToLowerInvariant(); Low = low; High = high; Unit = MarketingSegmentationValidation.Required(unit, 40);
        Currency = currency; Method = MarketingSegmentationValidation.Required(method, 80).ToLowerInvariant(); Confidence = confidence;
        SourceIdsJson = MarketingSegmentationValidation.Json(sourceIdsJson); ObservedUtc = MarketingSegmentationValidation.Utc(observedUtc);
        Classification = MarketingSegmentationValidation.Required(classification, 24).ToLowerInvariant(); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public string MetricCode { get; private set; } = null!; public decimal? Low { get; private set; } public decimal? High { get; private set; }
    public string Unit { get; private set; } = null!; public string? Currency { get; private set; } public string Method { get; private set; } = null!;
    public decimal Confidence { get; private set; } public string SourceIdsJson { get; private set; } = "[]";
    public DateTime ObservedUtc { get; private set; } public string Classification { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingSegmentScorePolicy : ICompanyOwnedEntity
{
    private MarketingSegmentScorePolicy() { }
    public MarketingSegmentScorePolicy(Guid id, Guid companyId, Guid versionId, decimal targetThreshold,
        string missingEvidenceBehavior, string exclusionsJson, string riskJson)
    {
        MarketingSegmentationValidation.Version(companyId, versionId);
        if (targetThreshold is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(targetThreshold));
        if (missingEvidenceBehavior is not ("zero" or "needs_review" or "exclude")) throw new ArgumentException("Missing-evidence behavior is unsupported.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentVersionId = versionId;
        TargetThreshold = targetThreshold; MissingEvidenceBehavior = missingEvidenceBehavior;
        ExclusionsJson = MarketingSegmentationValidation.Json(exclusionsJson); RiskJson = MarketingSegmentationValidation.Json(riskJson); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public decimal TargetThreshold { get; private set; } public string MissingEvidenceBehavior { get; private set; } = null!;
    public string ExclusionsJson { get; private set; } = "[]"; public string RiskJson { get; private set; } = "{}"; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingSegmentScoreDimension : ICompanyOwnedEntity
{
    private MarketingSegmentScoreDimension() { }
    public MarketingSegmentScoreDimension(Guid id, Guid companyId, Guid policyId, string code, decimal weight, decimal? score, string evidenceJson)
    {
        SalesEntityText.EnsureCompany(companyId); if (policyId == Guid.Empty) throw new ArgumentException("Score policy is required.");
        if (weight is <= 0 or > 1 || score is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(weight));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingSegmentScorePolicyId = policyId;
        Code = MarketingSegmentationValidation.Required(code, 80); Weight = weight; Score = score;
        EvidenceJson = MarketingSegmentationValidation.Json(evidenceJson); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingSegmentScorePolicyId { get; private set; }
    public string Code { get; private set; } = null!; public decimal Weight { get; private set; } public decimal? Score { get; private set; }
    public string EvidenceJson { get; private set; } = "[]"; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingSegmentTargetDecision : ICompanyOwnedEntity
{
    private MarketingSegmentTargetDecision() { }
    public MarketingSegmentTargetDecision(Guid id, Guid companyId, Guid versionId, string targetType, string rationale,
        string expectedImpactJson, decimal confidence, string risksJson, DateTime reviewUtc, string approvalStatus,
        Guid actorId, Guid? approvalRequestId, string idempotencyKey)
    {
        MarketingSegmentationValidation.Version(companyId, versionId); MarketingSegmentationValidation.Confidence(confidence);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentVersionId = versionId;
        TargetType = MarketingSegmentationValidation.Required(targetType, 40); Rationale = MarketingSegmentationValidation.Required(rationale, 4000);
        ExpectedImpactJson = MarketingSegmentationValidation.Json(expectedImpactJson); Confidence = confidence;
        RisksJson = MarketingSegmentationValidation.Json(risksJson); ReviewUtc = MarketingSegmentationValidation.Utc(reviewUtc);
        ApprovalStatus = MarketingSegmentationValidation.Required(approvalStatus, 32); ActorId = actorId == Guid.Empty ? throw new ArgumentException("Actor is required.") : actorId;
        ApprovalRequestId = approvalRequestId; IdempotencyKey = MarketingSegmentationValidation.Required(idempotencyKey, 200); DecidedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public string TargetType { get; private set; } = null!; public string Rationale { get; private set; } = null!;
    public string ExpectedImpactJson { get; private set; } = "{}"; public decimal Confidence { get; private set; }
    public string RisksJson { get; private set; } = "[]"; public DateTime ReviewUtc { get; private set; }
    public string ApprovalStatus { get; private set; } = null!; public Guid ActorId { get; private set; } public Guid? ApprovalRequestId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!; public DateTime DecidedUtc { get; private set; }
}

public sealed class MarketingSegmentArtifactMapping : ICompanyOwnedEntity
{
    private MarketingSegmentArtifactMapping() { }
    public MarketingSegmentArtifactMapping(Guid id, Guid companyId, Guid versionId, string mappingType, Guid artifactId, string label, string idempotencyKey)
    {
        MarketingSegmentationValidation.Version(companyId, versionId); if (artifactId == Guid.Empty) throw new ArgumentException("Mapped artifact is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentVersionId = versionId;
        MappingType = MarketingSegmentationValidation.Required(mappingType, 80); ArtifactId = artifactId;
        Label = MarketingSegmentationValidation.Required(label, 300); IdempotencyKey = MarketingSegmentationValidation.Required(idempotencyKey, 200); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public string MappingType { get; private set; } = null!; public Guid ArtifactId { get; private set; } public string Label { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}

internal static class MarketingSegmentationValidation
{
    public static void Version(Guid companyId, Guid versionId) { SalesEntityText.EnsureCompany(companyId); if (versionId == Guid.Empty) throw new ArgumentException("Segment version is required."); }
    public static void Confidence(decimal value) { if (value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value)); }
    public static string Required(string value, int max) => SalesEntityText.NormalizeRequired(value, nameof(value), max);
    public static string Json(string value) { System.Text.Json.JsonDocument.Parse(value); return SalesEntityText.NormalizeRequired(value, nameof(value), 32000); }
    public static DateTime Utc(DateTime value) => SalesEntityText.NormalizeUtc(value, nameof(value));
}
