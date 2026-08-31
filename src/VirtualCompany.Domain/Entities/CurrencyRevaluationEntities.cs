namespace VirtualCompany.Domain.Entities;

public static class CurrencyRevaluationRunStatuses
{
    public const string Draft = "draft";
    public const string NeedsReview = "needs_review";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
    public const string Superseded = "superseded";
    public const string Failed = "failed";

    public static bool IsMutable(string value) => value is Draft or NeedsReview or AwaitingApproval;
}

public static class CurrencyRevaluationPopulationStatuses
{
    public const string Included = "included";
    public const string Excluded = "excluded";
    public const string NeedsReview = "needs_review";
}

public static class CurrencyRevaluationReviewActions
{
    public const string Include = "include";
    public const string Exclude = "exclude";
    public const string RequireReview = "require_review";
    public const string Submit = "submit";
    public const string Post = "post";
    public const string Reverse = "reverse";
    public const string Supersede = "supersede";
    public const string Fail = "fail";
}

public static class CurrencyRevaluationMonetaryClasses
{
    public const string Cash = "cash";
    public const string Receivable = "receivable";
    public const string Payable = "payable";
    public const string Other = "other";

    public static string Normalize(string value) => Token(value, nameof(value)) switch
    {
        Cash => Cash,
        Receivable => Receivable,
        Payable => Payable,
        Other => Other,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Monetary account class is not supported.")
    };

    private static string Token(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A monetary account class is required.", name)
        : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public sealed class CurrencyRevaluationRun : ICompanyOwnedEntity
{
    private CurrencyRevaluationRun() { }

    public CurrencyRevaluationRun(Guid id, Guid companyId, Guid fiscalPeriodId, int runNumber,
        DateOnly asOfDate, string functionalCurrency, string voucherSeriesCode, string requestIdentity,
        Guid createdByUserId, DateTime createdUtc, bool scheduled)
    {
        Id = Required(id, nameof(id));
        CompanyId = Required(companyId, nameof(companyId));
        FiscalPeriodId = Required(fiscalPeriodId, nameof(fiscalPeriodId));
        if (runNumber < 1) throw new ArgumentOutOfRangeException(nameof(runNumber));
        RunNumber = runNumber;
        AsOfDate = asOfDate;
        FunctionalCurrency = Currency(functionalCurrency, nameof(functionalCurrency));
        VoucherSeriesCode = Text(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        RequestIdentity = Text(requestIdentity, nameof(requestIdentity), 200);
        CreatedByUserId = Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        IsScheduled = scheduled;
        Status = CurrencyRevaluationRunStatuses.Draft;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public int RunNumber { get; private set; }
    public DateOnly AsOfDate { get; private set; }
    public string FunctionalCurrency { get; private set; } = null!;
    public string VoucherSeriesCode { get; private set; } = null!;
    public string RequestIdentity { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? FailureReasonCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public string? PopulationChecksum { get; private set; }
    public string? RateSetChecksum { get; private set; }
    public string? ProposalChecksum { get; private set; }
    public int PopulationCount { get; private set; }
    public int IncludedCount { get; private set; }
    public int ExcludedCount { get; private set; }
    public int ReviewCount { get; private set; }
    public decimal DocumentBalanceTotal { get; private set; }
    public decimal CarryingFunctionalTotal { get; private set; }
    public decimal RevaluedFunctionalTotal { get; private set; }
    public decimal ProposedAdjustmentTotal { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public Guid? ReversalLedgerEntryId { get; private set; }
    public Guid? SupersededByRunId { get; private set; }
    public bool IsScheduled { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? PostedByUserId { get; private set; }
    public Guid? ReversedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; }
    public DateTime? ReversedUtc { get; private set; }
    public long Version { get; private set; }
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public LedgerEntry? LedgerEntry { get; private set; }
    public LedgerEntry? ReversalLedgerEntry { get; private set; }
    public CurrencyRevaluationRun? SupersededByRun { get; private set; }
    public ICollection<CurrencyRevaluationPopulationItem> PopulationItems { get; } = new List<CurrencyRevaluationPopulationItem>();
    public ICollection<CurrencyRevaluationRateBinding> RateBindings { get; } = new List<CurrencyRevaluationRateBinding>();
    public ICollection<CurrencyRevaluationProposalLine> ProposalLines { get; } = new List<CurrencyRevaluationProposalLine>();
    public ICollection<CurrencyRevaluationReview> Reviews { get; } = new List<CurrencyRevaluationReview>();
    public ICollection<CurrencyRevaluationReconciliation> Reconciliations { get; } = new List<CurrencyRevaluationReconciliation>();

    public void RecordProposal(string populationChecksum, string rateSetChecksum, string proposalChecksum,
        int populationCount, int includedCount, int excludedCount, int reviewCount, decimal documentBalanceTotal,
        decimal carryingFunctionalTotal, decimal revaluedFunctionalTotal, decimal proposedAdjustmentTotal,
        DateTime updatedUtc)
    {
        EnsureMutable();
        PopulationChecksum = Hash(populationChecksum, nameof(populationChecksum));
        RateSetChecksum = Hash(rateSetChecksum, nameof(rateSetChecksum));
        ProposalChecksum = Hash(proposalChecksum, nameof(proposalChecksum));
        PopulationCount = Math.Max(0, populationCount);
        IncludedCount = Math.Max(0, includedCount);
        ExcludedCount = Math.Max(0, excludedCount);
        ReviewCount = Math.Max(0, reviewCount);
        DocumentBalanceTotal = documentBalanceTotal;
        CarryingFunctionalTotal = carryingFunctionalTotal;
        RevaluedFunctionalTotal = revaluedFunctionalTotal;
        ProposedAdjustmentTotal = proposedAdjustmentTotal;
        ApprovalRequestId = null;
        SubmittedUtc = null;
        Status = reviewCount > 0 ? CurrencyRevaluationRunStatuses.NeedsReview : CurrencyRevaluationRunStatuses.Draft;
        Touch(updatedUtc);
    }

    public void BindApproval(Guid approvalRequestId, DateTime submittedUtc)
    {
        if (Status != CurrencyRevaluationRunStatuses.Draft || ReviewCount != 0 || string.IsNullOrWhiteSpace(ProposalChecksum))
            throw new InvalidOperationException("Only a complete current revaluation proposal can be submitted for approval.");
        ApprovalRequestId = Required(approvalRequestId, nameof(approvalRequestId));
        SubmittedUtc = Utc(submittedUtc, nameof(submittedUtc));
        Status = CurrencyRevaluationRunStatuses.AwaitingApproval;
        Touch(SubmittedUtc.Value);
    }

    public void MarkPosted(Guid ledgerEntryId, Guid actorUserId, DateTime postedUtc)
    {
        if (Status != CurrencyRevaluationRunStatuses.AwaitingApproval || !ApprovalRequestId.HasValue)
            throw new InvalidOperationException("Only an approved submitted revaluation can be posted.");
        LedgerEntryId = Required(ledgerEntryId, nameof(ledgerEntryId));
        PostedByUserId = Required(actorUserId, nameof(actorUserId));
        PostedUtc = Utc(postedUtc, nameof(postedUtc));
        Status = CurrencyRevaluationRunStatuses.Posted;
        Touch(PostedUtc.Value);
    }

    public void MarkCompletedWithoutPosting(Guid actorUserId, DateTime completedUtc)
    {
        if (Status != CurrencyRevaluationRunStatuses.AwaitingApproval || !ApprovalRequestId.HasValue || ProposedAdjustmentTotal != 0m)
            throw new InvalidOperationException("Only an approved zero-adjustment revaluation can complete without a journal.");
        PostedByUserId = Required(actorUserId, nameof(actorUserId));
        PostedUtc = Utc(completedUtc, nameof(completedUtc));
        Status = CurrencyRevaluationRunStatuses.Posted;
        Touch(PostedUtc.Value);
    }

    public void MarkReversed(Guid ledgerEntryId, Guid actorUserId, DateTime reversedUtc)
    {
        if (Status == CurrencyRevaluationRunStatuses.Reversed && ReversalLedgerEntryId == ledgerEntryId) return;
        if (Status != CurrencyRevaluationRunStatuses.Posted || !LedgerEntryId.HasValue)
            throw new InvalidOperationException("Only a posted revaluation can be reversed.");
        ReversalLedgerEntryId = Required(ledgerEntryId, nameof(ledgerEntryId));
        ReversedByUserId = Required(actorUserId, nameof(actorUserId));
        ReversedUtc = Utc(reversedUtc, nameof(reversedUtc));
        Status = CurrencyRevaluationRunStatuses.Reversed;
        Touch(ReversedUtc.Value);
    }

    public void Supersede(Guid replacementRunId, DateTime updatedUtc)
    {
        if (!CurrencyRevaluationRunStatuses.IsMutable(Status)) return;
        SupersededByRunId = Required(replacementRunId, nameof(replacementRunId));
        Status = CurrencyRevaluationRunStatuses.Superseded;
        Touch(updatedUtc);
    }

    public void Fail(string reasonCode, string summary, DateTime failedUtc)
    {
        if (Status is CurrencyRevaluationRunStatuses.Posted or CurrencyRevaluationRunStatuses.Reversed)
            throw new InvalidOperationException("Posted revaluation evidence cannot be changed to failed.");
        FailureReasonCode = Text(reasonCode, nameof(reasonCode), 96).ToLowerInvariant();
        FailureSummary = Text(summary, nameof(summary), 1000);
        Status = CurrencyRevaluationRunStatuses.Failed;
        Touch(failedUtc);
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The revaluation run changed after it was loaded.");
    }

    private void EnsureMutable()
    {
        if (!CurrencyRevaluationRunStatuses.IsMutable(Status))
            throw new InvalidOperationException("This revaluation run is immutable.");
    }

    private void Touch(DateTime updatedUtc) { UpdatedUtc = Utc(updatedUtc, nameof(updatedUtc)); Version++; }
    internal static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    internal static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim() is var text && text.Length <= max ? text : throw new ArgumentOutOfRangeException(name);
    internal static string? Optional(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim() is var text && text.Length <= max ? text : throw new ArgumentOutOfRangeException(name);
    internal static string Currency(string value, string name) => Text(value, name, 3).ToUpperInvariant() is var code && code.Length == 3
        ? code : throw new ArgumentException("Currency must be a three-letter code.", name);
    internal static string Hash(string value, string name) => Text(value, name, 64).ToLowerInvariant();
    internal static DateTime Utc(DateTime value, string name) => EntityTimestampNormalizer.NormalizeUtc(value, name);
}

public sealed class CurrencyRevaluationPopulationItem : ICompanyOwnedEntity
{
    private CurrencyRevaluationPopulationItem() { }

    public CurrencyRevaluationPopulationItem(Guid id, Guid companyId, Guid runId, string populationKey,
        string monetaryClass, Guid financeAccountId, string accountCode, string accountName, string normalBalance,
        string documentCurrency, string functionalCurrency, decimal documentBalance, decimal carryingFunctionalAmount,
        decimal revaluedFunctionalAmount, decimal adjustmentAmount, Guid? exchangeRateConversionId,
        decimal? periodEndRate, DateOnly? rateDate, string sourceChecksum, string status, string? reviewReason)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id));
        CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId));
        RunId = CurrencyRevaluationRun.Required(runId, nameof(runId));
        PopulationKey = CurrencyRevaluationRun.Text(populationKey, nameof(populationKey), 200);
        MonetaryClass = CurrencyRevaluationMonetaryClasses.Normalize(monetaryClass);
        FinanceAccountId = CurrencyRevaluationRun.Required(financeAccountId, nameof(financeAccountId));
        AccountCode = CurrencyRevaluationRun.Text(accountCode, nameof(accountCode), 32);
        AccountName = CurrencyRevaluationRun.Text(accountName, nameof(accountName), 160);
        NormalBalance = FinanceNormalBalanceValues.NormalizeOptional(normalBalance)
            ?? throw new ArgumentException("Normal balance is required.", nameof(normalBalance));
        DocumentCurrency = CurrencyRevaluationRun.Currency(documentCurrency, nameof(documentCurrency));
        FunctionalCurrency = CurrencyRevaluationRun.Currency(functionalCurrency, nameof(functionalCurrency));
        DocumentBalance = documentBalance;
        CarryingFunctionalAmount = carryingFunctionalAmount;
        RevaluedFunctionalAmount = revaluedFunctionalAmount;
        AdjustmentAmount = adjustmentAmount;
        if (exchangeRateConversionId == Guid.Empty) throw new ArgumentException("Conversion id cannot be empty.", nameof(exchangeRateConversionId));
        if (periodEndRate is <= 0m) throw new ArgumentOutOfRangeException(nameof(periodEndRate));
        ExchangeRateConversionId = exchangeRateConversionId;
        PeriodEndRate = periodEndRate;
        RateDate = rateDate;
        SourceChecksum = CurrencyRevaluationRun.Hash(sourceChecksum, nameof(sourceChecksum));
        Status = NormalizeStatus(status);
        ReviewReason = CurrencyRevaluationRun.Optional(reviewReason, nameof(reviewReason), 1000);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public string PopulationKey { get; private set; } = null!;
    public string MonetaryClass { get; private set; } = null!;
    public Guid FinanceAccountId { get; private set; }
    public string AccountCode { get; private set; } = null!;
    public string AccountName { get; private set; } = null!;
    public string NormalBalance { get; private set; } = null!;
    public string DocumentCurrency { get; private set; } = null!;
    public string FunctionalCurrency { get; private set; } = null!;
    public decimal DocumentBalance { get; private set; }
    public decimal CarryingFunctionalAmount { get; private set; }
    public decimal RevaluedFunctionalAmount { get; private set; }
    public decimal AdjustmentAmount { get; private set; }
    public Guid? ExchangeRateConversionId { get; private set; }
    public decimal? PeriodEndRate { get; private set; }
    public DateOnly? RateDate { get; private set; }
    public string SourceChecksum { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? ReviewReason { get; private set; }
    public CurrencyRevaluationRun Run { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
    public ExchangeRateConversion? ExchangeRateConversion { get; private set; }

    public void Review(string action, string reason)
    {
        Status = action switch
        {
            CurrencyRevaluationReviewActions.Include => CurrencyRevaluationPopulationStatuses.Included,
            CurrencyRevaluationReviewActions.Exclude => CurrencyRevaluationPopulationStatuses.Excluded,
            CurrencyRevaluationReviewActions.RequireReview => CurrencyRevaluationPopulationStatuses.NeedsReview,
            _ => throw new ArgumentOutOfRangeException(nameof(action), "Review action is not supported.")
        };
        ReviewReason = CurrencyRevaluationRun.Text(reason, nameof(reason), 1000);
    }

    private static string NormalizeStatus(string value) => value switch
    {
        CurrencyRevaluationPopulationStatuses.Included => value,
        CurrencyRevaluationPopulationStatuses.Excluded => value,
        CurrencyRevaluationPopulationStatuses.NeedsReview => value,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Population status is not supported.")
    };
}

public sealed class CurrencyRevaluationRateBinding : ICompanyOwnedEntity
{
    private CurrencyRevaluationRateBinding() { }
    public CurrencyRevaluationRateBinding(Guid id, Guid companyId, Guid runId, Guid populationItemId,
        Guid exchangeRateConversionId, string documentCurrency, string functionalCurrency, decimal effectiveRate,
        DateOnly rateDate, string rateSetIdentity, string observationIdentity, string evidenceChecksum)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId));
        RunId = CurrencyRevaluationRun.Required(runId, nameof(runId)); PopulationItemId = CurrencyRevaluationRun.Required(populationItemId, nameof(populationItemId));
        ExchangeRateConversionId = CurrencyRevaluationRun.Required(exchangeRateConversionId, nameof(exchangeRateConversionId));
        DocumentCurrency = CurrencyRevaluationRun.Currency(documentCurrency, nameof(documentCurrency)); FunctionalCurrency = CurrencyRevaluationRun.Currency(functionalCurrency, nameof(functionalCurrency));
        if (effectiveRate <= 0m) throw new ArgumentOutOfRangeException(nameof(effectiveRate)); EffectiveRate = effectiveRate; RateDate = rateDate;
        RateSetIdentity = CurrencyRevaluationRun.Text(rateSetIdentity, nameof(rateSetIdentity), 1000);
        ObservationIdentity = CurrencyRevaluationRun.Text(observationIdentity, nameof(observationIdentity), 1000);
        EvidenceChecksum = CurrencyRevaluationRun.Hash(evidenceChecksum, nameof(evidenceChecksum));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public Guid PopulationItemId { get; private set; } public Guid ExchangeRateConversionId { get; private set; }
    public string DocumentCurrency { get; private set; } = null!; public string FunctionalCurrency { get; private set; } = null!;
    public decimal EffectiveRate { get; private set; } public DateOnly RateDate { get; private set; }
    public string RateSetIdentity { get; private set; } = null!; public string ObservationIdentity { get; private set; } = null!;
    public string EvidenceChecksum { get; private set; } = null!;
    public CurrencyRevaluationRun Run { get; private set; } = null!; public CurrencyRevaluationPopulationItem PopulationItem { get; private set; } = null!;
    public ExchangeRateConversion ExchangeRateConversion { get; private set; } = null!;
}

public sealed class CurrencyRevaluationProposalLine : ICompanyOwnedEntity
{
    private CurrencyRevaluationProposalLine() { }
    public CurrencyRevaluationProposalLine(Guid id, Guid companyId, Guid runId, int sequence, Guid financeAccountId,
        Guid? populationItemId, string lineType, decimal debitAmount, decimal creditAmount, string currency, string description)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId));
        RunId = CurrencyRevaluationRun.Required(runId, nameof(runId)); if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence)); Sequence = sequence;
        FinanceAccountId = CurrencyRevaluationRun.Required(financeAccountId, nameof(financeAccountId)); if (populationItemId == Guid.Empty) throw new ArgumentException("Population item id cannot be empty.", nameof(populationItemId)); PopulationItemId = populationItemId;
        LineType = CurrencyRevaluationRun.Text(lineType, nameof(lineType), 32).ToLowerInvariant();
        if (debitAmount < 0m || creditAmount < 0m || debitAmount == 0m && creditAmount == 0m || debitAmount > 0m && creditAmount > 0m) throw new ArgumentException("A proposal line requires one positive debit or credit amount.");
        DebitAmount = debitAmount; CreditAmount = creditAmount; Currency = CurrencyRevaluationRun.Currency(currency, nameof(currency)); Description = CurrencyRevaluationRun.Text(description, nameof(description), 500);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public int Sequence { get; private set; } public Guid FinanceAccountId { get; private set; } public Guid? PopulationItemId { get; private set; }
    public string LineType { get; private set; } = null!; public decimal DebitAmount { get; private set; } public decimal CreditAmount { get; private set; }
    public string Currency { get; private set; } = null!; public string Description { get; private set; } = null!;
    public CurrencyRevaluationRun Run { get; private set; } = null!; public FinanceAccount FinanceAccount { get; private set; } = null!;
    public CurrencyRevaluationPopulationItem? PopulationItem { get; private set; }
}

public sealed class CurrencyRevaluationReview : ICompanyOwnedEntity
{
    private CurrencyRevaluationReview() { }
    public CurrencyRevaluationReview(Guid id, Guid companyId, Guid runId, Guid? populationItemId, string action,
        string reason, Guid actorUserId, Guid? approvalRequestId, string evidenceChecksum, DateTime occurredUtc)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId)); RunId = CurrencyRevaluationRun.Required(runId, nameof(runId));
        if (populationItemId == Guid.Empty || approvalRequestId == Guid.Empty) throw new ArgumentException("Optional identifiers cannot be empty."); PopulationItemId = populationItemId;
        Action = CurrencyRevaluationRun.Text(action, nameof(action), 32).ToLowerInvariant(); Reason = CurrencyRevaluationRun.Text(reason, nameof(reason), 1000);
        ActorUserId = CurrencyRevaluationRun.Required(actorUserId, nameof(actorUserId)); ApprovalRequestId = approvalRequestId; EvidenceChecksum = CurrencyRevaluationRun.Hash(evidenceChecksum, nameof(evidenceChecksum)); OccurredUtc = CurrencyRevaluationRun.Utc(occurredUtc, nameof(occurredUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; } public Guid? PopulationItemId { get; private set; }
    public string Action { get; private set; } = null!; public string Reason { get; private set; } = null!; public Guid ActorUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; } public string EvidenceChecksum { get; private set; } = null!; public DateTime OccurredUtc { get; private set; }
    public CurrencyRevaluationRun Run { get; private set; } = null!; public CurrencyRevaluationPopulationItem? PopulationItem { get; private set; }
}

public sealed class CurrencyRevaluationReconciliation : ICompanyOwnedEntity
{
    private CurrencyRevaluationReconciliation() { }
    public CurrencyRevaluationReconciliation(Guid id, Guid companyId, Guid runId, string reconciliationType,
        int populationCount, decimal carryingAmount, decimal revaluedAmount, decimal proposedAdjustment,
        decimal proposalLineAdjustment, decimal difference, string currency, string checksum)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId)); RunId = CurrencyRevaluationRun.Required(runId, nameof(runId));
        ReconciliationType = CurrencyRevaluationRun.Text(reconciliationType, nameof(reconciliationType), 32).ToLowerInvariant(); PopulationCount = Math.Max(0, populationCount);
        CarryingAmount = carryingAmount; RevaluedAmount = revaluedAmount; ProposedAdjustment = proposedAdjustment; ProposalLineAdjustment = proposalLineAdjustment; Difference = difference;
        Currency = CurrencyRevaluationRun.Currency(currency, nameof(currency)); Checksum = CurrencyRevaluationRun.Hash(checksum, nameof(checksum)); IsReconciled = difference == 0m;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public string ReconciliationType { get; private set; } = null!; public int PopulationCount { get; private set; }
    public decimal CarryingAmount { get; private set; } public decimal RevaluedAmount { get; private set; } public decimal ProposedAdjustment { get; private set; }
    public decimal ProposalLineAdjustment { get; private set; } public decimal Difference { get; private set; } public string Currency { get; private set; } = null!;
    public string Checksum { get; private set; } = null!; public bool IsReconciled { get; private set; } public CurrencyRevaluationRun Run { get; private set; } = null!;
}

public sealed class CurrencyRevaluationAccountPolicy : ICompanyOwnedEntity
{
    private CurrencyRevaluationAccountPolicy() { }
    public CurrencyRevaluationAccountPolicy(Guid id, Guid companyId, Guid financeAccountId, string monetaryClass,
        bool isEnabled, Guid actorUserId, DateTime updatedUtc)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId)); FinanceAccountId = CurrencyRevaluationRun.Required(financeAccountId, nameof(financeAccountId));
        MonetaryClass = CurrencyRevaluationMonetaryClasses.Normalize(monetaryClass); IsEnabled = isEnabled; UpdatedByUserId = CurrencyRevaluationRun.Required(actorUserId, nameof(actorUserId)); UpdatedUtc = CurrencyRevaluationRun.Utc(updatedUtc, nameof(updatedUtc)); Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid FinanceAccountId { get; private set; }
    public string MonetaryClass { get; private set; } = null!; public bool IsEnabled { get; private set; } public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public long Version { get; private set; } public FinanceAccount FinanceAccount { get; private set; } = null!;
    public void Update(string monetaryClass, bool enabled, Guid actor, long expectedVersion, DateTime updatedUtc)
    { if (Version != expectedVersion) throw new InvalidOperationException("The monetary account policy changed after it was loaded."); MonetaryClass = CurrencyRevaluationMonetaryClasses.Normalize(monetaryClass); IsEnabled = enabled; UpdatedByUserId = CurrencyRevaluationRun.Required(actor, nameof(actor)); UpdatedUtc = CurrencyRevaluationRun.Utc(updatedUtc, nameof(updatedUtc)); Version++; }
}

public sealed class CurrencyRevaluationSchedule : ICompanyOwnedEntity
{
    private CurrencyRevaluationSchedule() { }
    public CurrencyRevaluationSchedule(Guid id, Guid companyId, bool isEnabled, int daysBeforePeriodEnd,
        bool automaticReversal, string voucherSeriesCode, Guid actorUserId, DateTime updatedUtc)
    {
        Id = CurrencyRevaluationRun.Required(id, nameof(id)); CompanyId = CurrencyRevaluationRun.Required(companyId, nameof(companyId));
        Apply(isEnabled, daysBeforePeriodEnd, automaticReversal, voucherSeriesCode, actorUserId, updatedUtc); Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public bool IsEnabled { get; private set; }
    public int DaysBeforePeriodEnd { get; private set; } public bool AutomaticReversal { get; private set; } public string VoucherSeriesCode { get; private set; } = null!;
    public Guid UpdatedByUserId { get; private set; } public DateTime UpdatedUtc { get; private set; } public DateTime? LastEvaluatedUtc { get; private set; } public long Version { get; private set; }
    public void Update(bool enabled, int days, bool reversal, string series, Guid actor, long expectedVersion, DateTime now)
    { if (Version != expectedVersion) throw new InvalidOperationException("The revaluation schedule changed after it was loaded."); Apply(enabled, days, reversal, series, actor, now); Version++; }
    public void MarkEvaluated(DateTime now) { LastEvaluatedUtc = CurrencyRevaluationRun.Utc(now, nameof(now)); UpdatedUtc = LastEvaluatedUtc.Value; Version++; }
    private void Apply(bool enabled, int days, bool reversal, string series, Guid actor, DateTime now)
    { if (days is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(days)); IsEnabled = enabled; DaysBeforePeriodEnd = days; AutomaticReversal = reversal; VoucherSeriesCode = CurrencyRevaluationRun.Text(series, nameof(series), 32).ToUpperInvariant(); UpdatedByUserId = CurrencyRevaluationRun.Required(actor, nameof(actor)); UpdatedUtc = CurrencyRevaluationRun.Utc(now, nameof(now)); }
}
