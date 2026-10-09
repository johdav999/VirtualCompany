namespace VirtualCompany.Domain.Entities;

// Non-Finance task settings. Finance continues to use its existing grant aggregate.
public sealed class TaskTypePolicy : ICompanyOwnedEntity
{
    private TaskTypePolicy() { }
    public TaskTypePolicy(Guid companyId, Guid agentId, string taskType)
    { Id=Guid.NewGuid(); CompanyId=companyId; AgentId=agentId; TaskType=taskType; }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string TaskType { get; private set; } = "";
    public int Version { get; private set; }
    public Guid? ActiveRevisionId { get; private set; }
    public int UsageVersion { get; private set; }
    public DateTime UsageDayUtc { get; private set; }
    public int ActionsUsed { get; private set; }
    public void Activate(Guid revisionId) { ActiveRevisionId=revisionId; Version++; }
    public bool Reserve(DateTime now, int maximum)
    {
        if(UsageDayUtc!=now.Date) {UsageDayUtc=now.Date;ActionsUsed=0;}
        if(ActionsUsed>=maximum) return false;
        ActionsUsed++;UsageVersion++;return true;
    }
}

public sealed class TaskTypePolicyRevision : ICompanyOwnedEntity
{
    private TaskTypePolicyRevision() { }
    public TaskTypePolicyRevision(TaskTypePolicy policy, string mode, int maximumActionsPerDay,
        DateTime expiresUtc, Guid actorId, string rationale, string previewHash, string previewInputs, DateTime now)
    {
        Id=Guid.NewGuid();CompanyId=policy.CompanyId;PolicyId=policy.Id;Version=policy.Version+1;
        Mode=mode;MaximumActionsPerDay=maximumActionsPerDay;ExpiresUtc=expiresUtc;ActorId=actorId;
        Rationale=rationale;PreviewHash=previewHash;PreviewInputs=previewInputs;ActivatedUtc=now;
    }
    public Guid Id {get;private set;}
    public Guid CompanyId {get;private set;}
    public Guid PolicyId {get;private set;}
    public int Version {get;private set;}
    public string Mode {get;private set;}="disabled";
    public int MaximumActionsPerDay {get;private set;}
    public DateTime ActivatedUtc {get;private set;}
    public DateTime ExpiresUtc {get;private set;}
    public Guid ActorId {get;private set;}
    public string Rationale {get;private set;}="";
    public string PreviewHash {get;private set;}="";
    public string PreviewInputs {get;private set;}="";
}
