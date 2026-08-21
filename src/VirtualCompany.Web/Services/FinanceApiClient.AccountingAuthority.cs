namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingAuthorityReadModelResponse?> GetAccountingAuthorityAsync(
        Guid companyId,
        int exportLimit = 50,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingAuthorityReadModelResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/authority?exportLimit={exportLimit}",
            allowNotFound: false, cancellationToken);

    public Task<AccountingAuthorityChangePreviewResponse> PreviewAccountingAuthorityChangeAsync(
        Guid companyId,
        PreviewAccountingAuthorityChangeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PreviewAccountingAuthorityChangeApiRequest, AccountingAuthorityChangePreviewResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/preview", request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> StartAccountingAuthorityChangeAsync(
        Guid companyId,
        StartAccountingAuthorityChangeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingAuthorityChangeApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/change", request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> RecordAccountingCutoverValidationAsync(
        Guid companyId,
        Guid authorityPeriodId,
        RecordAccountingCutoverValidationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordAccountingCutoverValidationApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/authority/{authorityPeriodId}/cutover-validation",
            request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> CompleteAccountingAuthorityCutoverAsync(
        Guid companyId,
        Guid authorityPeriodId,
        CompleteAccountingAuthorityCutoverApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CompleteAccountingAuthorityCutoverApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/{authorityPeriodId}/complete",
            request, cancellationToken);
    }

    public Task<AccountingProviderExportResponse> QueueAccountingProviderExportAsync(
        Guid companyId,
        QueueAccountingProviderExportApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<QueueAccountingProviderExportApiRequest, AccountingProviderExportResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/provider-exports", request, cancellationToken);
    }

    public Task<AccountingProviderExportResponse> ReconcileAccountingProviderExportAsync(
        Guid companyId,
        Guid exportId,
        ReconcileAccountingProviderExportApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileAccountingProviderExportApiRequest, AccountingProviderExportResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/provider-exports/{exportId}/reconcile",
            request, cancellationToken);
    }
}

public sealed class AccountingAuthorityReadModelResponse
{
    public Guid CompanyId { get; set; }
    public AccountingAuthorityPeriodResponse? CurrentPeriod { get; set; }
    public List<AccountingAuthorityPeriodResponse> Periods { get; set; } = [];
    public List<AccountingAuthorityProviderResponse> Providers { get; set; } = [];
    public List<AccountingProviderExportResponse> Exports { get; set; } = [];
    public int PendingExportCount { get; set; }
    public int ReconciliationRequiredCount { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public bool CanChangeAuthority { get; set; }
}

public sealed class AccountingAuthorityPeriodResponse
{
    public Guid Id { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Authority { get; set; } = string.Empty;
    public string AuthorityLabel { get; set; } = string.Empty;
    public string? TargetAuthority { get; set; }
    public string? TargetAuthorityLabel { get; set; }
    public string? ProviderKey { get; set; }
    public string? ProviderName { get; set; }
    public string ChangeReason { get; set; } = string.Empty;
    public bool OpeningBalancesReconciled { get; set; }
    public bool TrialBalanceReconciled { get; set; }
    public bool SourceMappingsReconciled { get; set; }
    public int ConflictCount { get; set; }
    public string? ValidationSummary { get; set; }
    public bool IsCutoverReady { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public sealed class AccountingAuthorityProviderResponse
{
    public string ProviderKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string ConnectionStatus { get; set; } = string.Empty;
    public DateTime? LastSuccessfulSyncUtc { get; set; }
    public List<string> GrantedScopes { get; set; } = [];
    public string ModeExplanation { get; set; } = string.Empty;
    public string? SafeIssueSummary { get; set; }
}

public sealed class AccountingProviderExportResponse
{
    public Guid Id { get; set; }
    public Guid LedgerEntryId { get; set; }
    public string JournalNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public Guid WriteRequestId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public string? FailureCategory { get; set; }
    public string? SafeSummary { get; set; }
    public string? ProviderExternalId { get; set; }
    public int AttemptCount { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AccountingAuthorityChangePreviewResponse
{
    public Guid CompanyId { get; set; }
    public string CurrentAuthority { get; set; } = string.Empty;
    public string TargetAuthority { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
    public Guid EffectiveFiscalPeriodId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public int PostedJournalCount { get; set; }
    public int PendingExportCount { get; set; }
    public int UnmappedSourceCount { get; set; }
    public string PreviewToken { get; set; } = string.Empty;
    public long ExpectedCurrentVersion { get; set; }
    public bool IsAllowed { get; set; }
    public List<AccountingAuthorityIssueResponse> Issues { get; set; } = [];
    public List<AccountingAuthorityIssueResponse> Warnings { get; set; } = [];
}

public sealed class AccountingAuthorityIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
    public Guid? SubjectId { get; set; }
}

public class PreviewAccountingAuthorityChangeApiRequest
{
    public Guid EffectiveFiscalPeriodId { get; set; }
    public string TargetAuthority { get; set; } = string.Empty;
    public string? ProviderKey { get; set; }
}

public sealed class StartAccountingAuthorityChangeApiRequest : PreviewAccountingAuthorityChangeApiRequest
{
    public string Reason { get; set; } = string.Empty;
    public string PreviewToken { get; set; } = string.Empty;
    public long ExpectedCurrentVersion { get; set; }
}

public sealed class RecordAccountingCutoverValidationApiRequest
{
    public bool OpeningBalancesReconciled { get; set; }
    public bool TrialBalanceReconciled { get; set; }
    public bool SourceMappingsReconciled { get; set; }
    public int ConflictCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}

public sealed class CompleteAccountingAuthorityCutoverApiRequest
{
    public long ExpectedVersion { get; set; }
}

public sealed class QueueAccountingProviderExportApiRequest
{
    public Guid LedgerEntryId { get; set; }
    public string ProviderKey { get; set; } = string.Empty;
}

public sealed class ReconcileAccountingProviderExportApiRequest
{
    public bool ProviderConfirmedSuccess { get; set; }
    public string? ProviderExternalId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}
