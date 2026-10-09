using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Application.Agents;


public sealed record TaskTypePolicyCatalogueEntry(string Code,string Name,string Department,string CapabilityId,string ToolName,string ActionType,string RecordKey,bool Finance=false);

public sealed record TaskPolicyWorkChoice(Guid Id,string Name);

public sealed record TaskPolicyQueueContext(IReadOnlyList<TaskPolicyWorkChoice> Records,IReadOnlyList<TaskPolicyWorkChoice> Goals);

public sealed record TaskPolicyChange(Guid AgentId,string TaskType,string Mode,int MaximumActionsPerDay,DateTime ExpiresUtc,string Rationale,int ExpectedVersion=0,Guid? SimulationRecordId=null);

public sealed record TaskPolicyPreview(TaskPolicyChange Change,TaskTypePolicyCatalogueEntry Type,TaskPolicyView Before,int AfterVersion,
    AuthorityCheckDto Check,bool CanApply,string PreviewHash,string AuthorityHash,int CompanyVersion,string SimulationExplanation,FinanceAutonomyGrantDefinition? FinanceDefinition=null,IReadOnlyList<AuthorityLimitDto>? Limits=null);

public sealed record TaskPolicyView(Guid CompanyId,Guid AgentId,string TaskType,int Version,IReadOnlyList<TaskPolicyHistory> History,FinanceAutonomyGrantDto? FinanceGrant=null);

public sealed record TaskPolicyApply(TaskPolicyChange Change,string PreviewHash);

public sealed record TaskPolicyHistory(int Version,string Mode,int MaximumActionsPerDay,DateTime ActivatedUtc,DateTime ExpiresUtc,Guid ActorId,string Rationale,string PreviewHash,string ActorName="Authorized policy manager");

public sealed record QueueTaskPolicyWork(Guid AgentId,string TaskType,Guid RecordId,int ExpectedVersion,Guid? GoalId=null);
