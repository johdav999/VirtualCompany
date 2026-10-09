

namespace VirtualCompany.Application.Agents;

public sealed record SupervisionEvidence(Guid Id, string Source, DateTime OccurredUtc, string Description);


public sealed record AgentSupervisionQuery(Guid CompanyId, DateOnly? From = null, DateOnly? To = null,
    string? Responsibility = null, string? TaskType = null, Guid? AgentId = null,
    string View = "work", string? Metric = null);

public sealed record SupervisionMeasure(string Code, string View, string Name, int? Count, int Denominator,
    string DenominatorMeaning, string Definition, string Coverage, decimal? SecondsInPeriod = null,
    decimal? AverageTotalSeconds = null);

public sealed record AgentSupervisionReport(AgentSupervisionQuery Query, DateTime ObservedUtc,
    DateTime FromUtc, DateTime UntilUtc, string Timezone, bool Partial, IReadOnlyList<string> Coverage,
    IReadOnlyList<string> Responsibilities, IReadOnlyList<string> TaskTypes, IReadOnlyList<SupervisionAgent> Agents,
    IReadOnlyList<SupervisionMeasure> Measures, IReadOnlyList<SupervisionRow> Rows, string SnapshotHash);

public sealed record SupervisionCsv(string FileName, string Content, AgentSupervisionReport Report);

public sealed record SupervisionAgent(Guid Id, string Name, string Responsibility);

public sealed record SupervisionRow(string Key, string Metric, string WorkKind, Guid WorkId, string Title,
    string Responsibility, string TaskType, Guid? AgentId, string AgentName, string State, DateTime RecordedUtc,
    string EvidenceStage, string WorkRoute, Guid? ApprovalId, decimal? SecondsInPeriod, decimal? TotalSeconds,
    bool Ongoing, IReadOnlyList<SupervisionEvidence> Evidence);
