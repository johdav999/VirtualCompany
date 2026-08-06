namespace VirtualCompany.Domain.Entities;

public static class MarketingQualificationStatuses
{
    public const string Qualified = "qualified";
    public const string NotQualified = "not_qualified";
    public const string Excluded = "excluded";
    public const string NeedsReview = "needs_review";
}

public sealed class MarketingQualificationDefinition : ICompanyOwnedEntity
{
    private MarketingQualificationDefinition() { }

    public MarketingQualificationDefinition(
        Guid id,
        Guid companyId,
        string name,
        string audienceType,
        string requiredChannel,
        decimal threshold,
        int freshnessDays,
        bool requiresCustomerCompany,
        DateTime effectiveFromUtc,
        DateTime? effectiveToUtc,
        string rulesJson,
        string exclusionsJson,
        Guid ownerUserId)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (threshold is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(threshold));
        if (freshnessDays is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(freshnessDays));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("An owner is required.", nameof(ownerUserId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        AudienceType = NormalizeAudienceType(audienceType);
        RequiredChannel = SalesEntityText.NormalizeRequired(requiredChannel, nameof(requiredChannel), 32).ToLowerInvariant();
        Threshold = threshold;
        FreshnessDays = freshnessDays;
        RequiresCustomerCompany = requiresCustomerCompany;
        EffectiveFromUtc = SalesEntityText.NormalizeUtc(effectiveFromUtc, nameof(effectiveFromUtc));
        EffectiveToUtc = effectiveToUtc.HasValue
            ? SalesEntityText.NormalizeUtc(effectiveToUtc.Value, nameof(effectiveToUtc))
            : null;
        if (EffectiveToUtc <= EffectiveFromUtc) throw new ArgumentException("The effective period is invalid.");
        RulesJson = SalesEntityText.NormalizeRequired(rulesJson, nameof(rulesJson), 8000);
        ExclusionsJson = SalesEntityText.NormalizeRequired(exclusionsJson, nameof(exclusionsJson), 8000);
        OwnerUserId = ownerUserId;
        Status = MarketingStatuses.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string AudienceType { get; private set; } = null!;
    public string RequiredChannel { get; private set; } = null!;
    public decimal Threshold { get; private set; }
    public int FreshnessDays { get; private set; }
    public bool RequiresCustomerCompany { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveToUtc { get; private set; }
    public string RulesJson { get; private set; } = null!;
    public string ExclusionsJson { get; private set; } = null!;
    public Guid OwnerUserId { get; private set; }
    public string Status { get; private set; } = null!;
    public int Version { get; private set; } = 1;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Activate()
    {
        if (Status != MarketingStatuses.Draft)
            throw new InvalidOperationException("Only draft qualification definitions can be activated.");
        Status = MarketingStatuses.Active;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Retire()
    {
        if (Status != MarketingStatuses.Active)
            throw new InvalidOperationException("Only active qualification definitions can be retired.");
        Status = MarketingStatuses.Completed;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeAudienceType(string value)
    {
        var normalized = SalesEntityText.NormalizeRequired(value, nameof(value), 16).ToLowerInvariant();
        return normalized is "b2b" or "b2c"
            ? normalized
            : throw new ArgumentException("Audience type must be b2b or b2c.", nameof(value));
    }
}

public sealed class MarketingQualificationEvaluation : ICompanyOwnedEntity
{
    private MarketingQualificationEvaluation() { }

    public MarketingQualificationEvaluation(
        Guid id,
        Guid companyId,
        Guid definitionId,
        int definitionVersion,
        Guid contactId,
        decimal score,
        string status,
        string reasonCodesJson,
        string evidenceReferencesJson,
        DateTime evidenceObservedUtc,
        string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (definitionId == Guid.Empty || contactId == Guid.Empty)
            throw new ArgumentException("Definition and contact are required.");
        if (definitionVersion <= 0) throw new ArgumentOutOfRangeException(nameof(definitionVersion));
        if (score is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(score));
        if (status is not (MarketingQualificationStatuses.Qualified or MarketingQualificationStatuses.NotQualified
            or MarketingQualificationStatuses.Excluded or MarketingQualificationStatuses.NeedsReview))
            throw new ArgumentException("Qualification status is invalid.", nameof(status));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingQualificationDefinitionId = definitionId;
        DefinitionVersion = definitionVersion;
        ContactId = contactId;
        Score = score;
        Status = status;
        ReasonCodesJson = SalesEntityText.NormalizeRequired(reasonCodesJson, nameof(reasonCodesJson), 8000);
        EvidenceReferencesJson = SalesEntityText.NormalizeRequired(evidenceReferencesJson, nameof(evidenceReferencesJson), 8000);
        EvidenceObservedUtc = SalesEntityText.NormalizeUtc(evidenceObservedUtc, nameof(evidenceObservedUtc));
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 160);
        EvaluatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingQualificationDefinitionId { get; private set; }
    public int DefinitionVersion { get; private set; }
    public Guid ContactId { get; private set; }
    public decimal Score { get; private set; }
    public string Status { get; private set; } = null!;
    public string ReasonCodesJson { get; private set; } = null!;
    public string EvidenceReferencesJson { get; private set; } = null!;
    public DateTime EvidenceObservedUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime EvaluatedUtc { get; private set; }
}

public sealed class MarketingQualificationFeedback : ICompanyOwnedEntity
{
    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.Ordinal)
    {
        "accepted", "rejected", "duplicate", "bad_fit", "timing"
    };

    private MarketingQualificationFeedback() { }

    public MarketingQualificationFeedback(Guid id, Guid companyId, Guid evaluationId, string decision,
        string reason, Guid? leadId, Guid? dealId, Guid decidedByUserId)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (evaluationId == Guid.Empty || decidedByUserId == Guid.Empty)
            throw new ArgumentException("Evaluation and deciding user are required.");
        var normalizedDecision = SalesEntityText.NormalizeRequired(decision, nameof(decision), 32).ToLowerInvariant();
        if (!AllowedDecisions.Contains(normalizedDecision))
            throw new ArgumentException("Feedback decision is invalid.", nameof(decision));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingQualificationEvaluationId = evaluationId;
        Decision = normalizedDecision;
        Reason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        LinkedLeadId = leadId;
        LinkedDealId = dealId;
        DecidedByUserId = decidedByUserId;
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingQualificationEvaluationId { get; private set; }
    public string Decision { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public Guid? LinkedLeadId { get; private set; }
    public Guid? LinkedDealId { get; private set; }
    public Guid DecidedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}
