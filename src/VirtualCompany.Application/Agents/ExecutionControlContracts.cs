namespace VirtualCompany.Application.Agents;

public sealed record ExecutionControlChange(Guid CommandId, Guid? AgentId, bool Pause, int ExpectedVersion, int ExpectedCompanyVersion, string Reason);
public sealed record ExecutionControlApply(ExecutionControlChange Change, string PreviewHash);
public sealed record ExecutionControlHistory(Guid CommandId, int Version, Guid? AgentId, bool Paused, string Actor, DateTime ChangedUtc, string Reason);
public sealed record ExecutionControlAgent(Guid Id, string Name);
public sealed record ExecutionControlWork(Guid Id, string Kind, Guid? AgentId, string AgentName, string Title, string State, DateTime UpdatedUtc, string Guidance);
public sealed record ExecutionControlView(Guid CompanyId, Guid? AgentId, int Version, int CompanyVersion, bool Paused, bool CompanyPaused, bool EmergencyStopped, bool CanManage, DateTime ObservedUtc, IReadOnlyList<ExecutionControlAgent> Agents, IReadOnlyList<ExecutionControlWork> Work, IReadOnlyList<ExecutionControlHistory> History);
public sealed record ExecutionControlPreview(ExecutionControlChange Change, ExecutionControlView Before, string PreviewHash, string Explanation);
public interface IExecutionControlService
{
    Task<ExecutionControlView> GetAsync(Guid companyId, Guid? agentId, CancellationToken ct);
    Task<ExecutionControlPreview> PreviewAsync(Guid companyId, ExecutionControlChange change, CancellationToken ct);
    Task<ExecutionControlView> ApplyAsync(Guid companyId, ExecutionControlApply apply, CancellationToken ct);
}
public interface IAgentExecutionControlGate
{
    Task<IExecutionControlFence> FenceAsync(Guid companyId, CancellationToken ct);
    Task<bool> IsPausedAsync(Guid companyId, Guid? agentId, CancellationToken ct, Guid? taskId=null);
    Task<Guid> AdmitAsync(Guid companyId, Guid? agentId, string boundary, string businessKey, CancellationToken ct, Guid? taskId=null);
    Task AcknowledgeAsync(Guid companyId, Guid admissionId, bool confirmed, CancellationToken ct);
}
public interface IExecutionControlFence : IAsyncDisposable { Task CommitAsync(CancellationToken ct); }
public sealed class ExecutionPausedException() : InvalidOperationException("New agent steps are paused. Review execution controls before continuing.");
public sealed class ExecutionControlConflictException(string message) : InvalidOperationException(message);
public interface IDurableTaskToolExecutionService
{
    Task<ExecuteAgentToolResultDto> ExecutePersistedTaskAsync(Guid companyId,Guid agentId,ExecuteAgentToolCommand command,CancellationToken ct);
}
