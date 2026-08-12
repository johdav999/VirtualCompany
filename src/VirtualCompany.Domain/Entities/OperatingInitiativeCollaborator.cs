using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class OperatingInitiativeCollaborator : ICompanyOwnedEntity
{
    private OperatingInitiativeCollaborator() { }
    public OperatingInitiativeCollaborator(Guid id, Guid companyId, Guid initiativeId, Guid agentId,
        OperatingCollaborationRole role, OperatingCollaborationPattern pattern, int sequence,
        string objective, string expectedArtifact)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        InitiativeId = OperatingCycle.RequiredId(initiativeId, nameof(initiativeId));
        AgentId = OperatingCycle.RequiredId(agentId, nameof(agentId));
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Role = role; Pattern = pattern; Sequence = sequence;
        Objective = OperatingCycle.Text(objective, nameof(objective), 2000);
        ExpectedArtifact = OperatingCycle.Text(expectedArtifact, nameof(expectedArtifact), 1000);
        CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InitiativeId { get; private set; }
    public Guid AgentId { get; private set; }
    public OperatingCollaborationRole Role { get; private set; }
    public OperatingCollaborationPattern Pattern { get; private set; }
    public int Sequence { get; private set; }
    public string Objective { get; private set; } = null!;
    public string ExpectedArtifact { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public OperatingInitiative Initiative { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
}
