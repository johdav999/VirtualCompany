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

public sealed class AccountingCloseWorkspaceResponse
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public AccountingCloseWorkspacePeriodResponse? SelectedPeriod { get; set; }
    public Guid? CloseInstanceId { get; set; }
    public string? CloseName { get; set; }
    public string? CloseStatus { get; set; }
    public long? CloseVersion { get; set; }
    public List<AccountingCloseWorkspacePeriodResponse> Periods { get; set; } = [];
    public AccountingCloseWorkspaceReadinessResponse? Readiness { get; set; }
    public List<AccountingCloseWorkspaceTaskResponse> Tasks { get; set; } = [];
    public List<AccountingCloseWorkspacePanelResponse> Panels { get; set; } = [];
    public List<AccountingCloseWorkspaceSignOffResponse> SignOffs { get; set; } = [];
    public List<AccountingCloseWorkspaceNotificationResponse> Notifications { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
    public string EvidenceNotice { get; set; } = string.Empty;
}

public sealed class AccountingCloseWorkspacePeriodResponse { public Guid FiscalPeriodId { get; set; } public string Name { get; set; } = string.Empty; public DateTime StartUtc { get; set; } public DateTime EndUtc { get; set; } public bool IsClosed { get; set; } public Guid? CloseInstanceId { get; set; } public string? CloseStatus { get; set; } public DateTime? UpdatedUtc { get; set; } }
public sealed class AccountingCloseWorkspaceEvidenceResponse { public Guid Id { get; set; } public Guid DocumentId { get; set; } public string EvidenceType { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string? ContentHash { get; set; } public DateTime LinkedUtc { get; set; } public string DrilldownUrl { get; set; } = string.Empty; }
public sealed class AccountingCloseWorkspaceBlockerResponse { public string Code { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty; public string SafeNextAction { get; set; } = string.Empty; public Guid? OwnerUserId { get; set; } public string Status { get; set; } = string.Empty; public int EvidenceCount { get; set; } public DateTime ObservedUtc { get; set; } public string DrilldownUrl { get; set; } = string.Empty; public bool IsWaivable { get; set; } public string? EvidenceHash { get; set; } }
public sealed class AccountingCloseWorkspaceTaskResponse { public Guid Id { get; set; } public string Key { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public Guid? OwnerUserId { get; set; } public string? OwnerRole { get; set; } public DateTime DueUtc { get; set; } public int Sequence { get; set; } public long Version { get; set; } public List<Guid> PredecessorTaskIds { get; set; } = []; public List<string> BlockingReasonCodes { get; set; } = []; public List<AccountingCloseWorkspaceEvidenceResponse> Evidence { get; set; } = []; public List<AccountingCloseWorkspaceBlockerResponse> Blockers { get; set; } = []; public List<string> AllowedActions { get; set; } = []; public string DrilldownUrl { get; set; } = string.Empty; }
public sealed class AccountingCloseWorkspaceReadinessResponse { public Guid SnapshotId { get; set; } public int SnapshotNumber { get; set; } public string Status { get; set; } = string.Empty; public bool IsReady { get; set; } public string EvidenceHash { get; set; } = string.Empty; public DateTime PreparedUtc { get; set; } public long Version { get; set; } public int BlockingCount { get; set; } public int WarningCount { get; set; } public bool IsStale { get; set; } public List<AccountingCloseWorkspaceBlockerResponse> Blockers { get; set; } = []; }
public sealed class AccountingCloseWorkspacePanelResponse { public string Key { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public int TotalCount { get; set; } public int AttentionCount { get; set; } public DateTime? EvidenceUtc { get; set; } public string DrilldownUrl { get; set; } = string.Empty; public List<string> AllowedActions { get; set; } = []; }
public sealed class AccountingCloseWorkspaceSignOffResponse { public Guid Id { get; set; } public string Action { get; set; } = string.Empty; public string ActorRole { get; set; } = string.Empty; public Guid ActorUserId { get; set; } public DateTime OccurredUtc { get; set; } public string? Reason { get; set; } public string EvidenceHash { get; set; } = string.Empty; }
public sealed class AccountingCloseWorkspaceNotificationResponse { public Guid Id { get; set; } public string Priority { get; set; } = string.Empty; public string Title { get; set; } = string.Empty; public string Body { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } public string? ActionUrl { get; set; } }
public sealed class CloseWorkspaceMutationResponse { public Guid Id { get; set; } public Guid CloseInstanceId { get; set; } }
