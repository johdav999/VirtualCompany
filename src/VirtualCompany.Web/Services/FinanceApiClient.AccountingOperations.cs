namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingOperationsResponse?> GetAccountingOperationsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingOperationsResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/operations",
            allowNotFound: false, cancellationToken);

    public Task<AccountingMigrationRunResponse> StartAccountingMigrationAsync(
        Guid companyId,
        StartAccountingMigrationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingMigrationApiRequest, AccountingMigrationRunResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/operations/migrations",
            request, cancellationToken);
    }

    public Task<AccountingMigrationRunResponse> ResolveAccountingMigrationConflictAsync(
        Guid companyId,
        Guid conflictId,
        ResolveAccountingMigrationConflictApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ResolveAccountingMigrationConflictApiRequest, AccountingMigrationRunResponse>(
            companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/operations/migration-conflicts/{conflictId}/resolve",
            request, cancellationToken);
    }

    public Task<AccountingRecoveryVerificationResponse> VerifyAccountingRecoveryAsync(
        Guid companyId,
        VerifyAccountingRecoveryApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<VerifyAccountingRecoveryApiRequest, AccountingRecoveryVerificationResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/operations/recovery-verification",
            request, cancellationToken);
    }
}

public sealed class AccountingOperationsResponse
{
    public Guid CompanyId { get; set; }
    public AccountingMigrationRunResponse? LatestMigration { get; set; }
    public AccountingReadinessResponse Readiness { get; set; } = new();
}

public sealed class AccountingMigrationRunResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TargetVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int ScannedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int ConflictCount { get; set; }
    public int ReportCount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public long Version { get; set; }
    public List<AccountingMigrationConflictResponse> Conflicts { get; set; } = [];
    public List<AccountingCutoverReportResponse> Reports { get; set; } = [];
}

public sealed class AccountingMigrationConflictResponse
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public Guid? FiscalPeriodId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = string.Empty;
    public string OperatorAction { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ResolutionSummary { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AccountingCutoverReportResponse
{
    public Guid Id { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public string PeriodName { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal JournalDebit { get; set; }
    public decimal JournalCredit { get; set; }
    public decimal ReceivablesBalance { get; set; }
    public decimal PayablesBalance { get; set; }
    public decimal BankBalance { get; set; }
    public decimal SuspenseBalance { get; set; }
    public int TaxFactLineCount { get; set; }
    public int ProviderReferenceCount { get; set; }
    public int EvidenceLinkCount { get; set; }
    public int SnapshotCount { get; set; }
    public int IssueCount { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
}

public sealed class AccountingReadinessResponse
{
    public Guid CompanyId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public DateTime EvaluatedUtc { get; set; }
    public List<AccountingReadinessSignalResponse> Signals { get; set; } = [];
}

public sealed class AccountingReadinessSignalResponse
{
    public string Key { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal? Amount { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string OperatorAction { get; set; } = string.Empty;
    public List<Guid> SubjectIds { get; set; } = [];
}

public sealed class AccountingRecoveryVerificationResponse
{
    public Guid CompanyId { get; set; }
    public Guid? FiscalPeriodId { get; set; }
    public bool ObjectContentVerified { get; set; }
    public int VoucherCount { get; set; }
    public int JournalCount { get; set; }
    public int LineCount { get; set; }
    public int SourceLinkCount { get; set; }
    public int EvidenceLinkCount { get; set; }
    public int AuditReferenceCount { get; set; }
    public int SnapshotCount { get; set; }
    public int ProviderReferenceCount { get; set; }
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public string EvidenceChecksum { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public DateTime VerifiedUtc { get; set; }
    public List<AccountingRecoveryIssueResponse> Issues { get; set; } = [];
}

public sealed class AccountingRecoveryIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
}

public sealed class StartAccountingMigrationApiRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ResolveAccountingMigrationConflictApiRequest
{
    public string ResolutionSummary { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
}

public sealed class VerifyAccountingRecoveryApiRequest
{
    public Guid? FiscalPeriodId { get; set; }
    public bool VerifyObjectContent { get; set; }
}
