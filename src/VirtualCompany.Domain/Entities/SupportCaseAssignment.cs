using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportCaseAssignment : ICompanyOwnedEntity
{
    private SupportCaseAssignment()
    {
    }

    public SupportCaseAssignment(Guid id, Guid companyId, Guid supportCaseId, Guid? assignedAgentId, Guid? assignedUserId, Guid assignedByUserId, DateTime assignedUtc, string? reason = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        AssignedAgentId = SupportEntityText.NormalizeOptionalId(assignedAgentId, nameof(assignedAgentId));
        AssignedUserId = SupportEntityText.NormalizeOptionalId(assignedUserId, nameof(assignedUserId));
        AssignedByUserId = assignedByUserId == Guid.Empty ? throw new ArgumentException("AssignedByUserId is required.", nameof(assignedByUserId)) : assignedByUserId;
        AssignedUtc = SupportEntityText.NormalizeUtc(assignedUtc, nameof(assignedUtc));
        Reason = SupportEntityText.NormalizeOptional(reason, nameof(reason), 1000);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public Guid? AssignedAgentId { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public Guid AssignedByUserId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime AssignedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

