using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceSeedAnomalyDto(Guid Id, string AnomalyType, string ScenarioProfile, IReadOnlyList<Guid> AffectedRecordIds, string ExpectedDetectionMetadataJson)
{
    public Guid Id { get; set; } = Id;
    public string AnomalyType { get; set; } = AnomalyType;
    public string ScenarioProfile { get; set; } = ScenarioProfile;
    public IReadOnlyList<Guid> AffectedRecordIds { get; set; } = AffectedRecordIds;
    public string ExpectedDetectionMetadataJson { get; set; } = ExpectedDetectionMetadataJson;

    public FinanceSeedAnomalyDto() : this(default !, string.Empty, string.Empty, [], string.Empty)
    {
    }
}

public sealed record CompanySimulationClockDto(Guid CompanyId, DateTime CurrentUtc, bool Enabled, bool AutoAdvanceEnabled, int DefaultStepHours, int AutoAdvanceIntervalSeconds, DateTime? LastAdvancedUtc)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateTime CurrentUtc { get; set; } = CurrentUtc;
    public bool Enabled { get; set; } = Enabled;
    public bool AutoAdvanceEnabled { get; set; } = AutoAdvanceEnabled;
    public int DefaultStepHours { get; set; } = DefaultStepHours;
    public int AutoAdvanceIntervalSeconds { get; set; } = AutoAdvanceIntervalSeconds;
    public DateTime? LastAdvancedUtc { get; set; } = LastAdvancedUtc;

    public CompanySimulationClockDto() : this(default !, default !, default !, default !, default !, default !, default !)
    {
    }
}
