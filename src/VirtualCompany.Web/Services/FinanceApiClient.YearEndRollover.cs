namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    private static string YearEndBase(Guid companyId) => $"api/companies/{companyId:D}/finance/year-end-runs";

    public async Task<IReadOnlyList<YearEndRunSummaryResponse>> GetYearEndRunsAsync(Guid companyId,
        CancellationToken cancellationToken = default) => await GetAsync<List<YearEndRunSummaryResponse>>(
            companyId, YearEndBase(companyId), false, cancellationToken) ?? [];

    public Task<YearEndRunResponse?> GetYearEndRunAsync(Guid companyId, Guid runId,
        CancellationToken cancellationToken = default) => GetAsync<YearEndRunResponse>(companyId,
            $"{YearEndBase(companyId)}/{runId:D}", true, cancellationToken);

    public Task<YearEndRunResponse> PrepareYearEndRunAsync(Guid companyId, PrepareYearEndRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PrepareYearEndRunApiRequest, YearEndRunResponse>(companyId,
            HttpMethod.Post, YearEndBase(companyId), request, cancellationToken);
    }

    public Task<YearEndRunResponse> RefreshYearEndReadinessAsync(Guid companyId, Guid runId, long version,
        string idempotencyKey, CancellationToken cancellationToken = default) => SendYearEndAsync(companyId, runId,
            "readiness/refresh", new { expectedVersion = version, idempotencyKey }, cancellationToken);

    public Task<YearEndRunResponse> SubmitYearEndRunAsync(Guid companyId, Guid runId, long version,
        string evidenceHash, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendYearEndEvidenceAsync(companyId, runId, "submit", version, evidenceHash, idempotencyKey, cancellationToken);

    public Task<YearEndRunResponse> ReviewYearEndRunAsync(Guid companyId, Guid runId, long version,
        string evidenceHash, bool approve, string? reason, string idempotencyKey,
        CancellationToken cancellationToken = default) => SendYearEndAsync(companyId, runId, "review",
        new { expectedVersion = version, expectedEvidenceHash = evidenceHash, approve, reason, idempotencyKey }, cancellationToken);

    public Task<YearEndRunResponse> ExecuteYearEndRunAsync(Guid companyId, Guid runId, long version,
        string evidenceHash, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendYearEndEvidenceAsync(companyId, runId, "execute", version, evidenceHash, idempotencyKey, cancellationToken);

    public Task<YearEndRunResponse> ReconcileYearEndRunAsync(Guid companyId, Guid runId, long version,
        string evidenceHash, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendYearEndEvidenceAsync(companyId, runId, "reconcile", version, evidenceHash, idempotencyKey, cancellationToken);

    public Task<YearEndRunResponse> FinalizeYearEndRunAsync(Guid companyId, Guid runId, long version,
        string idempotencyKey, CancellationToken cancellationToken = default) => SendYearEndAsync(companyId, runId,
        "finalize", new { expectedVersion = version, idempotencyKey }, cancellationToken);

    public Task<YearEndRunResponse> RecordYearEndSubsequentEventAsync(Guid companyId, Guid runId,
        RecordYearEndSubsequentEventApiRequest request, CancellationToken cancellationToken = default) =>
        SendYearEndAsync(companyId, runId, "subsequent-events", request, cancellationToken);

    public Task<YearEndRunResponse> SubmitYearEndSubsequentEventAsync(Guid companyId, Guid runId, Guid eventId,
        long version, string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendYearEndAsync(companyId, runId, $"subsequent-events/{eventId:D}/submit",
            new { expectedVersion = version, idempotencyKey }, cancellationToken);

    public Task<YearEndRunResponse> ReviewYearEndSubsequentEventAsync(Guid companyId, Guid runId, Guid eventId,
        long version, bool approve, string? reason, string idempotencyKey,
        CancellationToken cancellationToken = default) => SendYearEndAsync(companyId, runId,
        $"subsequent-events/{eventId:D}/review", new { expectedVersion = version, approve, reason, idempotencyKey }, cancellationToken);

    public Task<YearEndRunResponse> LinkYearEndCorrectionAsync(Guid companyId, Guid runId, Guid eventId,
        long version, Guid? correctionLedgerEntryId, Guid? reopenRequestId, string reason,
        string idempotencyKey, CancellationToken cancellationToken = default) => SendYearEndAsync(companyId, runId,
        $"subsequent-events/{eventId:D}/correction", new { expectedVersion = version, correctionLedgerEntryId,
            reopenRequestId, reason, idempotencyKey }, cancellationToken);

    private Task<YearEndRunResponse> SendYearEndEvidenceAsync(Guid companyId, Guid runId, string action,
        long version, string evidenceHash, string idempotencyKey, CancellationToken cancellationToken) =>
        SendYearEndAsync(companyId, runId, action, new { expectedVersion = version,
            expectedEvidenceHash = evidenceHash, idempotencyKey }, cancellationToken);

    private Task<YearEndRunResponse> SendYearEndAsync<T>(Guid companyId, Guid runId, string action,
        T request, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<T, YearEndRunResponse>(companyId, HttpMethod.Post,
            $"{YearEndBase(companyId)}/{runId:D}/{action}", request, cancellationToken);
    }
}

public sealed class PrepareYearEndRunApiRequest
{
    public DateOnly FiscalYearStart { get; set; }
    public Guid TargetFiscalPeriodId { get; set; }
    public Guid RetainedEarningsAccountId { get; set; }
    public Guid OpeningBalanceClearingAccountId { get; set; }
    public string VoucherSeriesCode { get; set; } = "YE";
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class RecordYearEndSubsequentEventApiRequest
{
    public DateOnly EventDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? EstimatedAmount { get; set; }
    public string Currency { get; set; } = "SEK";
    public string Decision { get; set; } = "disclose";
    public Guid OwnerUserId { get; set; }
    public Guid? EvidenceDocumentId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class YearEndRunSummaryResponse
{
    public Guid Id { get; set; }
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public string Status { get; set; } = string.Empty;
    public int BlockerCount { get; set; }
    public decimal NetIncome { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; }
}

public sealed class YearEndRunResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public Guid TargetFiscalPeriodId { get; set; }
    public string TargetFiscalPeriodName { get; set; } = string.Empty;
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid PreparedByUserId { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? ApprovedEvidenceHash { get; set; }
    public Guid? RetainedEarningsLedgerEntryId { get; set; }
    public Guid? OpeningBalanceLedgerEntryId { get; set; }
    public string? OpeningBalanceChecksum { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; }
    public YearEndReadinessSnapshotResponse? CurrentReadiness { get; set; }
    public YearEndRetainedEarningsProposalResponse? RetainedEarningsProposal { get; set; }
    public List<YearEndOpeningBalanceCandidateResponse> OpeningBalances { get; set; } = [];
    public List<YearEndSignOffResponse> SignOffs { get; set; } = [];
    public List<YearEndSubsequentEventResponse> SubsequentEvents { get; set; } = [];
    public List<YearEndHistoryResponse> History { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
}

public sealed class YearEndReadinessSnapshotResponse
{
    public Guid Id { get; set; }
    public int SnapshotNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string JournalCutoffHash { get; set; } = string.Empty;
    public int BlockerCount { get; set; }
    public int ClosedPeriodCount { get; set; }
    public DateTime PreparedUtc { get; set; }
    public long Version { get; set; }
    public List<YearEndReadinessCheckResponse> Checks { get; set; } = [];
}

public sealed class YearEndReadinessCheckResponse
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public bool Blocking { get; set; }
    public int Count { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class YearEndRetainedEarningsProposalResponse
{
    public Guid Id { get; set; }
    public string RetainedEarningsAccountCode { get; set; } = string.Empty;
    public string OpeningBalanceClearingAccountCode { get; set; } = string.Empty;
    public decimal NetIncome { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class YearEndOpeningBalanceCandidateResponse
{
    public Guid Id { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
    public string SourceCurrency { get; set; } = string.Empty;
    public string DimensionKey { get; set; } = string.Empty;
    public decimal ClosingFunctionalBalance { get; set; }
    public decimal OpeningFunctionalBalance { get; set; }
    public decimal Difference { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class YearEndSignOffResponse
{
    public string Action { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string ActorRole { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime OccurredUtc { get; set; }
}

public sealed class YearEndSubsequentEventResponse
{
    public Guid Id { get; set; }
    public DateOnly EventDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal? EstimatedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Version { get; set; }
}

public sealed class YearEndHistoryResponse
{
    public string Action { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public Guid ActorUserId { get; set; }
    public string EvidenceHash { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; }
}
