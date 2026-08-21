namespace VirtualCompany.Application.Finance;

public static class AccountingOperationsReasonCodes
{
    public const string MigrationNotFound = "accounting_migration_not_found";
    public const string MigrationConflictNotFound = "accounting_migration_conflict_not_found";
    public const string MigrationAlreadyActive = "accounting_migration_already_active";
    public const string MigrationConflictStale = "accounting_migration_conflict_stale";
    public const string RestoreDocumentMissing = "accounting_restore_document_missing";
    public const string RestoreDocumentHashMismatch = "accounting_restore_document_hash_mismatch";
    public const string RestoreJournalUnbalanced = "accounting_restore_journal_unbalanced";
    public const string RestoreVoucherDuplicate = "accounting_restore_voucher_duplicate";
    public const string RestoreSourceLinkMissing = "accounting_restore_source_link_missing";
    public const string RestoreAuditReferenceMissing = "accounting_restore_audit_reference_missing";
    public const string RestoreSnapshotMismatch = "accounting_restore_snapshot_mismatch";
}

public static class AccountingReadinessStatuses
{
    public const string Ready = "ready";
    public const string Attention = "attention";
    public const string Blocked = "blocked";
}

public sealed record AccountingMigrationConflictDto(
    Guid Id,
    string EntityType,
    string EntityId,
    Guid? FiscalPeriodId,
    string ReasonCode,
    string Explanation,
    string EvidenceJson,
    string OperatorAction,
    string Status,
    string? ResolutionSummary,
    long Version,
    DateTime UpdatedUtc);

public sealed record AccountingCutoverReportDto(
    Guid Id,
    Guid FiscalPeriodId,
    string PeriodName,
    decimal OpeningBalance,
    decimal JournalDebit,
    decimal JournalCredit,
    decimal ReceivablesBalance,
    decimal PayablesBalance,
    decimal BankBalance,
    decimal SuspenseBalance,
    int TaxFactLineCount,
    int ProviderReferenceCount,
    int EvidenceLinkCount,
    int SnapshotCount,
    int IssueCount,
    string Checksum,
    DateTime GeneratedUtc);

public sealed record AccountingMigrationRunDto(
    Guid Id,
    Guid CompanyId,
    string TargetVersion,
    string Status,
    string Phase,
    int AttemptCount,
    int ScannedCount,
    int UpdatedCount,
    int ConflictCount,
    int ReportCount,
    string? FailureCode,
    string? FailureSummary,
    DateTime RequestedUtc,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    long Version,
    IReadOnlyList<AccountingMigrationConflictDto> Conflicts,
    IReadOnlyList<AccountingCutoverReportDto> Reports);

public sealed record AccountingReadinessSignalDto(
    string Key,
    string Status,
    int Count,
    decimal? Amount,
    string Explanation,
    string OperatorAction,
    IReadOnlyList<Guid> SubjectIds);

public sealed record AccountingReadinessDto(
    Guid CompanyId,
    string Status,
    bool IsReady,
    DateTime EvaluatedUtc,
    IReadOnlyList<AccountingReadinessSignalDto> Signals);

public sealed record AccountingOperationsReadModel(
    Guid CompanyId,
    AccountingMigrationRunDto? LatestMigration,
    AccountingReadinessDto Readiness);

public sealed record AccountingRecoveryIssueDto(
    string ReasonCode,
    string Explanation,
    string EntityType,
    string EntityId,
    bool IsBlocking);

public sealed record AccountingRecoveryVerificationDto(
    Guid CompanyId,
    Guid? FiscalPeriodId,
    bool ObjectContentVerified,
    int VoucherCount,
    int JournalCount,
    int LineCount,
    int SourceLinkCount,
    int EvidenceLinkCount,
    int AuditReferenceCount,
    int SnapshotCount,
    int ProviderReferenceCount,
    decimal TotalDebit,
    decimal TotalCredit,
    string EvidenceChecksum,
    bool IsValid,
    DateTime VerifiedUtc,
    IReadOnlyList<AccountingRecoveryIssueDto> Issues);

public sealed record GetAccountingOperationsQuery(Guid CompanyId);
public sealed record StartAccountingMigrationCommand(
    Guid CompanyId,
    string IdempotencyKey,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record ResolveAccountingMigrationConflictCommand(
    Guid CompanyId,
    Guid ConflictId,
    string ResolutionSummary,
    long ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record VerifyAccountingRecoveryCommand(
    Guid CompanyId,
    Guid? FiscalPeriodId,
    bool VerifyObjectContent,
    Guid ActorUserId,
    string? CorrelationId = null);

public interface IAccountingMigrationService
{
    Task<AccountingMigrationRunDto?> GetLatestAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AccountingMigrationRunDto> StartAsync(StartAccountingMigrationCommand command, CancellationToken cancellationToken);
    Task<AccountingMigrationRunDto> ResolveConflictAsync(
        ResolveAccountingMigrationConflictCommand command,
        CancellationToken cancellationToken);
}

public interface IAccountingOperationsReadService
{
    Task<AccountingOperationsReadModel> GetAsync(GetAccountingOperationsQuery query, CancellationToken cancellationToken);
}

public interface IAccountingMigrationJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public interface IAccountingReadinessService
{
    Task<AccountingReadinessDto> EvaluateAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface IAccountingRecoveryVerificationService
{
    Task<AccountingRecoveryVerificationDto> VerifyAsync(
        VerifyAccountingRecoveryCommand command,
        CancellationToken cancellationToken);
}
