namespace VirtualCompany.Application.BackgroundExecution;

public sealed record ExecutionCoordinationLockScope(
    Guid? CompanyId,
    string Category,
    string Resource,
    string? CorrelationId = null);

public sealed record ExecutionCoordinationLockLease(
    string Key,
    string OwnerToken,
    DateTimeOffset ExpiresAtUtc,
    Guid? CompanyId,
    string Category,
    string Resource,
    string? CorrelationId = null);

public sealed record ExecutionStateKey(
    Guid CompanyId,
    string ExecutionId,
    string? CorrelationId = null);

public interface IExecutionCoordinationKeyBuilder
{
    string BuildLockKey(ExecutionCoordinationLockScope scope);

    string BuildExecutionStateKey(ExecutionStateKey key);
}

public interface IExecutionCoordinationStore
{
    Task<ExecutionCoordinationLockLease?> TryAcquireLockAsync(
        ExecutionCoordinationLockScope scope,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> RenewLockAsync(
        ExecutionCoordinationLockLease lease,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken);

    Task<bool> ReleaseLockAsync(
        ExecutionCoordinationLockLease lease,
        CancellationToken cancellationToken);

    Task SetStateAsync<TState>(
        ExecutionStateKey key,
        TState state,
        TimeSpan? ttl,
        CancellationToken cancellationToken);

    Task<TState?> GetStateAsync<TState>(
        ExecutionStateKey key,
        CancellationToken cancellationToken);

    Task<bool> DeleteStateAsync(
        ExecutionStateKey key,
        CancellationToken cancellationToken);
}
