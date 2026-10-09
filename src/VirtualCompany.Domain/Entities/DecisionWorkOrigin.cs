namespace VirtualCompany.Domain.Entities;

// A durable, immutable handoff. Proposed constraints and collaborators convey intent, never authority.
public sealed class DecisionWorkOrigin : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid TaskId { get; init; }
    public Guid RequestId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string SourceKind { get; init; } = "";
    public Guid SourceVersionId { get; init; }
    public string ItemKey { get; init; } = "";
    public int SourceVersion { get; init; }
    public string SourceFingerprint { get; init; } = "";
    public Guid? MonthlySnapshotId { get; init; }
    public Guid? QuarterReviewId { get; init; }
    public Guid? AnnualPlanId { get; init; }
    public Guid? ScenarioId { get; init; }
    public string Objective { get; init; } = "";
    public string AcceptanceOutcome { get; init; } = "";
    public string ProposedConstraints { get; init; } = "";
    public DateTime DueUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string CommandHash { get; init; } = "";
    public string PreviewJson { get; init; } = "";
    public string PreviewChecksum { get; init; } = "";
    public Guid? ApprovalId { get; set; }
    public List<DecisionWorkCollaborator> Collaborators { get; init; } = [];
}
public sealed class DecisionWorkCollaborator : ICompanyOwnedEntity
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid OriginId { get; init; }
    public Guid UserId { get; init; }
    public string Name { get; init; } = "";
}
