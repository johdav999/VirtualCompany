namespace VirtualCompany.Domain.Entities;

public static class MarketingStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Completed = "completed";
    public const string Proposed = "proposed";
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Expired = "expired";
    public const string InReview = "in_review";
    public const string Cancelled = "cancelled";
}

public sealed class MarketingObjective : ICompanyOwnedEntity
{
    private MarketingObjective() { }

    public MarketingObjective(Guid id, Guid companyId, string name, string objectiveType, decimal targetValue,
        string unit, DateTime periodStartUtc, DateTime periodEndUtc, Guid? ownerUserId = null, Guid? ownerAgentId = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (targetValue <= 0) throw new ArgumentOutOfRangeException(nameof(targetValue));
        periodStartUtc = SalesEntityText.NormalizeUtc(periodStartUtc, nameof(periodStartUtc));
        periodEndUtc = SalesEntityText.NormalizeUtc(periodEndUtc, nameof(periodEndUtc));
        if (periodEndUtc <= periodStartUtc) throw new ArgumentException("Objective period must end after it starts.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        ObjectiveType = SalesEntityText.NormalizeRequired(objectiveType, nameof(objectiveType), 64).ToLowerInvariant();
        TargetValue = targetValue;
        Unit = SalesEntityText.NormalizeRequired(unit, nameof(unit), 40).ToLowerInvariant();
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId;
        Status = MarketingStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string ObjectiveType { get; private set; } = null!;
    public decimal TargetValue { get; private set; }
    public string Unit { get; private set; } = null!;
    public decimal? BaselineValue { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public string Status { get; private set; } = null!;
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void SetBaseline(decimal? value) { BaselineValue = value; Touch(); }
    public void Activate()
    {
        if (Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only draft objectives can be activated.");
        Status = MarketingStatuses.Active;
        Touch();
    }
    public void Complete()
    {
        if (Status != MarketingStatuses.Active) throw new InvalidOperationException("Only active objectives can be completed.");
        Status = MarketingStatuses.Completed;
        Touch();
    }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class MarketingPlan : ICompanyOwnedEntity
{
    private MarketingPlan() { }

    public MarketingPlan(Guid id, Guid companyId, string name, string summary, DateTime startsUtc, DateTime endsUtc,
        decimal? plannedBudget, string budgetCurrency, Guid? ownerUserId = null, Guid? ownerAgentId = null,
        string? idempotencyKey = null, Guid? strategyId = null, int? strategyVersion = null,
        string rationale = "Legacy plan", string evidenceReferencesJson = "[]",
        string missingEvidenceJson = "[]", Guid? approvalRequestId = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        startsUtc = SalesEntityText.NormalizeUtc(startsUtc, nameof(startsUtc));
        endsUtc = SalesEntityText.NormalizeUtc(endsUtc, nameof(endsUtc));
        if (endsUtc <= startsUtc) throw new ArgumentException("Plan period must end after it starts.");
        if (plannedBudget is < 0) throw new ArgumentOutOfRangeException(nameof(plannedBudget));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 4000);
        StartsUtc = startsUtc;
        EndsUtc = endsUtc;
        PlannedBudget = plannedBudget;
        BudgetCurrency = SalesEntityText.NormalizeRequired(budgetCurrency, nameof(budgetCurrency), 3).ToUpperInvariant();
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId;
        IdempotencyKey = SalesEntityText.NormalizeOptional(idempotencyKey, nameof(idempotencyKey), 160);
        MarketingStrategyId = strategyId;
        MarketingStrategyVersion = strategyVersion;
        Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 4000);
        EvidenceReferencesJson = SalesEntityText.NormalizeRequired(evidenceReferencesJson, nameof(evidenceReferencesJson), 16000);
        MissingEvidenceJson = SalesEntityText.NormalizeRequired(missingEvidenceJson, nameof(missingEvidenceJson), 16000);
        ApprovalRequestId = approvalRequestId;
        Status = MarketingStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public DateTime StartsUtc { get; private set; }
    public DateTime EndsUtc { get; private set; }
    public decimal? PlannedBudget { get; private set; }
    public string BudgetCurrency { get; private set; } = null!;
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public Guid? MarketingStrategyId { get; private set; }
    public int? MarketingStrategyVersion { get; private set; }
    public string Rationale { get; private set; } = null!;
    public string EvidenceReferencesJson { get; private set; } = "[]";
    public string MissingEvidenceJson { get; private set; } = "[]";
    public Guid? ApprovalRequestId { get; private set; }
    public string Status { get; private set; } = null!;
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Activate()
    {
        if (MarketingStrategyId.HasValue && Status != MarketingStatuses.Approved) throw new InvalidOperationException("A grounded plan must be approved before activation.");
        if (!MarketingStrategyId.HasValue && Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only a draft legacy plan can be activated.");
        Status = MarketingStatuses.Active;
        Touch();
    }

    public void SubmitForReview(Guid approvalRequestId)
    {
        if (Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only draft plans can be submitted for review.");
        ApprovalRequestId = approvalRequestId == Guid.Empty ? throw new ArgumentException("Approval request is required.") : approvalRequestId;
        Status = MarketingStatuses.InReview;
        Touch();
    }

    public void MarkApproved() { if (Status != MarketingStatuses.InReview) throw new InvalidOperationException("Only plans in review can be approved."); Status = MarketingStatuses.Approved; Touch(); }
    public void Complete() { if (Status != MarketingStatuses.Active) throw new InvalidOperationException("Only active plans can be completed."); Status = MarketingStatuses.Completed; Touch(); }
    public void Cancel() { if (Status is MarketingStatuses.Completed or MarketingStatuses.Cancelled) throw new InvalidOperationException("The plan is already closed."); Status = MarketingStatuses.Cancelled; Touch(); }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
}

public sealed class MarketingPlanObjective : ICompanyOwnedEntity
{
    private MarketingPlanObjective() { }
    public MarketingPlanObjective(Guid id, Guid companyId, Guid planId, Guid objectiveId)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingPlanId = planId == Guid.Empty ? throw new ArgumentException("Plan is required.") : planId;
        MarketingObjectiveId = objectiveId == Guid.Empty ? throw new ArgumentException("Objective is required.") : objectiveId;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingPlanId { get; private set; }
    public Guid MarketingObjectiveId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingContentBrief : ICompanyOwnedEntity
{
    private MarketingContentBrief() { }
    public MarketingContentBrief(Guid id, Guid companyId, string title, string purpose, string audience,
        string channel, string language, string tone, string callToAction, Guid? campaignId, Guid? planId,
        DateTime? dueUtc, Guid? ownerUserId, Guid? ownerAgentId, Guid? segmentVersionId = null,
        string measurableObjective = "Not specified", string funnelStage = "awareness",
        string customerInsight = "Not specified", string keyMessage = "Not specified",
        string supportingPointsJson = "[]", string offer = "Not specified", string requiredClaimsJson = "[]",
        string prohibitedClaimsJson = "[]", string seoRequirementsJson = "{}", string visualDirection = "Not specified",
        string desiredFormatsJson = "[]", string variantRequirementsJson = "{}",
        string evidenceRequirementsJson = "{}", string approvalPolicyJson = "{}")
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        Purpose = SalesEntityText.NormalizeRequired(purpose, nameof(purpose), 2000);
        Audience = SalesEntityText.NormalizeRequired(audience, nameof(audience), 1000);
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 40).ToLowerInvariant();
        Language = SalesEntityText.NormalizeRequired(language, nameof(language), 20);
        Tone = SalesEntityText.NormalizeRequired(tone, nameof(tone), 120);
        CallToAction = SalesEntityText.NormalizeRequired(callToAction, nameof(callToAction), 500);
        SalesCampaignId = campaignId;
        MarketingPlanId = planId;
        DueUtc = dueUtc.HasValue ? SalesEntityText.NormalizeUtc(dueUtc.Value, nameof(dueUtc)) : null;
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId;
        MarketingCustomerSegmentVersionId = segmentVersionId;
        MeasurableObjective = SalesEntityText.NormalizeRequired(measurableObjective, nameof(measurableObjective), 1000);
        FunnelStage = SalesEntityText.NormalizeRequired(funnelStage, nameof(funnelStage), 40).ToLowerInvariant();
        CustomerInsight = SalesEntityText.NormalizeRequired(customerInsight, nameof(customerInsight), 4000);
        KeyMessage = SalesEntityText.NormalizeRequired(keyMessage, nameof(keyMessage), 2000);
        SupportingPointsJson = SalesEntityText.NormalizeRequired(supportingPointsJson, nameof(supportingPointsJson), 8000);
        Offer = SalesEntityText.NormalizeRequired(offer, nameof(offer), 2000);
        RequiredClaimsJson = SalesEntityText.NormalizeRequired(requiredClaimsJson, nameof(requiredClaimsJson), 8000);
        ProhibitedClaimsJson = SalesEntityText.NormalizeRequired(prohibitedClaimsJson, nameof(prohibitedClaimsJson), 8000);
        SeoRequirementsJson = SalesEntityText.NormalizeRequired(seoRequirementsJson, nameof(seoRequirementsJson), 8000);
        VisualDirection = SalesEntityText.NormalizeRequired(visualDirection, nameof(visualDirection), 2000);
        DesiredFormatsJson = SalesEntityText.NormalizeRequired(desiredFormatsJson, nameof(desiredFormatsJson), 4000);
        VariantRequirementsJson = SalesEntityText.NormalizeRequired(variantRequirementsJson, nameof(variantRequirementsJson), 8000);
        EvidenceRequirementsJson = SalesEntityText.NormalizeRequired(evidenceRequirementsJson, nameof(evidenceRequirementsJson), 8000);
        ApprovalPolicyJson = SalesEntityText.NormalizeRequired(approvalPolicyJson, nameof(approvalPolicyJson), 8000);
        Status = MarketingStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SalesCampaignId { get; private set; }
    public Guid? MarketingPlanId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;
    public string Audience { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string Language { get; private set; } = null!;
    public string Tone { get; private set; } = null!;
    public string CallToAction { get; private set; } = null!;
    public DateTime? DueUtc { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public Guid? MarketingCustomerSegmentVersionId { get; private set; }
    public string MeasurableObjective { get; private set; } = null!;
    public string FunnelStage { get; private set; } = null!;
    public string CustomerInsight { get; private set; } = null!;
    public string KeyMessage { get; private set; } = null!;
    public string SupportingPointsJson { get; private set; } = null!;
    public string Offer { get; private set; } = null!;
    public string RequiredClaimsJson { get; private set; } = null!;
    public string ProhibitedClaimsJson { get; private set; } = null!;
    public string SeoRequirementsJson { get; private set; } = null!;
    public string VisualDirection { get; private set; } = null!;
    public string DesiredFormatsJson { get; private set; } = null!;
    public string VariantRequirementsJson { get; private set; } = null!;
    public string EvidenceRequirementsJson { get; private set; } = null!;
    public string ApprovalPolicyJson { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Submit()
    {
        if (Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only draft content can be submitted.");
        Status = MarketingStatuses.Submitted;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }
    public void Review(bool approved)
    {
        if (Status != MarketingStatuses.Submitted) throw new InvalidOperationException("Only submitted content can be reviewed.");
        Status = approved ? MarketingStatuses.Approved : MarketingStatuses.Rejected;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class MarketingContentVariant : ICompanyOwnedEntity
{
    private MarketingContentVariant() { }
    public MarketingContentVariant(Guid id, Guid companyId, Guid briefId, string name, string body,
        string sourceReferences, bool generatedByAi, string contentFormat = "text", Guid? generationRunId = null,
        string capabilityVersion = "manual", string promptVersion = "manual", string? idempotencyKey = null,
        int batchIndex = 0, Guid? variantFamilyId = null, int versionNumber = 1)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingContentBriefId = briefId == Guid.Empty ? throw new ArgumentException("Brief is required.") : briefId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        Body = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);
        SourceReferences = SalesEntityText.NormalizeRequired(sourceReferences, nameof(sourceReferences), 4000);
        GeneratedByAi = generatedByAi;
        ContentFormat = SalesEntityText.NormalizeRequired(contentFormat, nameof(contentFormat), 60).ToLowerInvariant();
        GenerationRunId = generationRunId;
        CapabilityVersion = SalesEntityText.NormalizeRequired(capabilityVersion, nameof(capabilityVersion), 64);
        PromptVersion = SalesEntityText.NormalizeRequired(promptVersion, nameof(promptVersion), 128);
        IdempotencyKey = SalesEntityText.NormalizeOptional(idempotencyKey, nameof(idempotencyKey), 160);
        if (batchIndex < 0 || versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(batchIndex));
        BatchIndex = batchIndex; VariantFamilyId = variantFamilyId.GetValueOrDefault(Id); VersionNumber = versionNumber;
        Status = MarketingStatuses.Draft;
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingContentBriefId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string SourceReferences { get; private set; } = null!;
    public bool GeneratedByAi { get; private set; }
    public Guid VariantFamilyId { get; private set; }
    public int VersionNumber { get; private set; }
    public string ContentFormat { get; private set; } = null!;
    public Guid? GenerationRunId { get; private set; }
    public string CapabilityVersion { get; private set; } = null!;
    public string PromptVersion { get; private set; } = null!;
    public string? IdempotencyKey { get; private set; }
    public int BatchIndex { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public void Review(bool approved)
    {
        if (Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only draft variants can be reviewed.");
        Status = approved ? MarketingStatuses.Approved : MarketingStatuses.Rejected;
    }
    public void Retire()
    {
        if (Status == "retired") return;
        Status = "retired";
    }
}

public sealed class MarketingSalesHandoff : ICompanyOwnedEntity
{
    private MarketingSalesHandoff() { }
    public MarketingSalesHandoff(Guid id, Guid companyId, Guid? campaignId, Guid? contactId,
        Guid? customerCompanyId, string reason, string suggestedAction, string urgency, DateTime expiresUtc,
        string evidenceReferences, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (!contactId.HasValue && !customerCompanyId.HasValue) throw new ArgumentException("A contact or company is required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        ContactId = contactId;
        CustomerCompanyId = customerCompanyId;
        Reason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 2000);
        SuggestedAction = SalesEntityText.NormalizeRequired(suggestedAction, nameof(suggestedAction), 1000);
        Urgency = SalesEntityText.NormalizeRequired(urgency, nameof(urgency), 32).ToLowerInvariant();
        ExpiresUtc = SalesEntityText.NormalizeUtc(expiresUtc, nameof(expiresUtc));
        EvidenceReferences = SalesEntityText.NormalizeRequired(evidenceReferences, nameof(evidenceReferences), 4000);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        Status = MarketingStatuses.Proposed;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SalesCampaignId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public Guid? LinkedLeadId { get; private set; }
    public Guid? LinkedDealId { get; private set; }
    public string Reason { get; private set; } = null!;
    public string SuggestedAction { get; private set; } = null!;
    public string Urgency { get; private set; } = null!;
    public DateTime ExpiresUtc { get; private set; }
    public string EvidenceReferences { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? DecisionReason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Decide(bool accepted, string reason, Guid? leadId, Guid? dealId)
    {
        if (Status != MarketingStatuses.Proposed) throw new InvalidOperationException("Only proposed handoffs can be decided.");
        Status = accepted ? MarketingStatuses.Accepted : MarketingStatuses.Declined;
        DecisionReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        LinkedLeadId = leadId;
        LinkedDealId = dealId;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class MarketingChannelObservation : ICompanyOwnedEntity
{
    private MarketingChannelObservation() { }
    public MarketingChannelObservation(Guid id, Guid companyId, string provider, string metricCode, decimal value,
        string unit, DateTime periodStartUtc, DateTime periodEndUtc, Guid? campaignId, Guid? activityId,
        string sourceReference, string idempotencyKey, Guid? correctionOfObservationId = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Provider = SalesEntityText.NormalizeRequired(provider, nameof(provider), 80).ToLowerInvariant();
        MetricCode = SalesEntityText.NormalizeRequired(metricCode, nameof(metricCode), 80).ToLowerInvariant();
        Value = value;
        Unit = SalesEntityText.NormalizeRequired(unit, nameof(unit), 40).ToLowerInvariant();
        PeriodStartUtc = SalesEntityText.NormalizeUtc(periodStartUtc, nameof(periodStartUtc));
        PeriodEndUtc = SalesEntityText.NormalizeUtc(periodEndUtc, nameof(periodEndUtc));
        if (PeriodEndUtc <= PeriodStartUtc) throw new ArgumentException("Observation period is invalid.");
        SalesCampaignId = campaignId;
        SalesCampaignActivityId = activityId;
        SourceReference = SalesEntityText.NormalizeRequired(sourceReference, nameof(sourceReference), 1000);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        CorrectionOfObservationId = correctionOfObservationId;
        RetrievedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SalesCampaignId { get; private set; }
    public Guid? SalesCampaignActivityId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string MetricCode { get; private set; } = null!;
    public decimal Value { get; private set; }
    public string Unit { get; private set; } = null!;
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public string SourceReference { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? CorrectionOfObservationId { get; private set; }
    public bool IsSuperseded { get; private set; }
    public DateTime RetrievedUtc { get; private set; }
    public void Supersede() => IsSuperseded = true;
}

public sealed class MarketingExperiment : ICompanyOwnedEntity
{
    private MarketingExperiment() { }
    public MarketingExperiment(Guid id, Guid companyId, string name, string hypothesis, string primaryMetric,
        string guardrailMetric, int minimumSampleSize, DateTime startsUtc, DateTime endsUtc, Guid? campaignId)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (minimumSampleSize <= 0) throw new ArgumentOutOfRangeException(nameof(minimumSampleSize));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = campaignId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        Hypothesis = SalesEntityText.NormalizeRequired(hypothesis, nameof(hypothesis), 2000);
        PrimaryMetric = SalesEntityText.NormalizeRequired(primaryMetric, nameof(primaryMetric), 80).ToLowerInvariant();
        GuardrailMetric = SalesEntityText.NormalizeRequired(guardrailMetric, nameof(guardrailMetric), 80).ToLowerInvariant();
        MinimumSampleSize = minimumSampleSize;
        StartsUtc = SalesEntityText.NormalizeUtc(startsUtc, nameof(startsUtc));
        EndsUtc = SalesEntityText.NormalizeUtc(endsUtc, nameof(endsUtc));
        if (EndsUtc <= StartsUtc) throw new ArgumentException("Experiment period is invalid.");
        Status = MarketingStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SalesCampaignId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Hypothesis { get; private set; } = null!;
    public string PrimaryMetric { get; private set; } = null!;
    public string GuardrailMetric { get; private set; } = null!;
    public int MinimumSampleSize { get; private set; }
    public DateTime StartsUtc { get; private set; }
    public DateTime EndsUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Decision { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Activate()
    {
        if (Status != MarketingStatuses.Draft) throw new InvalidOperationException("Only draft experiments can be activated.");
        Status = MarketingStatuses.Active;
        UpdatedUtc = DateTime.UtcNow;
    }
    public void Complete(string decision)
    {
        if (Status != MarketingStatuses.Active) throw new InvalidOperationException("Only active experiments can be completed.");
        Decision = SalesEntityText.NormalizeRequired(decision, nameof(decision), 2000);
        Status = MarketingStatuses.Completed;
        UpdatedUtc = DateTime.UtcNow;
    }
}
