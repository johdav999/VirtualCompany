using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyResponsibilityAssignment : ICompanyOwnedEntity
{
    private CompanyResponsibilityAssignment() { }

    public CompanyResponsibilityAssignment(Guid id, Guid companyId, ResponsibilityArea responsibilityArea,
        ResponsibilityAssignmentKind assignmentKind, Guid assignedMembershipId, Guid? primaryAgentId,
        AgentAutonomyLevel authorityLevel, Guid? approvalPolicyId, Guid? escalationMembershipId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (assignedMembershipId == Guid.Empty) throw new ArgumentException("AssignedMembershipId is required.", nameof(assignedMembershipId));
        _ = responsibilityArea.ToStorageValue();
        _ = assignmentKind.ToStorageValue();
        AgentAutonomyLevelValues.EnsureSupported(authorityLevel, nameof(authorityLevel));
        if (primaryAgentId == Guid.Empty) throw new ArgumentException("PrimaryAgentId cannot be empty.", nameof(primaryAgentId));
        if (approvalPolicyId == Guid.Empty) throw new ArgumentException("ApprovalPolicyId cannot be empty.", nameof(approvalPolicyId));
        if (escalationMembershipId == Guid.Empty) throw new ArgumentException("EscalationMembershipId cannot be empty.", nameof(escalationMembershipId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ResponsibilityArea = responsibilityArea;
        AssignmentKind = assignmentKind;
        AssignedMembershipId = assignedMembershipId;
        PrimaryAgentId = primaryAgentId;
        AuthorityLevel = authorityLevel;
        ApprovalPolicyId = approvalPolicyId;
        EscalationMembershipId = escalationMembershipId;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public ResponsibilityArea ResponsibilityArea { get; private set; }
    public ResponsibilityAssignmentKind AssignmentKind { get; private set; }
    public Guid AssignedMembershipId { get; private set; }
    public Guid? PrimaryAgentId { get; private set; }
    public AgentAutonomyLevel AuthorityLevel { get; private set; }
    public Guid? ApprovalPolicyId { get; private set; }
    public Guid? EscalationMembershipId { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyMembership AssignedMembership { get; private set; } = null!;
    public Agent? PrimaryAgent { get; private set; }
    public CompanyMembership? EscalationMembership { get; private set; }

    public void Update(Guid assignedMembershipId, Guid? primaryAgentId, AgentAutonomyLevel authorityLevel,
        Guid? approvalPolicyId, Guid? escalationMembershipId)
    {
        if (assignedMembershipId == Guid.Empty) throw new ArgumentException("AssignedMembershipId is required.", nameof(assignedMembershipId));
        AgentAutonomyLevelValues.EnsureSupported(authorityLevel, nameof(authorityLevel));
        AssignedMembershipId = assignedMembershipId;
        PrimaryAgentId = primaryAgentId;
        AuthorityLevel = authorityLevel;
        ApprovalPolicyId = approvalPolicyId;
        EscalationMembershipId = escalationMembershipId;
        Version++;
        UpdatedUtc = DateTime.UtcNow;
    }
}
