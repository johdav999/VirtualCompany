

namespace VirtualCompany.Application.Agents;


public sealed record ExecutionControlChange(Guid CommandId, Guid? AgentId, bool Pause, int ExpectedVersion, int ExpectedCompanyVersion, string Reason);

public sealed record ExecutionControlHistory(Guid CommandId, int Version, Guid? AgentId, bool Paused, string Actor, DateTime ChangedUtc, string Reason);

public sealed record ExecutionControlPreview(ExecutionControlChange Change, ExecutionControlView Before, string PreviewHash, string Explanation);

public sealed record ExecutionControlWork(Guid Id, string Kind, Guid? AgentId, string AgentName, string Title, string State, DateTime UpdatedUtc, string Guidance);

public sealed record ExecutionControlView(Guid CompanyId, Guid? AgentId, int Version, int CompanyVersion, bool Paused, bool CompanyPaused, bool EmergencyStopped, bool CanManage, DateTime ObservedUtc, IReadOnlyList<ExecutionControlAgent> Agents, IReadOnlyList<ExecutionControlWork> Work, IReadOnlyList<ExecutionControlHistory> History);

public sealed record ExecutionControlApply(ExecutionControlChange Change, string PreviewHash);

public sealed record ExecutionControlAgent(Guid Id, string Name);
