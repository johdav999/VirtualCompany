namespace VirtualCompany.Domain.Entities;

public sealed class SalesCampaignObjective : ICompanyOwnedEntity
{
    private SalesCampaignObjective() { }

    public SalesCampaignObjective(Guid id, Guid companyId, Guid campaignId, string objectiveType,
        decimal targetValue, string unit, DateTime targetUtc, bool isPrimary, DateTime? createdUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (campaignId == Guid.Empty) throw new ArgumentException("Campaign is required.", nameof(campaignId));
        if (targetValue <= 0) throw new ArgumentOutOfRangeException(nameof(targetValue));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        ObjectiveType = SalesEntityText.NormalizeRequired(objectiveType, nameof(objectiveType), 64).ToLowerInvariant();
        TargetValue = targetValue;
        Unit = SalesEntityText.NormalizeRequired(unit, nameof(unit), 40).ToLowerInvariant();
        TargetUtc = SalesEntityText.NormalizeUtc(targetUtc, nameof(targetUtc));
        IsPrimary = isPrimary;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public string ObjectiveType { get; private set; } = null!;
    public decimal TargetValue { get; private set; }
    public string Unit { get; private set; } = null!;
    public DateTime TargetUtc { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesCampaign SalesCampaign { get; private set; } = null!;
}

public sealed class SalesCampaignOffer : ICompanyOwnedEntity
{
    private SalesCampaignOffer() { }

    public SalesCampaignOffer(Guid id, Guid companyId, Guid campaignId, string name,
        string sourceType, string sourceReference, Guid? knowledgeDocumentId = null, bool noOfferRequired = false)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId == Guid.Empty ? throw new ArgumentException("Campaign is required.", nameof(campaignId)) : campaignId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        SourceType = SalesEntityText.NormalizeRequired(sourceType, nameof(sourceType), 40).ToLowerInvariant();
        SourceReference = SalesEntityText.NormalizeRequired(sourceReference, nameof(sourceReference), 512);
        KnowledgeDocumentId = knowledgeDocumentId;
        NoOfferRequired = noOfferRequired;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public string Name { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceReference { get; private set; } = null!;
    public Guid? KnowledgeDocumentId { get; private set; }
    public bool NoOfferRequired { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SalesCampaign SalesCampaign { get; private set; } = null!;
}

public sealed class SalesCampaignAudienceSegment : ICompanyOwnedEntity
{
    private SalesCampaignAudienceSegment() { }

    public SalesCampaignAudienceSegment(Guid id, Guid companyId, string name, string segmentKind)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        SegmentKind = SalesEntityText.NormalizeRequired(segmentKind, nameof(segmentKind), 40).ToLowerInvariant();
        IsActive = true;
        Version = 1;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string SegmentKind { get; private set; } = null!;
    public string? Industry { get; private set; }
    public string? Country { get; private set; }
    public int? MinEmployees { get; private set; }
    public int? MaxEmployees { get; private set; }
    public string? BuyingRole { get; private set; }
    public string? CustomerLifecycle { get; private set; }
    public string? ProductInterest { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public bool RequireCommunicationPermission { get; private set; } = true;
    public bool ExcludeOpenCriticalSupportCases { get; private set; } = true;
    public bool IsActive { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Configure(string? industry, string? country, int? minEmployees, int? maxEmployees,
        string? buyingRole, string? customerLifecycle, string? productInterest, string? preferredLanguage,
        bool requireCommunicationPermission, bool excludeOpenCriticalSupportCases)
    {
        if (minEmployees is < 0 || maxEmployees is < 0 || (minEmployees.HasValue && maxEmployees.HasValue && minEmployees > maxEmployees))
            throw new ArgumentException("Employee range is invalid.");
        Industry = SalesEntityText.NormalizeOptional(industry, nameof(industry), 160);
        Country = SalesEntityText.NormalizeOptional(country, nameof(country), 120);
        MinEmployees = minEmployees;
        MaxEmployees = maxEmployees;
        BuyingRole = SalesEntityText.NormalizeOptional(buyingRole, nameof(buyingRole), 120);
        CustomerLifecycle = SalesEntityText.NormalizeOptional(customerLifecycle, nameof(customerLifecycle), 80);
        ProductInterest = SalesEntityText.NormalizeOptional(productInterest, nameof(productInterest), 200);
        PreferredLanguage = SalesEntityText.NormalizeOptional(preferredLanguage, nameof(preferredLanguage), 20);
        RequireCommunicationPermission = requireCommunicationPermission;
        ExcludeOpenCriticalSupportCases = excludeOpenCriticalSupportCases;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SalesCampaignAudienceSnapshot : ICompanyOwnedEntity
{
    private SalesCampaignAudienceSnapshot() { }
    public SalesCampaignAudienceSnapshot(Guid id, Guid companyId, Guid campaignId, Guid? segmentId, int segmentVersion, int snapshotVersion)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        AudienceSegmentId = segmentId;
        SegmentVersion = segmentVersion;
        SnapshotVersion = snapshotVersion;
        CapturedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid? AudienceSegmentId { get; private set; }
    public int SegmentVersion { get; private set; }
    public int SnapshotVersion { get; private set; }
    public DateTime CapturedUtc { get; private set; }
    public ICollection<SalesCampaignAudienceMember> Members { get; } = new List<SalesCampaignAudienceMember>();
}

public sealed class SalesCampaignAudienceMember : ICompanyOwnedEntity
{
    private SalesCampaignAudienceMember() { }
    public SalesCampaignAudienceMember(Guid id, Guid companyId, Guid snapshotId, Guid? contactId,
        Guid? customerCompanyId, Guid? prospectAccountId, string eligibilityStatus, string reason,
        string consentStatus, string? language)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AudienceSnapshotId = snapshotId;
        ContactId = contactId;
        CustomerCompanyId = customerCompanyId;
        ProspectAccountId = prospectAccountId;
        EligibilityStatus = SalesEntityText.NormalizeRequired(eligibilityStatus, nameof(eligibilityStatus), 32).ToLowerInvariant();
        InclusionReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        ConsentStatus = SalesEntityText.NormalizeRequired(consentStatus, nameof(consentStatus), 32).ToLowerInvariant();
        CommunicationLanguage = SalesEntityText.NormalizeOptional(language, nameof(language), 20);
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AudienceSnapshotId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public Guid? ProspectAccountId { get; private set; }
    public string EligibilityStatus { get; private set; } = null!;
    public string InclusionReason { get; private set; } = null!;
    public string ConsentStatus { get; private set; } = null!;
    public string? CommunicationLanguage { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class SalesCampaignMilestone : ICompanyOwnedEntity
{
    private SalesCampaignMilestone() { }
    public SalesCampaignMilestone(Guid id, Guid companyId, Guid campaignId, string name, DateTime dueUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        DueUtc = SalesEntityText.NormalizeUtc(dueUtc, nameof(dueUtc));
        Status = CampaignActivityStatuses.Planned;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public string Name { get; private set; } = null!;
    public DateTime DueUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

public sealed class SalesCampaignActivity : ICompanyOwnedEntity
{
    private SalesCampaignActivity() { }
    public SalesCampaignActivity(Guid id, Guid companyId, Guid campaignId, string name, string activityType,
        string channel, string executionMode, DateTime plannedStartUtc, DateTime dueUtc, string timeZoneId,
        Guid? ownerUserId = null, Guid? ownerAgentId = null, Guid? dependsOnActivityId = null,
        Guid? milestoneId = null, Guid? salesSequenceStepId = null, string? requiredToolCapability = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        plannedStartUtc = SalesEntityText.NormalizeUtc(plannedStartUtc, nameof(plannedStartUtc));
        dueUtc = SalesEntityText.NormalizeUtc(dueUtc, nameof(dueUtc));
        if (dueUtc < plannedStartUtc) throw new ArgumentException("Activity due date cannot be before its start date.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        ActivityType = SalesEntityText.NormalizeRequired(activityType, nameof(activityType), 64).ToLowerInvariant();
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 40).ToLowerInvariant();
        ExecutionMode = SalesEntityText.NormalizeRequired(executionMode, nameof(executionMode), 40).ToLowerInvariant();
        PlannedStartUtc = plannedStartUtc;
        DueUtc = dueUtc;
        TimeZoneId = SalesEntityText.NormalizeRequired(timeZoneId, nameof(timeZoneId), 128);
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId;
        DependsOnActivityId = dependsOnActivityId;
        MilestoneId = milestoneId;
        SalesSequenceStepId = salesSequenceStepId;
        RequiredToolCapability = SalesEntityText.NormalizeOptional(requiredToolCapability, nameof(requiredToolCapability), 120);
        Status = CampaignActivityStatuses.Planned;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid? MilestoneId { get; private set; }
    public Guid? DependsOnActivityId { get; private set; }
    public Guid? SalesSequenceStepId { get; private set; }
    public string Name { get; private set; } = null!;
    public string ActivityType { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string ExecutionMode { get; private set; } = null!;
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public DateTime PlannedStartUtc { get; private set; }
    public DateTime DueUtc { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public string? RequiredToolCapability { get; private set; }
    public string Status { get; private set; } = null!;
    public string? ResultSummary { get; private set; }
    public string? FailureReason { get; private set; }
    public string IdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");
    public int AttemptCount { get; private set; }
    public DateTime? ClaimedUtc { get; private set; }
    public string? ClaimToken { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesCampaign SalesCampaign { get; private set; } = null!;

    public void MarkReady()
    {
        if (Status != CampaignActivityStatuses.Planned) return;
        Status = CampaignActivityStatuses.Ready;
        UpdatedUtc = DateTime.UtcNow;
    }
    public bool TryClaim(string token, DateTime utcNow)
    {
        if (Status is not (CampaignActivityStatuses.Ready or CampaignActivityStatuses.Retrying) || DueUtc > utcNow) return false;
        Status = CampaignActivityStatuses.Ongoing;
        ClaimToken = SalesEntityText.NormalizeRequired(token, nameof(token), 64);
        ClaimedUtc = utcNow;
        AttemptCount++;
        UpdatedUtc = utcNow;
        return true;
    }
    public void Complete(string result)
    {
        Status = CampaignActivityStatuses.Completed;
        ResultSummary = SalesEntityText.NormalizeRequired(result, nameof(result), 1000);
        CompletedUtc = UpdatedUtc = DateTime.UtcNow;
        ClaimToken = null;
    }
    public void Fail(string reason, bool retryable)
    {
        Status = retryable ? CampaignActivityStatuses.Retrying : CampaignActivityStatuses.Failed;
        FailureReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        ClaimToken = null;
        UpdatedUtc = DateTime.UtcNow;
    }
    public void HoldForApproval()
    {
        Status = CampaignActivityStatuses.WaitingForApproval;
        ClaimToken = null;
        UpdatedUtc = DateTime.UtcNow;
    }
    public void Cancel(string reason)
    {
        Status = CampaignActivityStatuses.Cancelled;
        FailureReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        ClaimToken = null;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public static class CampaignActivityStatuses
{
    public const string Planned = "planned";
    public const string Ready = "ready";
    public const string Ongoing = "ongoing";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Retrying = "retrying";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class CampaignExecutionModes
{
    public const string Executable = "executable";
    public const string Manual = "manual";
    public const string Approval = "approval";
    public const string Handoff = "handoff";
}

public sealed class SalesCampaignKpiDefinition : ICompanyOwnedEntity
{
    private SalesCampaignKpiDefinition() { }

    public SalesCampaignKpiDefinition(Guid id, Guid companyId, Guid campaignId, string key, string label,
        string numerator, string? denominator, string unit, decimal? baseline, decimal? target,
        int attributionWindowDays, string dataSource, int version)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company is required.", nameof(companyId)) : companyId;
        SalesCampaignId = campaignId == Guid.Empty ? throw new ArgumentException("Campaign is required.", nameof(campaignId)) : campaignId;
        Key = SalesEntityText.NormalizeRequired(key, nameof(key), 80).ToLowerInvariant();
        Label = SalesEntityText.NormalizeRequired(label, nameof(label), 160);
        Numerator = SalesEntityText.NormalizeRequired(numerator, nameof(numerator), 80).ToLowerInvariant();
        Denominator = SalesEntityText.NormalizeOptional(denominator, nameof(denominator), 80)?.ToLowerInvariant();
        Unit = SalesEntityText.NormalizeRequired(unit, nameof(unit), 32).ToLowerInvariant();
        Baseline = baseline;
        Target = target;
        AttributionWindowDays = Math.Clamp(attributionWindowDays, 1, 730);
        DataSource = SalesEntityText.NormalizeRequired(dataSource, nameof(dataSource), 120);
        Version = Math.Max(1, version);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public string Key { get; private set; } = "";
    public string Label { get; private set; } = "";
    public string Numerator { get; private set; } = "";
    public string? Denominator { get; private set; }
    public string Unit { get; private set; } = "";
    public decimal? Baseline { get; private set; }
    public decimal? Target { get; private set; }
    public int AttributionWindowDays { get; private set; }
    public string DataSource { get; private set; } = "";
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class SalesCampaignKpiSnapshot : ICompanyOwnedEntity
{
    private SalesCampaignKpiSnapshot() { }

    public SalesCampaignKpiSnapshot(Guid id, Guid companyId, Guid campaignId, Guid definitionId,
        int definitionVersion, decimal? numeratorValue, decimal? denominatorValue, decimal? metricValue,
        DateTime observedUtc, string evidenceSummary)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company is required.", nameof(companyId)) : companyId;
        SalesCampaignId = campaignId == Guid.Empty ? throw new ArgumentException("Campaign is required.", nameof(campaignId)) : campaignId;
        DefinitionId = definitionId == Guid.Empty ? throw new ArgumentException("Definition is required.", nameof(definitionId)) : definitionId;
        DefinitionVersion = Math.Max(1, definitionVersion);
        NumeratorValue = numeratorValue;
        DenominatorValue = denominatorValue;
        MetricValue = metricValue;
        ObservedUtc = DateTime.SpecifyKind(observedUtc, DateTimeKind.Utc);
        EvidenceSummary = SalesEntityText.NormalizeRequired(evidenceSummary, nameof(evidenceSummary), 1000);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid DefinitionId { get; private set; }
    public int DefinitionVersion { get; private set; }
    public decimal? NumeratorValue { get; private set; }
    public decimal? DenominatorValue { get; private set; }
    public decimal? MetricValue { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public string EvidenceSummary { get; private set; } = "";
}

public sealed class SalesCampaignCost : ICompanyOwnedEntity
{
    private SalesCampaignCost() { }

    public SalesCampaignCost(Guid id, Guid companyId, Guid campaignId, string classification, decimal amount,
        string currency, string source, DateTime observedUtc, Guid? financeRecordId = null, Guid? activityId = null)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company is required.", nameof(companyId)) : companyId;
        SalesCampaignId = campaignId == Guid.Empty ? throw new ArgumentException("Campaign is required.", nameof(campaignId)) : campaignId;
        Classification = SalesEntityText.NormalizeRequired(classification, nameof(classification), 32).ToLowerInvariant();
        Amount = amount;
        Currency = SalesEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Source = SalesEntityText.NormalizeRequired(source, nameof(source), 120);
        ObservedUtc = DateTime.SpecifyKind(observedUtc, DateTimeKind.Utc);
        FinanceRecordId = financeRecordId;
        SalesCampaignActivityId = activityId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public string Classification { get; private set; } = "";
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public string Source { get; private set; } = "";
    public DateTime ObservedUtc { get; private set; }
    public Guid? FinanceRecordId { get; private set; }
    public Guid? SalesCampaignActivityId { get; private set; }
}
