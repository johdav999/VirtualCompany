

namespace VirtualCompany.Application.Orchestration;


public sealed record PlanningPeriod(int FiscalYear, int Quarter, DateTime StartUtc, DateTime EndUtc,
    string Timezone, string Currency, long CalendarVersion, int StartMonth, int StartDay);

public sealed record QuarterMilestone(string Title, DateTime DueUtc, string Status);

public sealed record QuarterReviewDocument(QuarterReviewSummary Summary, QuarterReviewPreview Review,
    IReadOnlyList<string> Changes);

public sealed record PlanningEvidenceOption(Guid Id, string Lens, int Year, int Month, int Revision);

public sealed record PreviewQuarterReview(int FiscalYear, int Quarter,
    IReadOnlyList<QuarterObjectiveInput> Objectives, IReadOnlyList<QuarterResourceInput> Resources, string Notes);

public sealed record QuarterReviewPreview(Guid CompanyId, PlanningPeriod Period,
    PreviewQuarterReview Proposal, IReadOnlyList<QuarterObjectiveProgress> Objectives,
    IReadOnlyList<QuarterResourceConflict> Conflicts, string Fingerprint);

public sealed record PlanningInitiative(Guid Id, Guid PlanId, Guid GoalId, string Title, string Status,
    Guid? OwnerAgentId, DateTime? TargetUtc, IReadOnlyList<Guid> DependsOn);

public sealed record QuarterMeasureLink(Guid SnapshotId, string MeasureKey);

public sealed record PlanningOwner(Guid Id, string Name);

public sealed record QuarterObjectiveInput(Guid GoalId, int GoalVersion, Guid OwnerUserId,
    decimal Baseline, decimal Target, string Unit, string Direction,
    IReadOnlyList<QuarterMeasureLink> Measures, IReadOnlyList<QuarterMilestone> Milestones,
    IReadOnlyList<Guid> InitiativeIds);

public sealed record QuarterResourceConflict(string Pool, decimal AvailableHours, decimal CommittedHours,
    decimal Shortfall, string Explanation);

public sealed record QuarterObjectiveProgress(QuarterObjectiveInput Objective, string Name, string Owner,
    decimal? Actual, decimal? ProgressPercent, string Outcome, string EvidenceState,
    IReadOnlyList<string> EvidenceLinks, IReadOnlyList<string> DependencyRisks);

public sealed record QuarterResourceInput(Guid GoalId, Guid OwnerUserId, string Pool,
    decimal AvailableHours, decimal ProposedHours, Guid SnapshotId, string Rationale);

public sealed record SaveQuarterReview(PreviewQuarterReview Proposal, Guid? PreviousId,
    int ExpectedRevision, Guid RequestId, string ExpectedFingerprint);

public sealed record QuarterlyPlanningOptions(Guid CompanyId, PlanningPeriod Period,
    IReadOnlyList<CompanyGoalDto> Goals, IReadOnlyList<PlanningOwner> Owners,
    IReadOnlyList<PlanningEvidenceOption> Evidence, IReadOnlyList<PlanningInitiative> Initiatives);

public sealed record QuarterReviewSummary(Guid Id, Guid CompanyId, int FiscalYear, int Quarter,
    int Revision, Guid? PreviousId, Guid AuthorUserId, DateTime SavedUtc, string Notes);
