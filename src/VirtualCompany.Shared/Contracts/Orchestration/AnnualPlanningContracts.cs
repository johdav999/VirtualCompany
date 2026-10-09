

namespace VirtualCompany.Application.Orchestration;

public sealed record AnnualObjectiveInput(Guid GoalId, int GoalVersion, Guid OwnerUserId, Guid BaselineReviewId, decimal Target, string Unit, IReadOnlyList<AnnualMilestoneInput> Milestones, IReadOnlyList<Guid> InitiativeIds);

public sealed record AnnualPlanInput(int FiscalYear, IReadOnlyList<AnnualObjectiveInput> Objectives, IReadOnlyList<Guid> BudgetIds, IReadOnlyList<AnnualAllocationInput> Allocations, string Notes);

public sealed record AnnualMilestoneInput(int Quarter, string Title, DateTime DueUtc);

public sealed record AnnualBudgetOption(Guid Id, Guid AccountId, string Account, DateTime MonthUtc, string Version, string Currency, decimal Amount, Guid? CostCenterId, string Fingerprint);

public sealed record AnnualPlanDocument(AnnualPlanSummary Summary, AnnualPlanPreview Plan, IReadOnlyList<string> Changes, bool CanReview, string Governance);

public sealed record AnnualPlanningOptions(Guid CompanyId, PlanningPeriod Period, IReadOnlyList<CompanyGoalDto> Goals, IReadOnlyList<PlanningOwner> Owners, IReadOnlyList<QuarterReviewSummary> QuarterReviews, IReadOnlyList<AnnualBudgetOption> Budgets, IReadOnlyList<PlanningInitiative> Initiatives);

public sealed record AnnualPlanPreview(Guid CompanyId, PlanningPeriod Period, AnnualPlanInput Input, IReadOnlyList<AnnualObjectiveView> Objectives, IReadOnlyList<AnnualBudgetOption> Budgets, decimal BudgetTotal, decimal AllocationTotal, IReadOnlyList<string> ReviewBlocks, string Fingerprint);

public sealed record AnnualObjectiveView(AnnualObjectiveInput Input, string Name, string Owner, string MetricKey, decimal Baseline, decimal? ProgressPercent, string EvidenceState, string SourceLink, IReadOnlyList<string> Dependencies);

public sealed record SaveAnnualPlan(AnnualPlanInput Input, Guid? PreviousId, int ExpectedVersion, Guid RequestId, string ExpectedFingerprint);

public sealed record ReviewAnnualPlan(int ExpectedStateRevision, string ExpectedFingerprint);

public sealed record AnnualAllocationInput(Guid BudgetId, Guid OwnerUserId, Guid? GoalId, string Kind, string Title, decimal Amount, int Quarter);

public sealed record AnnualPlanSummary(Guid Id, Guid CompanyId, int FiscalYear, int Version, Guid? PreviousId, string Status, int StateRevision, Guid AuthorUserId, DateTime SavedUtc, Guid? ApprovalId);
