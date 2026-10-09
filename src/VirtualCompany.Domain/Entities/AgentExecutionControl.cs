namespace VirtualCompany.Domain.Entities;

// Guid.Empty identifies the company scope; other scope IDs identify an agent in that company.
public sealed class AgentExecutionControl : ICompanyOwnedEntity
{
    private AgentExecutionControl() { }
    public AgentExecutionControl(Guid companyId, Guid scopeId) { Id = Guid.NewGuid(); CompanyId = companyId; ScopeId = scopeId; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ScopeId { get; private set; }
    public bool Paused { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void Change(bool paused, DateTime now) { Paused = paused; Version++; UpdatedUtc = now; }
}

public sealed class AgentExecutionControlCommand : ICompanyOwnedEntity
{
    private AgentExecutionControlCommand() { }
    public AgentExecutionControlCommand(Guid companyId, Guid commandId, Guid scopeId, int version, bool paused, Guid actorId, string reason, string requestHash, DateTime now)
    { Id = commandId; CompanyId = companyId; ScopeId = scopeId; Version = version; Paused = paused; ActorId = actorId; Reason = reason; RequestHash = requestHash; ChangedUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ScopeId { get; private set; }
    public int Version { get; private set; }
    public bool Paused { get; private set; }
    public Guid ActorId { get; private set; }
    public string Reason { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
    public DateTime ChangedUtc { get; private set; }
}

// Acknowledgement describes this boundary only. It does not assert provider confirmation or a business outcome.
public sealed class AgentExecutionAdmission : ICompanyOwnedEntity
{
    private AgentExecutionAdmission() { }
    public AgentExecutionAdmission(Guid companyId, Guid? agentId, string boundary, string key, DateTime now)
    { Id = Guid.NewGuid(); CompanyId = companyId; AgentId = agentId; Boundary = boundary; BusinessKey = key; AdmittedUtc = now; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string Boundary { get; private set; } = "";
    public string BusinessKey { get; private set; } = "";
    public DateTime AdmittedUtc { get; private set; }
    public DateTime? AcknowledgedUtc { get; private set; }
    public bool? Confirmed { get; private set; }
    public int Version { get; private set; } = 1;
    public void Acknowledge(bool confirmed, DateTime now) { if (AcknowledgedUtc.HasValue) return; Confirmed = confirmed; AcknowledgedUtc = now; Version++; }
}
