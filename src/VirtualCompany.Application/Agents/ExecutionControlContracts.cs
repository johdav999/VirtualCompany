namespace VirtualCompany.Application.Agents;
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
