namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingScheduleListResponse?> ListAccountingSchedulesAsync(Guid companyId, string? status = null,
        CancellationToken cancellationToken = default)
    {
        var suffix = string.IsNullOrWhiteSpace(status) ? string.Empty : $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<AccountingScheduleListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/schedules{suffix}", false, cancellationToken);
    }

    public Task<AccountingScheduleResponse?> GetAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        CancellationToken cancellationToken = default) => GetAsync<AccountingScheduleResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}", true, cancellationToken);

    public Task<AccountingScheduleResponse> CreateAccountingScheduleAsync(Guid companyId,
        SaveAccountingScheduleApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveAccountingScheduleApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules", request, cancellationToken);
    }

    public Task<AccountingScheduleResponse> UpdateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        SaveAccountingScheduleApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveAccountingScheduleApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}", request,
            cancellationToken);
    }

    public Task<AccountingSchedulePreviewResponse> PreviewAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingSchedulePreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/preview",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<AccountingScheduleResponse> SubmitAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleActionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/submit",
            new() { ExpectedVersion = expectedVersion, IdempotencyKey = idempotencyKey }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> DecideAccountingScheduleApprovalAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, bool approve, string? comment, Guid clientRequestId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<DecideAccountingScheduleApprovalApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/approval",
            new() { ExpectedVersion = expectedVersion, Approve = approve, Comment = comment, ClientRequestId = clientRequestId }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> ActivateAccountingScheduleAsync(Guid companyId, Guid scheduleId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendScheduleVersionActionAsync(companyId, scheduleId, "activate", expectedVersion, cancellationToken);

    public Task<AccountingScheduleResponse> ChangeAccountingScheduleStateAsync(Guid companyId, Guid scheduleId,
        string action, long expectedVersion, bool generateMissed = false, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ChangeAccountingScheduleStateApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/{action}",
            new() { ExpectedVersion = expectedVersion, GenerateMissed = generateMissed }, cancellationToken);
    }

    public Task<AccountingScheduleResponse> RegenerateAccountingScheduleOccurrenceAsync(Guid companyId,
        Guid scheduleId, Guid occurrenceId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/occurrences/{occurrenceId:D}/regenerate",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);
    }

    private Task<AccountingScheduleResponse> SendScheduleVersionActionAsync(Guid companyId, Guid scheduleId,
        string action, long expectedVersion, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AccountingScheduleVersionApiRequest, AccountingScheduleResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/schedules/{scheduleId:D}/{action}",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);
    }
}

public sealed class SaveAccountingScheduleApiRequest
{
    public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "recurring_fixed"; public string Cadence { get; set; } = "monthly";
    public string AmountBasis { get; set; } = "per_occurrence"; public string ProrationRule { get; set; } = "none";
    public DateOnly StartDate { get; set; } public DateOnly? EndDate { get; set; } public int OccurrenceDay { get; set; } = 1;
    public string TimeZoneId { get; set; } = "Europe/Stockholm"; public string VoucherSeriesCode { get; set; } = "A";
    public string Currency { get; set; } = "SEK"; public string ReversalRule { get; set; } = "none";
    public string Description { get; set; } = string.Empty; public List<AccountingScheduleLineApiRequest> Lines { get; set; } = [];
    public List<Guid> EvidenceDocumentIds { get; set; } = []; public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
public sealed class AccountingScheduleLineApiRequest { public Guid FinanceAccountId { get; set; } public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Description { get; set; } = string.Empty; public List<Guid> DimensionMemberIds { get; set; } = []; }
public class AccountingScheduleVersionApiRequest { public long ExpectedVersion { get; set; } }
public sealed class AccountingScheduleActionApiRequest : AccountingScheduleVersionApiRequest { public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class DecideAccountingScheduleApprovalApiRequest : AccountingScheduleVersionApiRequest { public bool Approve { get; set; } public string? Comment { get; set; } public Guid ClientRequestId { get; set; } }
public sealed class ChangeAccountingScheduleStateApiRequest : AccountingScheduleVersionApiRequest { public bool GenerateMissed { get; set; } }
public sealed class AccountingScheduleListResponse { public List<AccountingScheduleResponse> Items { get; set; } = []; public int TotalCount { get; set; } public int Skip { get; set; } public int Take { get; set; } public decimal ReleasedAmount { get; set; } public decimal ReversedAmount { get; set; } public decimal RemainingAmount { get; set; } public int ActiveCount { get; set; } public int DueCount { get; set; } public int ExceptionCount { get; set; } public string Currency { get; set; } = string.Empty; }
public sealed class AccountingScheduleResponse
{
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty; public string ScheduleType { get; set; } = string.Empty;
    public string Cadence { get; set; } = string.Empty; public string AmountBasis { get; set; } = string.Empty;
    public string ProrationRule { get; set; } = string.Empty; public DateOnly StartDate { get; set; } public DateOnly? EndDate { get; set; }
    public int OccurrenceDay { get; set; } public string TimeZoneId { get; set; } = string.Empty;
    public string VoucherSeriesCode { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty;
    public string ReversalRule { get; set; } = string.Empty; public string Status { get; set; } = string.Empty;
    public DateOnly NextOccurrenceDate { get; set; } public int CurrentVersionNumber { get; set; } public string? CurrentVersionHash { get; set; }
    public long Version { get; set; } public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; }
    public AccountingScheduleVersionResponse? CurrentVersion { get; set; } public AccountingScheduleApprovalResponse? Approval { get; set; }
    public List<AccountingScheduleOccurrenceResponse> Occurrences { get; set; } = []; public AccountingScheduleReconciliationResponse Reconciliation { get; set; } = new();
    public List<string> AllowedActions { get; set; } = [];
}
public sealed class AccountingScheduleVersionResponse { public Guid Id { get; set; } public int VersionNumber { get; set; } public string PayloadHash { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public DateOnly EffectiveFrom { get; set; } public DateTime CreatedUtc { get; set; } public List<AccountingScheduleLineResponse> Lines { get; set; } = []; public List<AccountingScheduleEvidenceResponse> Evidence { get; set; } = []; }
public sealed class AccountingScheduleLineResponse { public Guid Id { get; set; } public int Sequence { get; set; } public Guid FinanceAccountId { get; set; } public string AccountCode { get; set; } = string.Empty; public string AccountName { get; set; } = string.Empty; public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Description { get; set; } = string.Empty; public List<Guid> DimensionMemberIds { get; set; } = []; }
public sealed class AccountingScheduleEvidenceResponse { public Guid DocumentId { get; set; } public string Title { get; set; } = string.Empty; public string ContentHash { get; set; } = string.Empty; public string OriginalFileName { get; set; } = string.Empty; }
public sealed class AccountingScheduleApprovalResponse { public Guid ApprovalRequestId { get; set; } public string Status { get; set; } = string.Empty; public int VersionNumber { get; set; } public string PayloadHash { get; set; } = string.Empty; public DateTime BoundUtc { get; set; } public string? DecisionSummary { get; set; } }
public sealed class AccountingScheduleOccurrenceResponse { public Guid Id { get; set; } public DateOnly OccurrenceDate { get; set; } public DateOnly PostingDate { get; set; } public decimal ScheduledAmount { get; set; } public decimal ReleasedAmount { get; set; } public decimal ReversedAmount { get; set; } public string Currency { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public Guid? LedgerEntryId { get; set; } public Guid? ReversalLedgerEntryId { get; set; } public DateOnly? ReversalDueDate { get; set; } public int AttemptCount { get; set; } public string? FailureCode { get; set; } public string? FailureSummary { get; set; } public long Version { get; set; } public DateTime UpdatedUtc { get; set; } public List<AccountingScheduleExceptionResponse> Exceptions { get; set; } = []; }
public sealed class AccountingScheduleExceptionResponse { public Guid Id { get; set; } public string ReasonCode { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty; public string SafeNextAction { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } public DateTime? ResolvedUtc { get; set; } }
public sealed class AccountingScheduleReconciliationResponse { public decimal OriginalAmount { get; set; } public decimal ReleasedAmount { get; set; } public decimal ReversedAmount { get; set; } public decimal? RemainingAmount { get; set; } public decimal ExceptionAmount { get; set; } public string Currency { get; set; } = string.Empty; public int PlannedOccurrences { get; set; } public int PostedOccurrences { get; set; } public int ReversedOccurrences { get; set; } public int ExceptionOccurrences { get; set; } public bool IsReconciled { get; set; } }
public sealed class AccountingSchedulePreviewResponse { public AccountingScheduleResponse Schedule { get; set; } = new(); public AccountingPostingPreviewResponse PostingPreview { get; set; } = new(); public decimal OccurrenceAmount { get; set; } public DateOnly PostingDate { get; set; } public int PlannedOccurrences { get; set; } public List<AccountingPostingIssueResponse> Issues { get; set; } = []; }
