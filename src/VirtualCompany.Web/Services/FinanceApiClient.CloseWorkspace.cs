namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    private static string CloseWorkspaceBase(Guid companyId) => $"api/companies/{companyId:D}/finance/close-workspace";
    private static string AccountingCloseBase(Guid companyId) => $"api/companies/{companyId:D}/finance/accounting-close";

    public Task<AccountingCloseWorkspaceResponse?> GetAccountingCloseWorkspaceAsync(Guid companyId,
        Guid? fiscalPeriodId = null, Guid? closeInstanceId = null, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingCloseWorkspaceResponse>(companyId, CloseWorkspaceBase(companyId) + BuildQuery(
            ("fiscalPeriodId", fiscalPeriodId?.ToString("D")), ("closeInstanceId", closeInstanceId?.ToString("D"))),
            false, cancellationToken);

    public Task<CloseWorkspaceMutationResponse> CompleteAccountingCloseTaskAsync(Guid companyId,
        Guid closeInstanceId, AccountingCloseWorkspaceTaskResponse task, string? note,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var payload = new
        {
            expectedVersion = task.Version,
            reportedAmount = (decimal?)null,
            evidence = task.Evidence.Select(x => new { x.DocumentId, x.EvidenceType }).ToArray(),
            note,
            idempotencyKey = $"close-workspace-complete-{task.Id:D}-{Guid.NewGuid():N}"
        };
        return SendCompanyScopedAsync<object, CloseWorkspaceMutationResponse>(companyId, HttpMethod.Post,
            $"{AccountingCloseBase(companyId)}/instances/{closeInstanceId:D}/tasks/{task.Id:D}/complete", payload, cancellationToken);
    }

    public Task<CloseWorkspaceMutationResponse> RefreshAccountingCloseReadinessAsync(Guid companyId,
        Guid closeInstanceId, long closeVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, CloseWorkspaceMutationResponse>(companyId, HttpMethod.Post,
            $"{AccountingCloseBase(companyId)}/instances/{closeInstanceId:D}/readiness/prepare",
            new { expectedInstanceVersion = closeVersion, refresh = true,
                idempotencyKey = $"close-workspace-refresh-{closeInstanceId:D}-{Guid.NewGuid():N}" }, cancellationToken);
    }

    public Task<CloseWorkspaceMutationResponse> LockAccountingCloseAsync(Guid companyId,
        Guid closeInstanceId, AccountingCloseWorkspaceReadinessResponse readiness, string reason,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, CloseWorkspaceMutationResponse>(companyId, HttpMethod.Post,
            $"{AccountingCloseBase(companyId)}/instances/{closeInstanceId:D}/readiness/{readiness.SnapshotId:D}/lock",
            new { expectedVersion = readiness.Version, expectedEvidenceHash = readiness.EvidenceHash, reason,
                idempotencyKey = $"close-workspace-lock-{readiness.SnapshotId:D}-{Guid.NewGuid():N}" }, cancellationToken);
    }
}
public sealed class CloseWorkspaceMutationResponse { public Guid Id { get; set; } public Guid CloseInstanceId { get; set; } }
