namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    private static string SwitchRoute(Guid companyId, Guid switchId) =>
        $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}";

    public Task<IReadOnlyList<AccountingProviderSwitchResponse>> GetAccountingProviderSwitchesAsync(
        Guid companyId, int limit = 50, CancellationToken cancellationToken = default) =>
        GetListAsync<AccountingProviderSwitchResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/provider-switches?limit={Math.Clamp(limit, 1, 100)}",
            cancellationToken);

    public Task<AccountingProviderSwitchResponse?> GetAccountingProviderSwitchAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchResponse>(companyId, SwitchRoute(companyId, switchId),
            allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchResponse> CreateAccountingProviderSwitchAsync(
        Guid companyId, CreateAccountingProviderSwitchApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateAccountingProviderSwitchApiRequest, AccountingProviderSwitchResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/provider-switches", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchResponse> CancelAccountingProviderSwitchAsync(
        Guid companyId, Guid switchId, CancelAccountingProviderSwitchApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CancelAccountingProviderSwitchApiRequest, AccountingProviderSwitchResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/cancel", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchAllowedActionsResponse?> GetAccountingProviderSwitchAllowedActionsAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchAllowedActionsResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/allowed-actions", allowNotFound: true, cancellationToken);

    public Task<AccountingMigrationGuidanceResponse?> GetAccountingMigrationGuidanceAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingMigrationGuidanceResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/guidance", allowNotFound: true, cancellationToken);

    public Task<AccountingMigrationRecommendationResponse?> GetAccountingMigrationRecommendationAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingMigrationRecommendationResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/guidance/recommendation", allowNotFound: true, cancellationToken);

    public Task<AccountingMigrationEvidenceResponse?> GetAccountingMigrationEvidenceAsync(
        Guid companyId, Guid switchId, string view, int limit = 20,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingMigrationEvidenceResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/evidence/{Uri.EscapeDataString(view)}?limit={Math.Clamp(limit, 1, 50)}",
            allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchAssessmentResponse?> GetLatestAccountingProviderSwitchAssessmentAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchAssessmentResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/assessments/latest", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchAssessmentResponse> StartAccountingProviderSwitchAssessmentAsync(
        Guid companyId, Guid switchId, StartAccountingProviderSwitchRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchRunApiRequest, AccountingProviderSwitchAssessmentResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/assessments", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchAssessmentResponse> ReplayAccountingProviderSwitchAssessmentAsync(
        Guid companyId, Guid switchId, Guid assessmentId, StartAccountingProviderSwitchRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchRunApiRequest, AccountingProviderSwitchAssessmentResponse>(
            companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/assessments/{assessmentId}/replay", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchCompletenessResponse?> GetAccountingProviderSwitchCompletenessAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchCompletenessResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/staging/completeness", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<AccountingProviderSwitchMappingResponse>> GetAccountingProviderSwitchMappingsAsync(
        Guid companyId, Guid switchId, int limit = 200, CancellationToken cancellationToken = default) =>
        GetListAsync<AccountingProviderSwitchMappingResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/mappings?limit={Math.Clamp(limit, 1, 500)}", cancellationToken);

    public Task<AccountingProviderSwitchMappingResponse> RequestAccountingProviderSwitchMappingApprovalAsync(
        Guid companyId, Guid switchId, Guid mappingId, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AccountingProviderSwitchMappingResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/mappings/{mappingId}/approval",
            new { ExpectedVersion = expectedVersion }, cancellationToken);
    }

    public Task<AccountingProviderSwitchRehearsalResponse?> GetLatestAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchRehearsalResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/rehearsals/latest", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchRehearsalResponse> StartAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, StartAccountingProviderSwitchRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchRunApiRequest, AccountingProviderSwitchRehearsalResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/rehearsals", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchRehearsalResponse> ReplayAccountingProviderSwitchRehearsalAsync(
        Guid companyId, Guid switchId, Guid rehearsalId, StartAccountingProviderSwitchRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchRunApiRequest, AccountingProviderSwitchRehearsalResponse>(
            companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/rehearsals/{rehearsalId}/replay", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchPlanReadinessResponse?> GetAccountingProviderSwitchPlanReadinessAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchPlanReadinessResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/cutover-plans/readiness", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchCutoverPlanResponse> GenerateAccountingProviderSwitchCutoverPlanAsync(
        Guid companyId, Guid switchId, GenerateAccountingProviderSwitchCutoverPlanApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<GenerateAccountingProviderSwitchCutoverPlanApiRequest, AccountingProviderSwitchCutoverPlanResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/cutover-plans", request,
            cancellationToken);
    }

    public Task<AccountingProviderSwitchCutoverPlanResponse> RequestAccountingProviderSwitchPlanApprovalAsync(
        Guid companyId, Guid switchId, Guid planId, long expectedSwitchVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AccountingProviderSwitchCutoverPlanResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/cutover-plans/{planId}/approval",
            new { ExpectedSwitchVersion = expectedSwitchVersion }, cancellationToken);
    }

    public Task<AccountingProviderSwitchInternalReadinessResponse?> GetAccountingProviderSwitchInternalReadinessAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchInternalReadinessResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/preparation/readiness", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchPreparationResponse?> GetLatestAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchPreparationResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/preparations/latest", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchPreparationResponse> StartAccountingProviderSwitchPreparationAsync(
        Guid companyId, Guid switchId, StartAccountingProviderSwitchPlanRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchPlanRunApiRequest, AccountingProviderSwitchPreparationResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/preparations", request,
            cancellationToken);
    }

    public Task<AccountingProviderSwitchTargetTransferResponse?> GetLatestAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchTargetTransferResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/target-transfer-batches/latest", allowNotFound: true,
            cancellationToken);

    public Task<AccountingProviderSwitchTargetTransferResponse> StartAccountingProviderSwitchTargetTransferAsync(
        Guid companyId, Guid switchId, StartAccountingProviderSwitchPlanRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchPlanRunApiRequest, AccountingProviderSwitchTargetTransferResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/target-transfer-batches", request,
            cancellationToken);
    }

    public Task<AccountingProviderSwitchTargetTransferItemResponse> ReconcileAccountingProviderSwitchTargetTransferItemAsync(
        Guid companyId, Guid switchId, Guid batchId, Guid itemId,
        ReconcileAccountingProviderSwitchTransferItemApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileAccountingProviderSwitchTransferItemApiRequest,
            AccountingProviderSwitchTargetTransferItemResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/target-transfer-batches/{batchId}/items/{itemId}/reconcile",
            request, cancellationToken);
    }

    public Task<AccountingProviderSwitchCutoverResponse?> GetLatestAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchCutoverResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/cutovers/latest", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchCutoverResponse> ScheduleAccountingProviderSwitchCutoverAsync(
        Guid companyId, Guid switchId, StartAccountingProviderSwitchPlanRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingProviderSwitchPlanRunApiRequest, AccountingProviderSwitchCutoverResponse>(
            companyId, HttpMethod.Post, $"{SwitchRoute(companyId, switchId)}/cutovers", request, cancellationToken);
    }

    public Task<AccountingProviderSwitchCutoverResponse> RunAccountingProviderSwitchCutoverActionAsync(
        Guid companyId, Guid switchId, Guid executionId, string action, long expectedVersion,
        string? reason = null, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        object payload = action is "cancel" or "recover"
            ? new { Reason = reason ?? "Reviewed recovery action requested from the migration workspace.", ExpectedVersion = expectedVersion }
            : new { ExpectedVersion = expectedVersion };
        return SendCompanyScopedAsync<object, AccountingProviderSwitchCutoverResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/cutovers/{executionId}/{Uri.EscapeDataString(action)}", payload,
            cancellationToken);
    }

    public Task<AccountingProviderSwitchMonitoringResponse?> GetAccountingProviderSwitchMonitoringAsync(
        Guid companyId, Guid switchId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchMonitoringResponse>(companyId,
            $"{SwitchRoute(companyId, switchId)}/monitoring", allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchOperationsResponse?> GetAccountingProviderSwitchOperationsAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingProviderSwitchOperationsResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/provider-switches/operations",
            allowNotFound: true, cancellationToken);

    public Task<AccountingProviderSwitchMonitoringResponse> RunAccountingProviderSwitchMonitoringActionAsync(
        Guid companyId, Guid switchId, string action, long expectedVersion, string? summary = null,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        object payload = action == "close" ? new { ExpectedVersion = expectedVersion,
            Summary = summary ?? "Post-activation checks and retained evidence were reviewed." }
            : new { ExpectedVersion = expectedVersion };
        return SendCompanyScopedAsync<object, AccountingProviderSwitchMonitoringResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/monitoring/{Uri.EscapeDataString(action)}", payload, cancellationToken);
    }

    public Task<AccountingProviderSwitchMonitoringResponse> AcceptAccountingProviderSwitchMonitoringExceptionAsync(
        Guid companyId, Guid switchId, Guid incidentId, AcceptAccountingProviderSwitchMonitoringExceptionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AcceptAccountingProviderSwitchMonitoringExceptionApiRequest,
            AccountingProviderSwitchMonitoringResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/monitoring/incidents/{incidentId}/accept-exception", request,
            cancellationToken);
    }

    public Task<AccountingProviderSwitchMonitoringResponse> CreateCorrectiveAccountingProviderSwitchAsync(
        Guid companyId, Guid switchId, CreateCorrectiveAccountingProviderSwitchApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateCorrectiveAccountingProviderSwitchApiRequest,
            AccountingProviderSwitchMonitoringResponse>(companyId, HttpMethod.Post,
            $"{SwitchRoute(companyId, switchId)}/monitoring/corrective-cutover", request, cancellationToken);
    }
}

public sealed class AccountingProviderSwitchMonitoringResponse
{
    public Guid Id { get; set; }
    public Guid SwitchId { get; set; }
    public int WindowDays { get; set; }
    public string Status { get; set; } = string.Empty;
    public int CheckSequence { get; set; }
    public int AttemptCount { get; set; }
    public int ConsecutiveFailureCount { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime WindowEndsUtc { get; set; }
    public DateTime? LastSuccessfulCheckUtc { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public string? FailureSummary { get; set; }
    public Guid? ClosureApprovalRequestId { get; set; }
    public Guid? CorrectiveSwitchId { get; set; }
    public long Version { get; set; }
    public List<AccountingProviderSwitchMonitoringCheckResponse> Checks { get; set; } = [];
    public List<AccountingProviderSwitchMonitoringIncidentResponse> Incidents { get; set; } = [];
    public AccountingProviderSwitchMonitoringAllowedActionsResponse AllowedActions { get; set; } = new();
}

public sealed class AccountingProviderSwitchMonitoringCheckResponse
{
    public string CheckKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime ObservedUtc { get; set; }
}

public sealed class AccountingProviderSwitchMonitoringIncidentResponse
{
    public Guid Id { get; set; }
    public string CheckKey { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? TaskId { get; set; }
    public int OccurrenceCount { get; set; }
    public long Version { get; set; }
}

public sealed class AccountingProviderSwitchMonitoringAllowedActionsResponse
{
    public bool CanRunNow { get; set; }
    public bool CanRetry { get; set; }
    public bool CanReconnectAccess { get; set; }
    public bool CanReconcileProviderOutcome { get; set; }
    public bool CanRequestClosure { get; set; }
    public bool CanClose { get; set; }
    public bool CanCreateCorrectiveCutover { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchOperationsResponse
{
    public long StuckWorkflows { get; set; }
    public long ExpiredApprovals { get; set; }
    public long StaleFreezes { get; set; }
    public long ExhaustedRetries { get; set; }
    public long AmbiguousOutcomes { get; set; }
    public long UnreconciledTotals { get; set; }
    public List<AccountingProviderSwitchOperationIssueResponse> Issues { get; set; } = [];
}

public sealed class AccountingProviderSwitchOperationIssueResponse
{
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public long Count { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
}

public sealed class AcceptAccountingProviderSwitchMonitoringExceptionApiRequest
{
    public Guid IncidentId { get; set; }
    public long ExpectedVersion { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public decimal FinancialImpact { get; set; }
    public string EvidenceReference { get; set; } = string.Empty;
}

public sealed class CreateCorrectiveAccountingProviderSwitchApiRequest
{
    public Guid EffectiveFiscalPeriodId { get; set; }
    public long ExpectedVersion { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public AccountingProviderSwitchEndpointResponse Source { get; set; } = new();
    public AccountingProviderSwitchEndpointResponse Target { get; set; } = new();
    public string Direction { get; set; } = string.Empty;
    public Guid EffectiveFiscalPeriodId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public string MigrationStrategy { get; set; } = string.Empty;
    public string MigrationStrategyLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ResponsibleUserId { get; set; }
    public Guid? ResponsibleAgentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public string? FailureSummary { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime StatusChangedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public long Version { get; set; }
}

public sealed class AccountingProviderSwitchEndpointResponse
{
    public string Kind { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchAllowedActionsResponse
{
    public Guid SwitchId { get; set; }
    public long Version { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsTerminal { get; set; }
    public bool CanUpdatePlan { get; set; }
    public bool CanCancel { get; set; }
    public bool IsReadyForNextStep { get; set; }
    public List<string> AllowedTransitions { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
    public string? BlockingSummary { get; set; }
}

public sealed class AccountingProviderSwitchAssessmentResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? FailureSummary { get; set; }
    public List<AccountingProviderSwitchCapabilityResponse> Capabilities { get; set; } = [];
    public List<AccountingProviderSwitchDatasetResponse> Datasets { get; set; } = [];
    public List<AccountingProviderSwitchGapResponse> Gaps { get; set; } = [];
    public bool HasBlockingGaps { get; set; }
    public string AllowedNextAction { get; set; } = string.Empty;
    public string AllowedNextActionExplanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchCapabilityResponse
{
    public string EndpointRole { get; set; } = string.Empty;
    public string CapabilityKey { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchDatasetResponse
{
    public string EndpointRole { get; set; } = string.Empty;
    public string DatasetKey { get; set; } = string.Empty;
    public string Availability { get; set; } = string.Empty;
    public long RecordCount { get; set; }
    public decimal FinancialTotal { get; set; }
    public string? Currency { get; set; }
    public string? FailureSummary { get; set; }
}

public sealed class AccountingProviderSwitchGapResponse
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? DatasetKey { get; set; }
    public string Severity { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string OperatorAction { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}

public sealed class AccountingProviderSwitchCompletenessResponse
{
    public bool IsComplete { get; set; }
    public long ExpectedCount { get; set; }
    public long StagedCount { get; set; }
    public long ValidDispositionCount { get; set; }
    public long BlockingCount { get; set; }
    public List<AccountingProviderSwitchDatasetCompletenessResponse> Datasets { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchDatasetCompletenessResponse
{
    public string Dataset { get; set; } = string.Empty;
    public long ExpectedCount { get; set; }
    public long StagedCount { get; set; }
    public long ValidDispositionCount { get; set; }
    public bool IsComplete { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchMappingResponse
{
    public Guid Id { get; set; }
    public int MappingVersion { get; set; }
    public string MappingType { get; set; } = string.Empty;
    public string SourceKey { get; set; } = string.Empty;
    public string? TargetKey { get; set; }
    public decimal Confidence { get; set; }
    public bool IsMaterial { get; set; }
    public long AffectedRecordCount { get; set; }
    public decimal AffectedFinancialTotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
    public bool IsApprovalCurrent { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public long Version { get; set; }
}

public sealed class AccountingProviderSwitchRehearsalResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool ProviderAcceptanceProven { get; set; }
    public string? Disclosure { get; set; }
    public int ProgressPercent { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? FailureSummary { get; set; }
    public List<AccountingProviderSwitchRehearsalDatasetResponse> Datasets { get; set; } = [];
    public List<AccountingProviderSwitchReconciliationCheckResponse> Checks { get; set; } = [];
    public bool IsReadyForPlan { get; set; }
    public string ReadinessExplanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchRehearsalDatasetResponse
{
    public string Dataset { get; set; } = string.Empty;
    public long ExpectedCount { get; set; }
    public long ObservedCount { get; set; }
    public decimal ExpectedTotal { get; set; }
    public decimal ObservedTotal { get; set; }
    public string? Currency { get; set; }
    public string Result { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchReconciliationCheckResponse
{
    public Guid Id { get; set; }
    public string CheckKey { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ObservedValue { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string DataSourcesJson { get; set; } = string.Empty;
    public bool ManualEvidenceAllowed { get; set; }
    public bool HasCurrentManualEvidence { get; set; }
    public DateTime CalculatedUtc { get; set; }
}

public sealed class AccountingProviderSwitchPlanReadinessResponse
{
    public AccountingProviderSwitchCutoverPlanResponse? Plan { get; set; }
    public bool IsReady { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchCutoverPlanResponse
{
    public Guid Id { get; set; }
    public int PlanVersion { get; set; }
    public DateTime FreezeStartsUtc { get; set; }
    public DateTime FreezeEndsUtc { get; set; }
    public string RecoveryBoundary { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
    public string? ApprovalStatus { get; set; }
    public bool IsCurrent { get; set; }
    public bool IsApprovedAndCurrent { get; set; }
}

public sealed class AccountingProviderSwitchInternalReadinessResponse
{
    public bool IsReady { get; set; }
    public bool IsStatutoryComplianceValidated { get; set; }
    public string ComplianceDisclosure { get; set; } = string.Empty;
    public List<AccountingProviderSwitchReadinessCheckResponse> Checks { get; set; } = [];
}

public sealed class AccountingProviderSwitchReadinessCheckResponse
{
    public string CheckKey { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public bool IsBlocking { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchPreparationResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ProgressPercent { get; set; }
    public int CandidateCount { get; set; }
    public int ValidCandidateCount { get; set; }
    public int RejectedCandidateCount { get; set; }
    public string? FailureSummary { get; set; }
    public bool IsActivationReady { get; set; }
    public string ActivationReadinessExplanation { get; set; } = string.Empty;
}

public sealed class AccountingProviderSwitchTargetTransferResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalItemCount { get; set; }
    public int CompletedItemCount { get; set; }
    public int FailedItemCount { get; set; }
    public int ReconciliationItemCount { get; set; }
    public string? FailureSummary { get; set; }
    public bool IsReadyForCutover { get; set; }
    public string ReadinessExplanation { get; set; } = string.Empty;
    public List<AccountingProviderSwitchTargetTransferItemResponse> Items { get; set; } = [];
}

public sealed class AccountingProviderSwitchTargetTransferItemResponse
{
    public Guid Id { get; set; }
    public string Dataset { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
    public string? SafeSummary { get; set; }
    public bool ReconciliationNeeded { get; set; }
    public long Version { get; set; }
}

public sealed class AccountingProviderSwitchCutoverResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public bool TargetActivityRecorded { get; set; }
    public bool ProviderReconciliationRequired { get; set; }
    public string? FailureSummary { get; set; }
    public string? NextAction { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public DateTime? FreezeStartedUtc { get; set; }
    public DateTime? ReconciledUtc { get; set; }
    public DateTime? ActivatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public long Version { get; set; }
    public List<AccountingProviderSwitchFinalCheckResponse> Checks { get; set; } = [];
    public AccountingProviderSwitchActivationApprovalResponse? ActivationApproval { get; set; }
    public AccountingProviderSwitchCutoverAllowedActionsResponse AllowedActions { get; set; } = new();
}

public sealed class AccountingProviderSwitchFinalCheckResponse
{
    public string CheckKey { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public DateTime CalculatedUtc { get; set; }
}

public sealed class AccountingProviderSwitchActivationApprovalResponse
{
    public Guid ApprovalRequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedUtc { get; set; }
}

public sealed class AccountingProviderSwitchCutoverAllowedActionsResponse
{
    public bool CanStartFreeze { get; set; }
    public bool CanRequestActivationApproval { get; set; }
    public bool CanActivate { get; set; }
    public bool CanCancel { get; set; }
    public bool CanRetry { get; set; }
    public bool CanRecoverSource { get; set; }
    public bool RequiresProviderReconciliation { get; set; }
    public bool RequiresCorrectiveCutover { get; set; }
}

public sealed class AccountingMigrationGuidanceResponse
{
    public Guid SwitchId { get; set; }
    public long SwitchVersion { get; set; }
    public string CurrentStep { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public List<string> Blockers { get; set; } = [];
    public List<string> Evidence { get; set; } = [];
    public List<string> AllowedActions { get; set; } = [];
    public string ResponsibleParty { get; set; } = string.Empty;
    public string NextCheckpoint { get; set; } = string.Empty;
    public List<string> DataSources { get; set; } = [];
    public DateTime GeneratedUtc { get; set; }
}

public sealed class AccountingMigrationRecommendationResponse
{
    public long? SwitchVersion { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public List<string> Preconditions { get; set; } = [];
    public List<string> DataSources { get; set; } = [];
    public decimal Confidence { get; set; }
    public DateTime GeneratedUtc { get; set; }
}

public sealed class AccountingMigrationEvidenceResponse
{
    public string Summary { get; set; } = string.Empty;
    public List<AccountingMigrationEvidenceItemResponse> Items { get; set; } = [];
    public List<string> DataSources { get; set; } = [];
    public DateTime AsOfUtc { get; set; }
}

public sealed class AccountingMigrationEvidenceItemResponse
{
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public bool NeedsAttention { get; set; }
}

public sealed class CreateAccountingProviderSwitchApiRequest
{
    public string SourceKind { get; set; } = string.Empty;
    public string? SourceProviderKey { get; set; }
    public string TargetKind { get; set; } = string.Empty;
    public string? TargetProviderKey { get; set; }
    public Guid EffectiveFiscalPeriodId { get; set; }
    public string MigrationStrategy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid ResponsibleUserId { get; set; }
    public Guid? ResponsibleAgentId { get; set; }
}

public sealed class CancelAccountingProviderSwitchApiRequest
{
    public string Reason { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}

public class StartAccountingProviderSwitchRunApiRequest
{
    public long ExpectedSwitchVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class StartAccountingProviderSwitchPlanRunApiRequest : StartAccountingProviderSwitchRunApiRequest
{
    public Guid PlanId { get; set; }
}

public sealed class GenerateAccountingProviderSwitchCutoverPlanApiRequest
{
    public Guid RehearsalId { get; set; }
    public long ExpectedSwitchVersion { get; set; }
    public DateTime FreezeStartsUtc { get; set; }
    public DateTime FreezeEndsUtc { get; set; }
    public string RecoveryBoundary { get; set; } = string.Empty;
    public List<Guid> ParticipantUserIds { get; set; } = [];
}

public sealed class ReconcileAccountingProviderSwitchTransferItemApiRequest
{
    public bool ProviderConfirmedSuccess { get; set; }
    public string? ProviderExternalId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public long ExpectedItemVersion { get; set; }
}
