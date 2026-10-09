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
