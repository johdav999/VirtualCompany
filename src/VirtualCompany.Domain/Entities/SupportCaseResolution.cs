using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportCaseResolution : ICompanyOwnedEntity
{
    private SupportCaseResolution()
    {
    }

    public SupportCaseResolution(Guid id, Guid companyId, Guid supportCaseId, string summary, string outcome, Guid resolvedByUserId, DateTime resolvedUtc, string rootCauseCategory = "other", string? actionTaken = null, string? reusableAnswer = null, string? customerPreferenceObservations = null, string? relevantLinksJson = null, bool reuseEligible = false)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Summary = SupportEntityText.NormalizeRequired(summary, nameof(summary), 2000);
        Outcome = SupportEntityText.NormalizeRequired(outcome, nameof(outcome), 120);
        RootCauseCategory = SupportEntityText.NormalizeRequired(rootCauseCategory, nameof(rootCauseCategory), 80).ToLowerInvariant();
        ActionTaken = SupportEntityText.NormalizeOptional(actionTaken, nameof(actionTaken), 2000);
        ReusableAnswer = SupportEntityText.NormalizeOptional(reusableAnswer, nameof(reusableAnswer), 4000);
        CustomerPreferenceObservations = SupportEntityText.NormalizeOptional(customerPreferenceObservations, nameof(customerPreferenceObservations), 2000);
        RelevantLinksJson = SupportEntityText.NormalizeOptional(relevantLinksJson, nameof(relevantLinksJson), 4000);
        ReuseEligible = reuseEligible && !string.IsNullOrWhiteSpace(ReusableAnswer);
        ResolvedByUserId = resolvedByUserId == Guid.Empty ? throw new ArgumentException("ResolvedByUserId is required.", nameof(resolvedByUserId)) : resolvedByUserId;
        ResolvedUtc = SupportEntityText.NormalizeUtc(resolvedUtc, nameof(resolvedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string Summary { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string RootCauseCategory { get; private set; } = "other";
    public string? ActionTaken { get; private set; }
    public string? ReusableAnswer { get; private set; }
    public string? CustomerPreferenceObservations { get; private set; }
    public string? RelevantLinksJson { get; private set; }
    public bool ReuseEligible { get; private set; }
    public Guid ResolvedByUserId { get; private set; }
    public DateTime ResolvedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

