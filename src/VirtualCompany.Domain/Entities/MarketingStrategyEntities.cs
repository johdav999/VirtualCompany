namespace VirtualCompany.Domain.Entities;

public static class MarketingStrategicStatuses
{
    public const string Draft = "draft";
    public const string InReview = "in_review";
    public const string Approved = "approved";
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    public const string Archived = "archived";
}

public sealed class MarketingStrategy : ICompanyOwnedEntity
{
    private MarketingStrategy() { }

    public MarketingStrategy(Guid id, Guid companyId, string title, string summary, string businessContext,
        DateTime validFromUtc, DateTime validToUtc, Guid ownerUserId, string sectionsJson,
        string evidenceReferencesJson, string missingEvidenceJson, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        validFromUtc = SalesEntityText.NormalizeUtc(validFromUtc, nameof(validFromUtc));
        validToUtc = SalesEntityText.NormalizeUtc(validToUtc, nameof(validToUtc));
        if (validToUtc <= validFromUtc) throw new ArgumentException("Strategy validity must end after it starts.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 4000);
        BusinessContext = SalesEntityText.NormalizeRequired(businessContext, nameof(businessContext), 8000);
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        OwnerUserId = ownerUserId == Guid.Empty ? throw new ArgumentException("Owner is required.") : ownerUserId;
        SectionsJson = RequireJson(sectionsJson, nameof(sectionsJson));
        EvidenceReferencesJson = RequireJson(evidenceReferencesJson, nameof(evidenceReferencesJson));
        MissingEvidenceJson = RequireJson(missingEvidenceJson, nameof(missingEvidenceJson));
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        Status = MarketingStrategicStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string BusinessContext { get; private set; } = null!;
    public DateTime ValidFromUtc { get; private set; }
    public DateTime ValidToUtc { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string SectionsJson { get; private set; } = null!;
    public string EvidenceReferencesJson { get; private set; } = null!;
    public string MissingEvidenceJson { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? ApprovalRequestId { get; private set; }
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Update(int expectedVersion, string title, string summary, string businessContext,
        DateTime validFromUtc, DateTime validToUtc, string sectionsJson, string evidenceJson, string missingEvidenceJson)
    {
        EnsureVersion(expectedVersion);
        if (Status != MarketingStrategicStatuses.Draft && Status != MarketingStrategicStatuses.Rejected)
            throw new InvalidOperationException("Only draft or rejected strategies can be edited.");
        validFromUtc = SalesEntityText.NormalizeUtc(validFromUtc, nameof(validFromUtc));
        validToUtc = SalesEntityText.NormalizeUtc(validToUtc, nameof(validToUtc));
        if (validToUtc <= validFromUtc) throw new ArgumentException("Strategy validity must end after it starts.");
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 4000);
        BusinessContext = SalesEntityText.NormalizeRequired(businessContext, nameof(businessContext), 8000);
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
        SectionsJson = RequireJson(sectionsJson, nameof(sectionsJson));
        EvidenceReferencesJson = RequireJson(evidenceJson, nameof(evidenceJson));
        MissingEvidenceJson = RequireJson(missingEvidenceJson, nameof(missingEvidenceJson));
        Status = MarketingStrategicStatuses.Draft;
        Touch();
    }

    public void Submit(Guid approvalRequestId)
    {
        if (Status != MarketingStrategicStatuses.Draft && Status != MarketingStrategicStatuses.Rejected)
            throw new InvalidOperationException("Only a draft strategy can be submitted.");
        ApprovalRequestId = approvalRequestId == Guid.Empty ? throw new ArgumentException("Approval is required.") : approvalRequestId;
        Status = MarketingStrategicStatuses.InReview;
        Touch();
    }

    public void MarkApproved() { RequireStatus(MarketingStrategicStatuses.InReview); Status = MarketingStrategicStatuses.Approved; Touch(); }
    public void MarkRejected() { RequireStatus(MarketingStrategicStatuses.InReview); Status = MarketingStrategicStatuses.Rejected; Touch(); }
    public void Activate() { RequireStatus(MarketingStrategicStatuses.Approved); Status = MarketingStrategicStatuses.Active; Touch(); }
    public void Supersede() { RequireStatus(MarketingStrategicStatuses.Active); Status = MarketingStrategicStatuses.Superseded; Touch(); }
    public void Cancel(int expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (MarketingStrategicStatuses.Draft or MarketingStrategicStatuses.Rejected or MarketingStrategicStatuses.InReview))
            throw new InvalidOperationException("Only a draft, rejected, or in-review strategy can be cancelled.");
        Status = MarketingStrategicStatuses.Cancelled;
        Touch();
    }

    private void EnsureVersion(int expected) { if (Version != expected) throw new InvalidOperationException("The strategy changed. Refresh and try again."); }
    private void RequireStatus(string status) { if (Status != status) throw new InvalidOperationException($"Strategy must be {status}."); }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
    private static string RequireJson(string value, string name) => SalesEntityText.NormalizeRequired(value, name, 64000);
}

public sealed class MarketingStrategySegment : ICompanyOwnedEntity
{
    private MarketingStrategySegment() { }
    public MarketingStrategySegment(Guid id, Guid companyId, Guid strategyId, Guid segmentId, Guid segmentVersionId)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingStrategyId = strategyId == Guid.Empty ? throw new ArgumentException("Strategy is required.") : strategyId;
        MarketingCustomerSegmentId = segmentId == Guid.Empty ? throw new ArgumentException("Segment is required.") : segmentId;
        MarketingCustomerSegmentVersionId = segmentVersionId == Guid.Empty ? throw new ArgumentException("Segment version is required.") : segmentVersionId;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingStrategyId { get; private set; }
    public Guid MarketingCustomerSegmentId { get; private set; }
    public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingStrategyCampaignLink : ICompanyOwnedEntity
{
    private MarketingStrategyCampaignLink() { }
    public MarketingStrategyCampaignLink(Guid id, Guid companyId, Guid strategyId, Guid planId, Guid campaignId,
        Guid segmentVersionId, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        MarketingStrategyId = Required(strategyId); MarketingPlanId = Required(planId); SalesCampaignId = Required(campaignId);
        MarketingCustomerSegmentVersionId = Required(segmentVersionId);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        Status = "draft_committed"; CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; }
    public Guid MarketingStrategyId { get; private set; } public Guid MarketingPlanId { get; private set; }
    public Guid SalesCampaignId { get; private set; } public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!; public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    private static Guid Required(Guid id) => id == Guid.Empty ? throw new ArgumentException("Reference is required.") : id;
}

public sealed class MarketingIntelligenceRecord : ICompanyOwnedEntity
{
    private static readonly HashSet<string> Kinds = new(StringComparer.OrdinalIgnoreCase)
        { "market_hypothesis", "customer_insight", "competitor_profile", "competitor_claim", "comparison" };
    private static readonly HashSet<string> Classifications = new(StringComparer.OrdinalIgnoreCase)
        { "observed", "verified", "estimated", "inferred", "assumption" };
    private MarketingIntelligenceRecord() { }
    public MarketingIntelligenceRecord(Guid id, Guid companyId, string kind, string subject, string summary,
        string classification, decimal confidence, string sourceType, string sourceReference,
        DateTime observedUtc, DateTime reviewDueUtc, string dimensionsJson, Guid ownerUserId)
    {
        SalesEntityText.EnsureCompany(companyId);
        kind = SalesEntityText.NormalizeRequired(kind, nameof(kind), 40).ToLowerInvariant();
        classification = SalesEntityText.NormalizeRequired(classification, nameof(classification), 24).ToLowerInvariant();
        if (!Kinds.Contains(kind)) throw new ArgumentException("Unsupported intelligence kind.");
        if (!Classifications.Contains(classification)) throw new ArgumentException("Unsupported evidence classification.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        observedUtc = SalesEntityText.NormalizeUtc(observedUtc, nameof(observedUtc));
        reviewDueUtc = SalesEntityText.NormalizeUtc(reviewDueUtc, nameof(reviewDueUtc));
        if (reviewDueUtc < observedUtc) throw new ArgumentException("Review date cannot precede observation.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; Kind = kind;
        Subject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 240);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 8000);
        Classification = classification; Confidence = confidence;
        SourceType = SalesEntityText.NormalizeRequired(sourceType, nameof(sourceType), 48).ToLowerInvariant();
        SourceReference = SalesEntityText.NormalizeRequired(sourceReference, nameof(sourceReference), 2000);
        ObservedUtc = observedUtc; ReviewDueUtc = reviewDueUtc;
        DimensionsJson = SalesEntityText.NormalizeRequired(dimensionsJson, nameof(dimensionsJson), 32000);
        OwnerUserId = ownerUserId == Guid.Empty ? throw new ArgumentException("Owner is required.") : ownerUserId;
        ReviewStatus = "pending"; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string Subject { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Classification { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceReference { get; private set; } = null!;
    public DateTime ObservedUtc { get; private set; }
    public DateTime ReviewDueUtc { get; private set; }
    public string DimensionsJson { get; private set; } = null!;
    public Guid OwnerUserId { get; private set; }
    public string ReviewStatus { get; private set; } = null!;
    public bool IsArchived { get; private set; }
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Review(bool verified)
    {
        ReviewStatus = verified ? "reviewed" : "needs_evidence";
        if (verified && Classification == "inferred") Classification = "verified";
        Version++; UpdatedUtc = DateTime.UtcNow;
    }
    public void Update(int expectedVersion, string subject, string summary, string classification, decimal confidence,
        string sourceType, string sourceReference, DateTime observedUtc, DateTime reviewDueUtc, string dimensionsJson)
    {
        EnsureVersion(expectedVersion);
        if (IsArchived) throw new InvalidOperationException("Archived intelligence cannot be edited.");
        classification = SalesEntityText.NormalizeRequired(classification, nameof(classification), 24).ToLowerInvariant();
        if (!Classifications.Contains(classification)) throw new ArgumentException("Unsupported evidence classification.");
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        observedUtc = SalesEntityText.NormalizeUtc(observedUtc, nameof(observedUtc));
        reviewDueUtc = SalesEntityText.NormalizeUtc(reviewDueUtc, nameof(reviewDueUtc));
        if (reviewDueUtc < observedUtc) throw new ArgumentException("Review date cannot precede observation.");
        Subject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 240);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 8000);
        Classification = classification; Confidence = confidence;
        SourceType = SalesEntityText.NormalizeRequired(sourceType, nameof(sourceType), 48).ToLowerInvariant();
        SourceReference = SalesEntityText.NormalizeRequired(sourceReference, nameof(sourceReference), 2000);
        ObservedUtc = observedUtc; ReviewDueUtc = reviewDueUtc;
        DimensionsJson = SalesEntityText.NormalizeRequired(dimensionsJson, nameof(dimensionsJson), 32000);
        ReviewStatus = "pending"; Version++; UpdatedUtc = DateTime.UtcNow;
    }
    public void Archive(int expectedVersion) { EnsureVersion(expectedVersion); IsArchived = true; Version++; UpdatedUtc = DateTime.UtcNow; }
    private void EnsureVersion(int expectedVersion)
    { if (Version != expectedVersion) throw new InvalidOperationException("The intelligence record changed. Refresh and try again."); }
}

public sealed class MarketingIntelligenceReview : ICompanyOwnedEntity
{
    private MarketingIntelligenceReview() { }
    public MarketingIntelligenceReview(Guid id, Guid companyId, Guid intelligenceId, int reviewNumber,
        Guid reviewerUserId, string outcome, string rationale, string beforeJson, string afterJson)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (intelligenceId == Guid.Empty || reviewerUserId == Guid.Empty || reviewNumber < 1)
            throw new ArgumentException("Intelligence, reviewer, and review number are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingIntelligenceRecordId = intelligenceId;
        ReviewNumber = reviewNumber; ReviewerUserId = reviewerUserId;
        Outcome = SalesEntityText.NormalizeRequired(outcome, nameof(outcome), 32).ToLowerInvariant();
        Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 4000);
        BeforeJson = SalesEntityText.NormalizeRequired(beforeJson, nameof(beforeJson), 32000);
        AfterJson = SalesEntityText.NormalizeRequired(afterJson, nameof(afterJson), 32000);
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingIntelligenceRecordId { get; private set; }
    public int ReviewNumber { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public string Outcome { get; private set; } = null!;
    public string Rationale { get; private set; } = null!;
    public string BeforeJson { get; private set; } = null!;
    public string AfterJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingCustomerSegment : ICompanyOwnedEntity
{
    private MarketingCustomerSegment() { }
    public MarketingCustomerSegment(Guid id, Guid companyId, string name, string description, Guid ownerUserId)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        Description = SalesEntityText.NormalizeRequired(description, nameof(description), 4000);
        OwnerUserId = ownerUserId == Guid.Empty ? throw new ArgumentException("Owner is required.") : ownerUserId;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Guid OwnerUserId { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Archive() { IsArchived = true; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class MarketingCustomerSegmentVersion : ICompanyOwnedEntity
{
    private static readonly string[] DisallowedCriteria = ["race", "ethnicity", "religion", "sexual_orientation", "health_condition"];
    private MarketingCustomerSegmentVersion() { }
    public MarketingCustomerSegmentVersion(Guid id, Guid companyId, Guid segmentId, int versionNumber,
        string criteriaJson, string needsJson, string behaviorsJson, string channelsJson, string pricingJson,
        long? sizeLow, long? sizeHigh, string sizeMethod, decimal confidence, string economicsJson,
        string scorecardJson, decimal attractivenessScore, string evidenceJson, DateTime evidenceObservedUtc,
        Guid ownerUserId, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (segmentId == Guid.Empty || versionNumber < 1) throw new ArgumentException("Segment and version are required.");
        if (sizeLow < 0 || sizeHigh < sizeLow) throw new ArgumentException("Segment size range is invalid.");
        if (confidence is < 0 or > 1 || attractivenessScore is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(confidence));
        var criteria = SalesEntityText.NormalizeRequired(criteriaJson, nameof(criteriaJson), 32000);
        if (DisallowedCriteria.Any(x => criteria.Contains(x, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This segment uses a criterion that is not permitted for targeting.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingCustomerSegmentId = segmentId;
        VersionNumber = versionNumber; CriteriaJson = criteria;
        NeedsJson = Required(needsJson, nameof(needsJson)); BehaviorsJson = Required(behaviorsJson, nameof(behaviorsJson));
        ChannelsJson = Required(channelsJson, nameof(channelsJson)); PricingJson = Required(pricingJson, nameof(pricingJson));
        SizeLow = sizeLow; SizeHigh = sizeHigh; SizeMethod = SalesEntityText.NormalizeRequired(sizeMethod, nameof(sizeMethod), 32).ToLowerInvariant();
        Confidence = confidence; EconomicsJson = Required(economicsJson, nameof(economicsJson));
        ScorecardJson = Required(scorecardJson, nameof(scorecardJson)); AttractivenessScore = attractivenessScore;
        EvidenceJson = Required(evidenceJson, nameof(evidenceJson));
        EvidenceObservedUtc = SalesEntityText.NormalizeUtc(evidenceObservedUtc, nameof(evidenceObservedUtc));
        OwnerUserId = ownerUserId == Guid.Empty ? throw new ArgumentException("Owner is required.") : ownerUserId;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        Status = MarketingStrategicStatuses.Draft; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingCustomerSegmentId { get; private set; }
    public int VersionNumber { get; private set; }
    public string CriteriaJson { get; private set; } = null!;
    public string NeedsJson { get; private set; } = null!;
    public string BehaviorsJson { get; private set; } = null!;
    public string ChannelsJson { get; private set; } = null!;
    public string PricingJson { get; private set; } = null!;
    public long? SizeLow { get; private set; }
    public long? SizeHigh { get; private set; }
    public string SizeMethod { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string EconomicsJson { get; private set; } = null!;
    public string ScorecardJson { get; private set; } = null!;
    public decimal AttractivenessScore { get; private set; }
    public string EvidenceJson { get; private set; } = null!;
    public DateTime EvidenceObservedUtc { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string TargetState { get; private set; } = "observe_only";
    public string? TargetRationale { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public int ConcurrencyVersion { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Submit(Guid approvalRequestId) { if (Status != MarketingStrategicStatuses.Draft) throw new InvalidOperationException("Only draft segment versions can be submitted."); ApprovalRequestId = approvalRequestId; Status = MarketingStrategicStatuses.InReview; Touch(); }
    public void MarkApproved() { if (Status != MarketingStrategicStatuses.InReview) throw new InvalidOperationException("Segment is not in review."); Status = MarketingStrategicStatuses.Approved; Touch(); }
    public void ActivateTarget(string targetState, string rationale)
    {
        if (Status != MarketingStrategicStatuses.Approved) throw new InvalidOperationException("Only approved segment versions can be targeted.");
        TargetState = SalesEntityText.NormalizeRequired(targetState, nameof(targetState), 40).ToLowerInvariant();
        TargetRationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 4000); Status = MarketingStrategicStatuses.Active; Touch();
    }
    public void Supersede() { if (Status == MarketingStrategicStatuses.Active) { Status = MarketingStrategicStatuses.Superseded; Touch(); } }
    private void Touch() { ConcurrencyVersion++; UpdatedUtc = DateTime.UtcNow; }
    private static string Required(string value, string name) => SalesEntityText.NormalizeRequired(value, name, 32000);
}

public sealed class MarketingSegmentDimension : ICompanyOwnedEntity
{
    private MarketingSegmentDimension() { }
    public MarketingSegmentDimension(Guid id, Guid companyId, Guid segmentVersionId, string category,
        string path, string value, string classification, decimal? numericValue = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (segmentVersionId == Guid.Empty) throw new ArgumentException("Segment version is required.");
        if (numericValue is < -1_000_000_000_000m or > 1_000_000_000_000m)
            throw new ArgumentOutOfRangeException(nameof(numericValue));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingCustomerSegmentVersionId = segmentVersionId;
        Category = SalesEntityText.NormalizeRequired(category, nameof(category), 40).ToLowerInvariant();
        Path = SalesEntityText.NormalizeRequired(path, nameof(path), 500);
        Value = SalesEntityText.NormalizeRequired(value, nameof(value), 4000);
        Classification = SalesEntityText.NormalizeRequired(classification, nameof(classification), 24).ToLowerInvariant();
        NumericValue = numericValue;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingCustomerSegmentVersionId { get; private set; }
    public string Category { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public string Classification { get; private set; } = null!;
    public decimal? NumericValue { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingOperatingRun : ICompanyOwnedEntity
{
    private MarketingOperatingRun() { }
    public MarketingOperatingRun(Guid id, Guid companyId, Guid agentId, string triggerType, string triggerReference,
        string idempotencyKey, string correlationId, Guid? goalId, Guid? initiativeId, Guid? taskId,
        string effectiveAuthority, int configurationVersion, string evidenceVersion, decimal? budgetLimit)
    {
        SalesEntityText.EnsureCompany(companyId); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        AgentId = agentId == Guid.Empty ? throw new ArgumentException("Agent is required.") : agentId;
        TriggerType = SalesEntityText.NormalizeRequired(triggerType, nameof(triggerType), 64).ToLowerInvariant();
        TriggerReference = SalesEntityText.NormalizeRequired(triggerReference, nameof(triggerReference), 500);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200);
        CorrelationId = SalesEntityText.NormalizeRequired(correlationId, nameof(correlationId), 128);
        CompanyGoalId = goalId; OperatingInitiativeId = initiativeId; WorkTaskId = taskId;
        EffectiveAuthority = SalesEntityText.NormalizeRequired(effectiveAuthority, nameof(effectiveAuthority), 40);
        ConfigurationVersion = configurationVersion; EvidenceVersion = SalesEntityText.NormalizeRequired(evidenceVersion, nameof(evidenceVersion), 100);
        BudgetLimit = budgetLimit; Status = "requested"; AttemptCount = 1; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid? CompanyGoalId { get; private set; }
    public Guid? OperatingInitiativeId { get; private set; }
    public Guid? WorkTaskId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public string TriggerReference { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string EffectiveAuthority { get; private set; } = null!;
    public int ConfigurationVersion { get; private set; }
    public string EvidenceVersion { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string SelectedWorkJson { get; private set; } = "[]";
    public string EvidenceJson { get; private set; } = "{}";
    public string MissingEvidenceJson { get; private set; } = "[]";
    public string AssignmentContextJson { get; private set; } = "{}";
    public string? OutcomeSummary { get; private set; }
    public string? RecoveryCode { get; private set; }
    public decimal? BudgetLimit { get; private set; }
    public decimal BudgetUsed { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public void SetAssignmentContext(string assignmentContextJson)
    {
        if (Status != "requested") throw new InvalidOperationException("Assignment context is immutable after a run starts.");
        AssignmentContextJson = SalesEntityText.NormalizeRequired(assignmentContextJson, nameof(assignmentContextJson), 32000);
        UpdatedUtc = DateTime.UtcNow;
    }
    public void Claim(TimeSpan lease) { if (Status != "requested") throw new InvalidOperationException("Run is not available."); Status = "running"; LeaseExpiresUtc = DateTime.UtcNow.Add(lease); UpdatedUtc = DateTime.UtcNow; }
    public void RenewLease(TimeSpan lease)
    {
        if (Status != "running") throw new InvalidOperationException("Run is not active.");
        LeaseExpiresUtc = DateTime.UtcNow.Add(lease); UpdatedUtc = DateTime.UtcNow;
    }
    public void AddBudgetUsage(decimal amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (BudgetLimit.HasValue && BudgetUsed + amount > BudgetLimit.Value)
            throw new InvalidOperationException("The Marketing operating-run budget would be exceeded.");
        BudgetUsed += amount; UpdatedUtc = DateTime.UtcNow;
    }
    public void Complete(string selectedWorkJson, string evidenceJson, string missingEvidenceJson, string summary)
    { if (Status != "running") throw new InvalidOperationException("Run is not active."); SelectedWorkJson = selectedWorkJson; EvidenceJson = evidenceJson; MissingEvidenceJson = missingEvidenceJson; OutcomeSummary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 4000); Status = "completed"; LeaseExpiresUtc = null; CompletedUtc = UpdatedUtc = DateTime.UtcNow; }
    public void Block(string code, string summary, string missingEvidenceJson = "[]")
    { RecoveryCode = SalesEntityText.NormalizeRequired(code, nameof(code), 100); OutcomeSummary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 4000); MissingEvidenceJson = missingEvidenceJson; Status = "blocked"; LeaseExpiresUtc = null; CompletedUtc = UpdatedUtc = DateTime.UtcNow; }
}

public sealed class MarketingOperatingAction : ICompanyOwnedEntity
{
    private MarketingOperatingAction() { }
    public MarketingOperatingAction(Guid id, Guid companyId, Guid runId, int sequence, string actionType,
        string title, string? capability, string? tool, string targetJson, string sourceVersion,
        string goalRelevance, string dependenciesJson, string expectedEvidence, string authorityDecision,
        bool requiresApproval, string idempotencyKey, decimal estimatedCost, int maximumAttempts)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (runId == Guid.Empty) throw new ArgumentException("Operating run is required.", nameof(runId));
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (estimatedCost < 0) throw new ArgumentOutOfRangeException(nameof(estimatedCost));
        if (maximumAttempts is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingOperatingRunId = runId;
        Sequence = sequence; ActionType = SalesEntityText.NormalizeRequired(actionType, nameof(actionType), 80).ToLowerInvariant();
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 500); Capability = capability;
        Tool = tool; TargetJson = SalesEntityText.NormalizeRequired(targetJson, nameof(targetJson), 16000);
        SourceVersion = SalesEntityText.NormalizeRequired(sourceVersion, nameof(sourceVersion), 200);
        GoalRelevance = SalesEntityText.NormalizeRequired(goalRelevance, nameof(goalRelevance), 2000);
        DependenciesJson = SalesEntityText.NormalizeRequired(dependenciesJson, nameof(dependenciesJson), 8000);
        ExpectedCompletionEvidence = SalesEntityText.NormalizeRequired(expectedEvidence, nameof(expectedEvidence), 2000);
        AuthorityDecision = SalesEntityText.NormalizeRequired(authorityDecision, nameof(authorityDecision), 100);
        RequiresApproval = requiresApproval; IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 240);
        EstimatedCost = estimatedCost; MaximumAttempts = maximumAttempts; Status = "planned";
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingOperatingRunId { get; private set; }
    public int Sequence { get; private set; }
    public int Version { get; private set; } = 1;
    public string ActionType { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Capability { get; private set; }
    public string? Tool { get; private set; }
    public string TargetJson { get; private set; } = "{}";
    public string SourceVersion { get; private set; } = null!;
    public string GoalRelevance { get; private set; } = null!;
    public string DependenciesJson { get; private set; } = "[]";
    public string ExpectedCompletionEvidence { get; private set; } = null!;
    public string AuthorityDecision { get; private set; } = null!;
    public bool RequiresApproval { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public decimal EstimatedCost { get; private set; }
    public decimal ActualCost { get; private set; }
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public int MaximumAttempts { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? ArtifactType { get; private set; }
    public Guid? ArtifactId { get; private set; }
    public string ActualEvidenceJson { get; private set; } = "{}";
    public string? RecoveryCode { get; private set; }
    public string? RecoveryGuidance { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }

    public void Claim(string owner, TimeSpan lease)
    {
        var now = DateTime.UtcNow;
        if (Status is not ("planned" or "retry_wait") && !(Status == "running" && LeaseExpiresUtc <= now))
            throw new InvalidOperationException("The Marketing action is not claimable.");
        if (Status == "retry_wait" && NextAttemptUtc > now) throw new InvalidOperationException("The retry cooldown is active.");
        if (AttemptCount >= MaximumAttempts) throw new InvalidOperationException("Maximum attempts have been reached.");
        LeaseOwner = SalesEntityText.NormalizeRequired(owner, nameof(owner), 128); LeaseExpiresUtc = now.Add(lease);
        AttemptCount++; Status = "running"; RecoveryCode = RecoveryGuidance = null; NextAttemptUtc = null;
        Version++; UpdatedUtc = now;
    }
    public void RenewLease(string owner, TimeSpan lease)
    {
        if (Status != "running" || !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
            throw new InvalidOperationException("The Marketing action lease is not owned by this worker.");
        LeaseExpiresUtc = DateTime.UtcNow.Add(lease); Version++; UpdatedUtc = DateTime.UtcNow;
    }
    public void Complete(string owner, string artifactType, Guid? artifactId, string evidenceJson, decimal actualCost)
    {
        EnsureOwner(owner); if (actualCost < 0) throw new ArgumentOutOfRangeException(nameof(actualCost));
        ArtifactType = SalesEntityText.NormalizeRequired(artifactType, nameof(artifactType), 100); ArtifactId = artifactId;
        ActualEvidenceJson = SalesEntityText.NormalizeRequired(evidenceJson, nameof(evidenceJson), 16000);
        ActualCost = actualCost; Status = "completed"; LeaseOwner = null; LeaseExpiresUtc = null;
        CompletedUtc = UpdatedUtc = DateTime.UtcNow; Version++;
    }
    public void Block(string owner, string code, string guidance, bool retryable, TimeSpan? retryDelay = null)
    {
        EnsureOwner(owner); RecoveryCode = SalesEntityText.NormalizeRequired(code, nameof(code), 100);
        RecoveryGuidance = SalesEntityText.NormalizeRequired(guidance, nameof(guidance), 2000);
        LeaseOwner = null; LeaseExpiresUtc = null;
        if (retryable && AttemptCount < MaximumAttempts)
        { Status = "retry_wait"; NextAttemptUtc = DateTime.UtcNow.Add(retryDelay ?? TimeSpan.FromMinutes(5)); }
        else { Status = AttemptCount >= MaximumAttempts ? "dead_letter" : "blocked"; CompletedUtc = DateTime.UtcNow; }
        UpdatedUtc = DateTime.UtcNow; Version++;
    }
    public void Cancel(string guidance)
    {
        if (Status is "completed" or "cancelled") return;
        Status = "cancelled"; RecoveryGuidance = SalesEntityText.NormalizeRequired(guidance, nameof(guidance), 2000);
        LeaseOwner = null; LeaseExpiresUtc = null; CompletedUtc = UpdatedUtc = DateTime.UtcNow; Version++;
    }
    public void Retry(string rationale)
    {
        if (Status is not ("blocked" or "retry_wait" or "dead_letter"))
            throw new InvalidOperationException("Only a recoverable Marketing action can be retried.");
        if (AttemptCount >= MaximumAttempts) AttemptCount = MaximumAttempts - 1;
        Status = "planned"; RecoveryCode = "operator_retry";
        RecoveryGuidance = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 2000);
        NextAttemptUtc = null; CompletedUtc = null; Version++; UpdatedUtc = DateTime.UtcNow;
    }
    private void EnsureOwner(string owner)
    {
        if (Status != "running" || !string.Equals(LeaseOwner, owner, StringComparison.Ordinal))
            throw new InvalidOperationException("The Marketing action lease is not owned by this worker.");
    }
}
