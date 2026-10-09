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

public class StartAccountingProviderSwitchRunApiRequest
{
    public long ExpectedSwitchVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class StartAccountingProviderSwitchPlanRunApiRequest : StartAccountingProviderSwitchRunApiRequest
{
    public Guid PlanId { get; set; }
}

public sealed class ReconcileAccountingProviderSwitchTransferItemApiRequest
{
    public bool ProviderConfirmedSuccess { get; set; }
    public string? ProviderExternalId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public long ExpectedItemVersion { get; set; }
}
