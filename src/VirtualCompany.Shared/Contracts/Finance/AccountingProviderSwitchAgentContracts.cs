using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;
public sealed record AccountingProviderSwitchAgentEvidenceItemDto(string Label, string Status, string Explanation, string? Reference = null, bool NeedsAttention = false)
{
    public string Label { get; set; } = Label;
    public string Status { get; set; } = Status;
    public string Explanation { get; set; } = Explanation;
    public string? Reference { get; set; } = Reference;
    public bool NeedsAttention { get; set; } = NeedsAttention;

    public AccountingProviderSwitchAgentEvidenceItemDto() : this(string.Empty, string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchAgentEvidenceDto(Guid SwitchId, long SwitchVersion, string View, string Summary, IReadOnlyList<AccountingProviderSwitchAgentEvidenceItemDto> Items, IReadOnlyList<string> DataSources, DateTime AsOfUtc)
{
    public Guid SwitchId { get; set; } = SwitchId;
    public long SwitchVersion { get; set; } = SwitchVersion;
    public string View { get; set; } = View;
    public string Summary { get; set; } = Summary;
    public IReadOnlyList<AccountingProviderSwitchAgentEvidenceItemDto> Items { get; set; } = Items;
    public IReadOnlyList<string> DataSources { get; set; } = DataSources;
    public DateTime AsOfUtc { get; set; } = AsOfUtc;

    public AccountingProviderSwitchAgentEvidenceDto() : this(default !, default !, default !, string.Empty, [], [], default !)
    {
    }
}

public sealed record AccountingProviderSwitchAgentRecommendationDto(Guid? SwitchId, long? SwitchVersion, string RecommendationType, string Recommendation, string Rationale, IReadOnlyList<string> Preconditions, IReadOnlyList<string> DataSources, decimal Confidence, DateTime GeneratedUtc)
{
    public Guid? SwitchId { get; set; } = SwitchId;
    public long? SwitchVersion { get; set; } = SwitchVersion;
    public string RecommendationType { get; set; } = RecommendationType;
    public string Recommendation { get; set; } = Recommendation;
    public string Rationale { get; set; } = Rationale;
    public IReadOnlyList<string> Preconditions { get; set; } = Preconditions;
    public IReadOnlyList<string> DataSources { get; set; } = DataSources;
    public decimal Confidence { get; set; } = Confidence;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;

    public AccountingProviderSwitchAgentRecommendationDto() : this(default !, default !, default !, string.Empty, string.Empty, [], [], default !, default !)
    {
    }
}

public sealed record AccountingProviderSwitchAgentBriefingDto(Guid SwitchId, long SwitchVersion, string CurrentStep, string WhyItMatters, IReadOnlyList<string> Blockers, IReadOnlyList<string> Evidence, IReadOnlyList<string> AllowedActions, string ResponsibleParty, string NextCheckpoint, IReadOnlyList<string> DataSources, DateTime GeneratedUtc)
{
    public Guid SwitchId { get; set; } = SwitchId;
    public long SwitchVersion { get; set; } = SwitchVersion;
    public string CurrentStep { get; set; } = CurrentStep;
    public string WhyItMatters { get; set; } = WhyItMatters;
    public IReadOnlyList<string> Blockers { get; set; } = Blockers;
    public IReadOnlyList<string> Evidence { get; set; } = Evidence;
    public IReadOnlyList<string> AllowedActions { get; set; } = AllowedActions;
    public string ResponsibleParty { get; set; } = ResponsibleParty;
    public string NextCheckpoint { get; set; } = NextCheckpoint;
    public IReadOnlyList<string> DataSources { get; set; } = DataSources;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;

    public AccountingProviderSwitchAgentBriefingDto() : this(default !, default !, string.Empty, string.Empty, [], [], [], string.Empty, string.Empty, [], default !)
    {
    }
}
