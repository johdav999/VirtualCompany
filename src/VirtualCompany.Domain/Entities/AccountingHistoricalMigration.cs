namespace VirtualCompany.Domain.Entities;

public static class AccountingMigrationRunStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithConflicts = "completed_with_conflicts";
    public const string NotRequired = "not_required";
    public const string Failed = "failed";

    public static bool IsTerminal(string status) => status is Completed or CompletedWithConflicts or NotRequired or Failed;
}

public static class AccountingMigrationPhases
{
    public const string Inventory = "inventory";
    public const string Accounts = "accounts";
    public const string Journals = "journals";
    public const string Reports = "reports";
    public const string Complete = "complete";
}

public static class AccountingMigrationConflictStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class AccountingMigrationConflictReasonCodes
{
    public const string ConfigurationMissing = "accounting_migration_configuration_missing";
    public const string AmbiguousAccountSemantics = "accounting_migration_ambiguous_account_semantics";
    public const string JournalSourceMissing = "accounting_migration_journal_source_missing";
    public const string JournalSourceMismatch = "accounting_migration_journal_source_mismatch";
    public const string JournalCurrencyAmbiguous = "accounting_migration_journal_currency_ambiguous";
    public const string JournalVoucherAmbiguous = "accounting_migration_journal_voucher_ambiguous";
    public const string JournalPolicyVersionUnknown = "accounting_migration_journal_policy_version_unknown";
    public const string JournalUnbalanced = "accounting_migration_journal_unbalanced";
    public const string JournalTaxFactsUnknown = "accounting_migration_journal_tax_facts_unknown";
    public const string SourceDocumentEvidenceMissing = "accounting_migration_source_document_evidence_missing";
    public const string ReconciliationStateConflict = "accounting_migration_reconciliation_state_conflict";
    public const string ProviderOutcomeAmbiguous = "accounting_migration_provider_outcome_ambiguous";
}

public sealed class AccountingMigrationRun : ICompanyOwnedEntity
{
    private AccountingMigrationRun() { }

    public AccountingMigrationRun(
        Guid id,
        Guid companyId,
        string targetVersion,
        string idempotencyKey,
        Guid requestedByUserId,
        string? correlationId,
        DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        TargetVersion = Text(targetVersion, nameof(targetVersion), 64).ToLowerInvariant();
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200);
        RequestedByUserId = Required(requestedByUserId, nameof(requestedByUserId));
        CorrelationId = Optional(correlationId, nameof(correlationId), 128);
        Status = AccountingMigrationRunStatuses.Queued;
        Phase = AccountingMigrationPhases.Inventory;
        RequestedUtc = Utc(requestedUtc, nameof(requestedUtc));
        UpdatedUtc = RequestedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TargetVersion { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string Phase { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public int ScannedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int ConflictCount { get; private set; }
    public int ReportCount { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<AccountingMigrationConflict> Conflicts { get; } = new List<AccountingMigrationConflict>();
    public ICollection<AccountingCutoverReport> Reports { get; } = new List<AccountingCutoverReport>();

    public void MarkNotRequired(DateTime completedUtc)
    {
        var utc = Utc(completedUtc, nameof(completedUtc));
        Status = AccountingMigrationRunStatuses.NotRequired;
        Phase = AccountingMigrationPhases.Complete;
        CompletedUtc = utc;
        UpdatedUtc = utc;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        Version++;
    }

    public void RecordBatch(
        string nextPhase,
        int scannedDelta,
        int updatedDelta,
        int conflictCount,
        int reportCount,
        DateTime updatedUtc)
    {
        if (scannedDelta < 0 || updatedDelta < 0 || conflictCount < 0 || reportCount < 0)
            throw new ArgumentOutOfRangeException(nameof(scannedDelta), "Migration progress counts cannot be negative.");

        Phase = NormalizePhase(nextPhase);
        Status = AccountingMigrationRunStatuses.Queued;
        ScannedCount = checked(ScannedCount + scannedDelta);
        UpdatedCount = checked(UpdatedCount + updatedDelta);
        ConflictCount = conflictCount;
        ReportCount = reportCount;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        AttemptCount = 0;
        FailureCode = null;
        FailureSummary = null;
        UpdatedUtc = Utc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void Complete(int conflictCount, int reportCount, DateTime completedUtc)
    {
        if (conflictCount < 0 || reportCount < 0) throw new ArgumentOutOfRangeException(nameof(conflictCount));
        var utc = Utc(completedUtc, nameof(completedUtc));
        Status = conflictCount == 0
            ? AccountingMigrationRunStatuses.Completed
            : AccountingMigrationRunStatuses.CompletedWithConflicts;
        Phase = AccountingMigrationPhases.Complete;
        ConflictCount = conflictCount;
        ReportCount = reportCount;
        CompletedUtc = utc;
        UpdatedUtc = utc;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        AttemptCount = 0;
        FailureCode = null;
        FailureSummary = null;
        Version++;
    }

    public void Fail(string code, string summary, int attemptCount, DateTime failedUtc)
    {
        if (attemptCount < 1) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        var utc = Utc(failedUtc, nameof(failedUtc));
        Status = AccountingMigrationRunStatuses.Failed;
        FailureCode = Text(code, nameof(code), 100);
        FailureSummary = Text(summary, nameof(summary), 1000);
        AttemptCount = attemptCount;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        CompletedUtc = utc;
        UpdatedUtc = utc;
        Version++;
    }

    public void ScheduleRetry(string code, string summary, int attemptCount, DateTime retryUtc)
    {
        if (attemptCount < 1) throw new ArgumentOutOfRangeException(nameof(attemptCount));
        var utc = Utc(retryUtc, nameof(retryUtc));
        Status = AccountingMigrationRunStatuses.Queued;
        FailureCode = Text(code, nameof(code), 100);
        FailureSummary = Text(summary, nameof(summary), 1000);
        AttemptCount = attemptCount;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        UpdatedUtc = utc;
        Version++;
    }

    private static string NormalizePhase(string value) => value switch
    {
        AccountingMigrationPhases.Inventory or AccountingMigrationPhases.Accounts or
        AccountingMigrationPhases.Journals or AccountingMigrationPhases.Reports or
        AccountingMigrationPhases.Complete => value,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting migration phase is not supported.")
    };

    private static Guid Required(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    internal static string Text(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    internal static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Text(value, name, maxLength);

    internal static DateTime Utc(DateTime value, string name) =>
        value == default
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class AccountingMigrationConflict : ICompanyOwnedEntity
{
    private AccountingMigrationConflict() { }

    public AccountingMigrationConflict(
        Guid id,
        Guid companyId,
        Guid migrationRunId,
        string targetVersion,
        string entityType,
        string entityId,
        Guid? fiscalPeriodId,
        string reasonCode,
        string explanation,
        string evidenceJson,
        string operatorAction,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        MigrationRunId = migrationRunId == Guid.Empty ? throw new ArgumentException("MigrationRunId is required.", nameof(migrationRunId)) : migrationRunId;
        TargetVersion = AccountingMigrationRun.Text(targetVersion, nameof(targetVersion), 64).ToLowerInvariant();
        EntityType = AccountingMigrationRun.Text(entityType, nameof(entityType), 64).ToLowerInvariant();
        EntityId = AccountingMigrationRun.Text(entityId, nameof(entityId), 128);
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? throw new ArgumentException("FiscalPeriodId cannot be empty.", nameof(fiscalPeriodId)) : fiscalPeriodId;
        ReasonCode = AccountingMigrationRun.Text(reasonCode, nameof(reasonCode), 100).ToLowerInvariant();
        Explanation = AccountingMigrationRun.Text(explanation, nameof(explanation), 1000);
        EvidenceJson = AccountingMigrationRun.Text(evidenceJson, nameof(evidenceJson), 16000);
        OperatorAction = AccountingMigrationRun.Text(operatorAction, nameof(operatorAction), 1000);
        Status = AccountingMigrationConflictStatuses.Open;
        CreatedUtc = AccountingMigrationRun.Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MigrationRunId { get; private set; }
    public string TargetVersion { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public Guid? FiscalPeriodId { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public string OperatorAction { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? ResolutionSummary { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingMigrationRun MigrationRun { get; private set; } = null!;
    public FiscalPeriod? FiscalPeriod { get; private set; }

    public void Resolve(string resolutionSummary, Guid actorUserId, long expectedVersion, DateTime resolvedUtc)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The migration conflict changed. Refresh and try again.");
        if (Status == AccountingMigrationConflictStatuses.Resolved) return;
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        ResolutionSummary = AccountingMigrationRun.Text(resolutionSummary, nameof(resolutionSummary), 1000);
        ResolvedByUserId = actorUserId;
        ResolvedUtc = AccountingMigrationRun.Utc(resolvedUtc, nameof(resolvedUtc));
        UpdatedUtc = ResolvedUtc.Value;
        Status = AccountingMigrationConflictStatuses.Resolved;
        Version++;
    }

    public void Reopen(string explanation, string evidenceJson, string operatorAction, Guid runId, DateTime reopenedUtc)
    {
        MigrationRunId = runId == Guid.Empty ? throw new ArgumentException("MigrationRunId is required.", nameof(runId)) : runId;
        Explanation = AccountingMigrationRun.Text(explanation, nameof(explanation), 1000);
        EvidenceJson = AccountingMigrationRun.Text(evidenceJson, nameof(evidenceJson), 16000);
        OperatorAction = AccountingMigrationRun.Text(operatorAction, nameof(operatorAction), 1000);
        Status = AccountingMigrationConflictStatuses.Open;
        ResolutionSummary = null;
        ResolvedByUserId = null;
        ResolvedUtc = null;
        UpdatedUtc = AccountingMigrationRun.Utc(reopenedUtc, nameof(reopenedUtc));
        Version++;
    }
}

public sealed class AccountingCutoverReport : ICompanyOwnedEntity
{
    private AccountingCutoverReport() { }

    public AccountingCutoverReport(
        Guid id,
        Guid companyId,
        Guid migrationRunId,
        Guid fiscalPeriodId,
        decimal openingBalance,
        decimal journalDebit,
        decimal journalCredit,
        decimal receivablesBalance,
        decimal payablesBalance,
        decimal bankBalance,
        decimal suspenseBalance,
        int taxFactLineCount,
        int providerReferenceCount,
        int evidenceLinkCount,
        int snapshotCount,
        int issueCount,
        string detailsJson,
        string checksum,
        DateTime generatedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        MigrationRunId = migrationRunId == Guid.Empty ? throw new ArgumentException("MigrationRunId is required.", nameof(migrationRunId)) : migrationRunId;
        FiscalPeriodId = fiscalPeriodId == Guid.Empty ? throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId)) : fiscalPeriodId;
        OpeningBalance = openingBalance;
        JournalDebit = journalDebit;
        JournalCredit = journalCredit;
        ReceivablesBalance = receivablesBalance;
        PayablesBalance = payablesBalance;
        BankBalance = bankBalance;
        SuspenseBalance = suspenseBalance;
        TaxFactLineCount = NonNegative(taxFactLineCount, nameof(taxFactLineCount));
        ProviderReferenceCount = NonNegative(providerReferenceCount, nameof(providerReferenceCount));
        EvidenceLinkCount = NonNegative(evidenceLinkCount, nameof(evidenceLinkCount));
        SnapshotCount = NonNegative(snapshotCount, nameof(snapshotCount));
        IssueCount = NonNegative(issueCount, nameof(issueCount));
        DetailsJson = AccountingMigrationRun.Text(detailsJson, nameof(detailsJson), 32000);
        Checksum = AccountingMigrationRun.Text(checksum, nameof(checksum), 64).ToLowerInvariant();
        GeneratedUtc = AccountingMigrationRun.Utc(generatedUtc, nameof(generatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MigrationRunId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public decimal JournalDebit { get; private set; }
    public decimal JournalCredit { get; private set; }
    public decimal ReceivablesBalance { get; private set; }
    public decimal PayablesBalance { get; private set; }
    public decimal BankBalance { get; private set; }
    public decimal SuspenseBalance { get; private set; }
    public int TaxFactLineCount { get; private set; }
    public int ProviderReferenceCount { get; private set; }
    public int EvidenceLinkCount { get; private set; }
    public int SnapshotCount { get; private set; }
    public int IssueCount { get; private set; }
    public string DetailsJson { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public DateTime GeneratedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingMigrationRun MigrationRun { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;

    private static int NonNegative(int value, string name) =>
        value < 0 ? throw new ArgumentOutOfRangeException(name) : value;
}
