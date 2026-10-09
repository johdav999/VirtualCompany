namespace VirtualCompany.Application.Finance;
public sealed record AccountingRecoveryIssueDto(string ReasonCode, string Explanation, string EntityType, string EntityId, bool IsBlocking)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EntityType { get; set; } = EntityType;
    public string EntityId { get; set; } = EntityId;
    public bool IsBlocking { get; set; } = IsBlocking;

    public AccountingRecoveryIssueDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, default !)
    {
    }
}

public sealed record AccountingReadinessSignalDto(string Key, string Status, int Count, decimal? Amount, string Explanation, string OperatorAction, IReadOnlyList<Guid> SubjectIds)
{
    public string Key { get; set; } = Key;
    public string Status { get; set; } = Status;
    public int Count { get; set; } = Count;
    public decimal? Amount { get; set; } = Amount;
    public string Explanation { get; set; } = Explanation;
    public string OperatorAction { get; set; } = OperatorAction;
    public IReadOnlyList<Guid> SubjectIds { get; set; } = SubjectIds;

    public AccountingReadinessSignalDto() : this(string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, [])
    {
    }
}

public sealed record AccountingMigrationConflictDto(Guid Id, string EntityType, string EntityId, Guid? FiscalPeriodId, string ReasonCode, string Explanation, string EvidenceJson, string OperatorAction, string Status, string? ResolutionSummary, long Version, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string EntityType { get; set; } = EntityType;
    public string EntityId { get; set; } = EntityId;
    public Guid? FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string EvidenceJson { get; set; } = EvidenceJson;
    public string OperatorAction { get; set; } = OperatorAction;
    public string Status { get; set; } = Status;
    public string? ResolutionSummary { get; set; } = ResolutionSummary;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public AccountingMigrationConflictDto() : this(default !, string.Empty, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record AccountingRecoveryVerificationDto(Guid CompanyId, Guid? FiscalPeriodId, bool ObjectContentVerified, int VoucherCount, int JournalCount, int LineCount, int SourceLinkCount, int EvidenceLinkCount, int AuditReferenceCount, int SnapshotCount, int ProviderReferenceCount, decimal TotalDebit, decimal TotalCredit, string EvidenceChecksum, bool IsValid, DateTime VerifiedUtc, IReadOnlyList<AccountingRecoveryIssueDto> Issues, IReadOnlyList<AccountingRecoveryControlDto> AdvancedControls)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid? FiscalPeriodId { get; set; } = FiscalPeriodId;
    public bool ObjectContentVerified { get; set; } = ObjectContentVerified;
    public int VoucherCount { get; set; } = VoucherCount;
    public int JournalCount { get; set; } = JournalCount;
    public int LineCount { get; set; } = LineCount;
    public int SourceLinkCount { get; set; } = SourceLinkCount;
    public int EvidenceLinkCount { get; set; } = EvidenceLinkCount;
    public int AuditReferenceCount { get; set; } = AuditReferenceCount;
    public int SnapshotCount { get; set; } = SnapshotCount;
    public int ProviderReferenceCount { get; set; } = ProviderReferenceCount;
    public decimal TotalDebit { get; set; } = TotalDebit;
    public decimal TotalCredit { get; set; } = TotalCredit;
    public string EvidenceChecksum { get; set; } = EvidenceChecksum;
    public bool IsValid { get; set; } = IsValid;
    public DateTime VerifiedUtc { get; set; } = VerifiedUtc;
    public IReadOnlyList<AccountingRecoveryIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingRecoveryControlDto> AdvancedControls { get; set; } = AdvancedControls;

    public AccountingRecoveryVerificationDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, [], default !)
    {
    }
}

public sealed record AccountingReadinessDto(Guid CompanyId, string Status, bool IsReady, DateTime EvaluatedUtc, IReadOnlyList<AccountingReadinessSignalDto> Signals)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string Status { get; set; } = Status;
    public bool IsReady { get; set; } = IsReady;
    public DateTime EvaluatedUtc { get; set; } = EvaluatedUtc;
    public IReadOnlyList<AccountingReadinessSignalDto> Signals { get; set; } = Signals;

    public AccountingReadinessDto() : this(default !, string.Empty, default !, default !, [])
    {
    }
}

public sealed record AccountingMigrationRunDto(Guid Id, Guid CompanyId, string TargetVersion, string Status, string Phase, int AttemptCount, int ScannedCount, int UpdatedCount, int ConflictCount, int ReportCount, string? FailureCode, string? FailureSummary, DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, long Version, IReadOnlyList<AccountingMigrationConflictDto> Conflicts, IReadOnlyList<AccountingCutoverReportDto> Reports)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string TargetVersion { get; set; } = TargetVersion;
    public string Status { get; set; } = Status;
    public string Phase { get; set; } = Phase;
    public int AttemptCount { get; set; } = AttemptCount;
    public int ScannedCount { get; set; } = ScannedCount;
    public int UpdatedCount { get; set; } = UpdatedCount;
    public int ConflictCount { get; set; } = ConflictCount;
    public int ReportCount { get; set; } = ReportCount;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public DateTime RequestedUtc { get; set; } = RequestedUtc;
    public DateTime? StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public long Version { get; set; } = Version;
    public IReadOnlyList<AccountingMigrationConflictDto> Conflicts { get; set; } = Conflicts;
    public IReadOnlyList<AccountingCutoverReportDto> Reports { get; set; } = Reports;

    public AccountingMigrationRunDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], [])
    {
    }
}

public sealed record AccountingCutoverReportDto(Guid Id, Guid FiscalPeriodId, string PeriodName, decimal OpeningBalance, decimal JournalDebit, decimal JournalCredit, decimal ReceivablesBalance, decimal PayablesBalance, decimal BankBalance, decimal SuspenseBalance, int TaxFactLineCount, int ProviderReferenceCount, int EvidenceLinkCount, int SnapshotCount, int IssueCount, string Checksum, DateTime GeneratedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public string PeriodName { get; set; } = PeriodName;
    public decimal OpeningBalance { get; set; } = OpeningBalance;
    public decimal JournalDebit { get; set; } = JournalDebit;
    public decimal JournalCredit { get; set; } = JournalCredit;
    public decimal ReceivablesBalance { get; set; } = ReceivablesBalance;
    public decimal PayablesBalance { get; set; } = PayablesBalance;
    public decimal BankBalance { get; set; } = BankBalance;
    public decimal SuspenseBalance { get; set; } = SuspenseBalance;
    public int TaxFactLineCount { get; set; } = TaxFactLineCount;
    public int ProviderReferenceCount { get; set; } = ProviderReferenceCount;
    public int EvidenceLinkCount { get; set; } = EvidenceLinkCount;
    public int SnapshotCount { get; set; } = SnapshotCount;
    public int IssueCount { get; set; } = IssueCount;
    public string Checksum { get; set; } = Checksum;
    public DateTime GeneratedUtc { get; set; } = GeneratedUtc;

    public AccountingCutoverReportDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record AccountingRecoveryControlDto(string Key, string Status, int RecordCount, decimal Debit, decimal Credit, decimal Difference, string Checksum);
public sealed record AccountingOperationsReadModel(Guid CompanyId, AccountingMigrationRunDto? LatestMigration, AccountingReadinessDto Readiness)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public AccountingMigrationRunDto? LatestMigration { get; set; } = LatestMigration;
    public AccountingReadinessDto Readiness { get; set; } = Readiness;

    public AccountingOperationsReadModel() : this(default !, default !, new())
    {
    }
}
