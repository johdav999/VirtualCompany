namespace VirtualCompany.Domain.Entities;

// Reviewed revisions are append-only. References preserve native ownership; no execution authority is created.
public sealed class QuarterlyReview : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public int FiscalYear { get; init; }
    public int Quarter { get; init; }
    public int Revision { get; init; }
    public Guid? PreviousId { get; init; }
    public Guid RequestId { get; init; }
    public Guid AuthorUserId { get; init; }
    public DateTime SavedUtc { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string Timezone { get; init; } = "";
    public string Currency { get; init; } = "";
    public long CalendarVersion { get; init; }
    public int StartMonth { get; init; }
    public int StartDay { get; init; }
    public string Notes { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string CommandHash { get; init; } = "";
    public List<QuarterlyObjective> Objectives { get; init; } = [];
    public List<QuarterlyResourceAllocation> Resources { get; init; } = [];
}
public sealed class QuarterlyObjective : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ReviewId { get; init; }
    public Guid GoalId { get; init; }
    public int GoalVersion { get; init; }
    public string Name { get; init; } = "";
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = "";
    public decimal Baseline { get; init; }
    public decimal Target { get; init; }
    public string Unit { get; init; } = "";
    public string Direction { get; init; } = "";
    public List<QuarterlyMeasureLink> Measures { get; init; } = [];
    public List<QuarterlyMilestone> Milestones { get; init; } = [];
    public List<QuarterlyInitiativeLink> Initiatives { get; init; } = [];
}
public sealed class QuarterlyMeasureLink : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ObjectiveId { get; init; }
    public Guid SnapshotId { get; init; }
    public string MeasureKey { get; init; } = "";
    public string SnapshotChecksum { get; init; } = "";
}
public sealed class QuarterlyMilestone : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ObjectiveId { get; init; }
    public string Title { get; init; } = "";
    public DateTime DueUtc { get; init; }
    public string Status { get; init; } = "";
}
public sealed class QuarterlyInitiativeLink : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ObjectiveId { get; init; }
    public Guid InitiativeId { get; init; }
}
public sealed class QuarterlyResourceAllocation : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid ReviewId { get; init; }
    public Guid GoalId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string Pool { get; init; } = "";
    public decimal AvailableHours { get; init; }
    public decimal ProposedHours { get; init; }
    public Guid SnapshotId { get; init; }
    public string SnapshotChecksum { get; init; } = "";
    public string Rationale { get; init; } = "";
}
