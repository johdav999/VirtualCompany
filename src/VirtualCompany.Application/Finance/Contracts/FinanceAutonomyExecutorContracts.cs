namespace VirtualCompany.Application.Finance;

public sealed record FinanceAutonomyExecutorBatchResult(
    int Considered,
    int Claimed,
    int Completed,
    int AwaitingApproval,
    int Retried,
    int Reconciling,
    int Blocked,
    int DeadLettered);

public interface IFinanceAutonomyExecutor
{
    Task<FinanceAutonomyExecutorBatchResult> ProcessBatchAsync(
        DateTime utcNow,
        string workerId,
        int batchSize,
        CancellationToken cancellationToken);
}
