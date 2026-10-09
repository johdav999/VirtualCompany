using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

// Immutable receipt of the business output passed between workers. Attempts are versions,
// not additional participants or business outcomes.
public sealed class CollaborationContribution : ICompanyOwnedEntity
{
    private CollaborationContribution() { }
    public CollaborationContribution(Guid companyId, Guid parentTaskId, Guid sourceTaskId, Guid planId,
        Guid agentId, int sequence, int version, OperatingCollaborationRole role,
        OperatingCollaborationPattern pattern, string objective, string status, string output,
        string? rationale, string? reviewOutcome = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        ParentTaskId = OperatingCycle.RequiredId(parentTaskId, nameof(parentTaskId));
        SourceTaskId = OperatingCycle.RequiredId(sourceTaskId, nameof(sourceTaskId));
        PlanId = OperatingCycle.RequiredId(planId, nameof(planId));
        AgentId = OperatingCycle.RequiredId(agentId, nameof(agentId));
        if (sequence < 1 || version < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Id = Guid.NewGuid(); Sequence = sequence; Version = version; Role = role; Pattern = pattern;
        Objective = OperatingCycle.Text(objective, nameof(objective), 2000);
        Status = OperatingCycle.Text(status, nameof(status), 32); Output = output;
        Rationale = OperatingCycle.Optional(rationale, 2000);
        ReviewOutcome = OperatingCycle.Optional(reviewOutcome, 2000); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ParentTaskId { get; private set; }
    public Guid SourceTaskId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid AgentId { get; private set; }
    public int Sequence { get; private set; }
    public int Version { get; private set; }
    public OperatingCollaborationRole Role { get; private set; }
    public OperatingCollaborationPattern Pattern { get; private set; }
    public string Objective { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string Output { get; private set; } = "";
    public string? Rationale { get; private set; }
    public string? ReviewOutcome { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

public sealed class CollaborationArtifactHandoff : ICompanyOwnedEntity
{
    private CollaborationArtifactHandoff() { }
    public CollaborationArtifactHandoff(Guid companyId, Guid inputContributionId, Guid receivingContributionId,
        bool passed, string? reason = null)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        InputContributionId = OperatingCycle.RequiredId(inputContributionId, nameof(inputContributionId));
        ReceivingContributionId = OperatingCycle.RequiredId(receivingContributionId, nameof(receivingContributionId));
        Id = Guid.NewGuid(); Passed = passed; Reason = OperatingCycle.Optional(reason, 2000); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InputContributionId { get; private set; }
    public Guid ReceivingContributionId { get; private set; }
    public bool Passed { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}
