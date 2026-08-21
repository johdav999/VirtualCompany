namespace VirtualCompany.Domain.Entities;

public static class SupplierBillAccountingStatuses
{
    public const string NotReady = "not_ready";
    public const string AwaitingApproval = "awaiting_approval";
    public const string ReadyToPost = "ready_to_post";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
    public const string Blocked = "blocked";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Supplier bill accounting status is required.", nameof(value))
            : value.Trim().Replace('-', '_').ToLowerInvariant() switch
            {
                NotReady => NotReady,
                AwaitingApproval => AwaitingApproval,
                ReadyToPost => ReadyToPost,
                Posted => Posted,
                Reversed => Reversed,
                Blocked => Blocked,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Supplier bill accounting status is not supported.")
            };
}

public sealed class SupplierBillAccountingProfile : ICompanyOwnedEntity
{
    private SupplierBillAccountingProfile() { }

    public SupplierBillAccountingProfile(
        Guid id, Guid companyId, Guid billId, Guid fiscalPeriodId, string voucherSeriesCode,
        string documentCurrency, string baseCurrency, decimal exchangeRate, decimal netAmount,
        decimal recoverableTaxAmount, decimal nonRecoverableTaxAmount, decimal grossAmount,
        decimal costBaseAmount, decimal recoverableTaxBaseAmount, decimal grossBaseAmount,
        decimal roundingBaseAmount, Guid payableAccountId, string taxTreatment,
        string policyPackKey, string policyPackVersion, string policyDefinitionHash,
        string? sourceDocumentHash, Guid? originalBillId, Guid actorUserId, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || billId == Guid.Empty || fiscalPeriodId == Guid.Empty || actorUserId == Guid.Empty)
            throw new ArgumentException("Company, bill, period, and actor are required.");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        BillId = billId;
        CreatedUtc = NormalizeUtc(nowUtc);
        Version = 1;
        ApplyFacts(fiscalPeriodId, voucherSeriesCode, documentCurrency, baseCurrency, exchangeRate,
            netAmount, recoverableTaxAmount, nonRecoverableTaxAmount, grossAmount, costBaseAmount,
            recoverableTaxBaseAmount, grossBaseAmount, roundingBaseAmount, payableAccountId,
            taxTreatment, policyPackKey, policyPackVersion, policyDefinitionHash, sourceDocumentHash,
            originalBillId, actorUserId, nowUtc, incrementVersion: false);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BillId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string VoucherSeriesCode { get; private set; } = null!;
    public string DocumentCurrency { get; private set; } = null!;
    public string BaseCurrency { get; private set; } = null!;
    public decimal ExchangeRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal RecoverableTaxAmount { get; private set; }
    public decimal NonRecoverableTaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CostBaseAmount { get; private set; }
    public decimal RecoverableTaxBaseAmount { get; private set; }
    public decimal GrossBaseAmount { get; private set; }
    public decimal RoundingBaseAmount { get; private set; }
    public Guid PayableAccountId { get; private set; }
    public string TaxTreatment { get; private set; } = null!;
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string PolicyDefinitionHash { get; private set; } = null!;
    public string? SourceDocumentHash { get; private set; }
    public string Status { get; private set; } = null!;
    public string PayloadHash { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public Guid? OriginalBillId { get; private set; }
    public string? BlockingReasonCode { get; private set; }
    public string? BlockingReason { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public FinanceBill Bill { get; private set; } = null!;
    public FinanceBill? OriginalBill { get; private set; }
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public LedgerEntry? LedgerEntry { get; private set; }
    public ICollection<SupplierBillAccountingLine> Lines { get; } = new List<SupplierBillAccountingLine>();

    public void ReplaceFacts(
        Guid fiscalPeriodId, string voucherSeriesCode, string documentCurrency, string baseCurrency,
        decimal exchangeRate, decimal netAmount, decimal recoverableTaxAmount,
        decimal nonRecoverableTaxAmount, decimal grossAmount, decimal costBaseAmount,
        decimal recoverableTaxBaseAmount, decimal grossBaseAmount, decimal roundingBaseAmount,
        Guid payableAccountId, string taxTreatment, string policyPackKey, string policyPackVersion,
        string policyDefinitionHash, string? sourceDocumentHash, Guid? originalBillId,
        Guid actorUserId, DateTime nowUtc) =>
        ApplyFacts(fiscalPeriodId, voucherSeriesCode, documentCurrency, baseCurrency, exchangeRate,
            netAmount, recoverableTaxAmount, nonRecoverableTaxAmount, grossAmount, costBaseAmount,
            recoverableTaxBaseAmount, grossBaseAmount, roundingBaseAmount, payableAccountId,
            taxTreatment, policyPackKey, policyPackVersion, policyDefinitionHash, sourceDocumentHash,
            originalBillId, actorUserId, nowUtc, incrementVersion: true);

    public void SetPayloadHash(string payloadHash) =>
        PayloadHash = NormalizeRequired(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();

    public void BindApproval(Guid approvalRequestId, Guid actorUserId, DateTime nowUtc)
    {
        ApprovalRequestId = approvalRequestId == Guid.Empty
            ? throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId))
            : approvalRequestId;
        Status = SupplierBillAccountingStatuses.AwaitingApproval;
        BlockingReasonCode = null;
        BlockingReason = null;
        Touch(actorUserId, nowUtc);
    }

    public void MarkPosted(Guid ledgerEntryId, Guid actorUserId, DateTime nowUtc)
    {
        LedgerEntryId = ledgerEntryId == Guid.Empty
            ? throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId))
            : ledgerEntryId;
        Status = SupplierBillAccountingStatuses.Posted;
        BlockingReasonCode = null;
        BlockingReason = null;
        Touch(actorUserId, nowUtc);
    }

    public void MarkReversed(Guid actorUserId, DateTime nowUtc)
    {
        Status = SupplierBillAccountingStatuses.Reversed;
        Touch(actorUserId, nowUtc);
    }

    private void ApplyFacts(
        Guid fiscalPeriodId, string voucherSeriesCode, string documentCurrency, string baseCurrency,
        decimal exchangeRate, decimal netAmount, decimal recoverableTaxAmount,
        decimal nonRecoverableTaxAmount, decimal grossAmount, decimal costBaseAmount,
        decimal recoverableTaxBaseAmount, decimal grossBaseAmount, decimal roundingBaseAmount,
        Guid payableAccountId, string taxTreatment, string policyPackKey, string policyPackVersion,
        string policyDefinitionHash, string? sourceDocumentHash, Guid? originalBillId,
        Guid actorUserId, DateTime nowUtc, bool incrementVersion)
    {
        if (fiscalPeriodId == Guid.Empty || actorUserId == Guid.Empty) throw new ArgumentException("Period and actor are required.");
        FiscalPeriodId = fiscalPeriodId;
        VoucherSeriesCode = NormalizeRequired(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        DocumentCurrency = NormalizeCurrency(documentCurrency);
        BaseCurrency = NormalizeCurrency(baseCurrency);
        ExchangeRate = exchangeRate > 0m ? exchangeRate : throw new ArgumentOutOfRangeException(nameof(exchangeRate));
        NetAmount = NormalizeAmount(netAmount);
        RecoverableTaxAmount = NormalizeAmount(recoverableTaxAmount);
        NonRecoverableTaxAmount = NormalizeAmount(nonRecoverableTaxAmount);
        GrossAmount = NormalizePositive(grossAmount);
        CostBaseAmount = NormalizeAmount(costBaseAmount);
        RecoverableTaxBaseAmount = NormalizeAmount(recoverableTaxBaseAmount);
        GrossBaseAmount = NormalizePositive(grossBaseAmount);
        RoundingBaseAmount = decimal.Round(roundingBaseAmount, 6, MidpointRounding.ToEven);
        PayableAccountId = payableAccountId == Guid.Empty ? throw new ArgumentException("PayableAccountId is required.", nameof(payableAccountId)) : payableAccountId;
        TaxTreatment = NormalizeRequired(taxTreatment, nameof(taxTreatment), 32).ToLowerInvariant();
        PolicyPackKey = NormalizeRequired(policyPackKey, nameof(policyPackKey), 96).ToLowerInvariant();
        PolicyPackVersion = NormalizeRequired(policyPackVersion, nameof(policyPackVersion), 32);
        PolicyDefinitionHash = NormalizeRequired(policyDefinitionHash, nameof(policyDefinitionHash), 64).ToLowerInvariant();
        SourceDocumentHash = NormalizeOptional(sourceDocumentHash, nameof(sourceDocumentHash), 64)?.ToLowerInvariant();
        OriginalBillId = originalBillId;
        Status = SupplierBillAccountingStatuses.NotReady;
        ApprovalRequestId = null;
        LedgerEntryId = null;
        BlockingReasonCode = null;
        BlockingReason = null;
        if (incrementVersion) Version++;
        CreatedByUserId = CreatedByUserId == Guid.Empty ? actorUserId : CreatedByUserId;
        Touch(actorUserId, nowUtc);
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("ActorUserId is required.", nameof(actorUserId)) : actorUserId;
        UpdatedUtc = NormalizeUtc(nowUtc);
    }

    private static decimal NormalizeAmount(decimal value) => value < 0m ? throw new ArgumentOutOfRangeException(nameof(value)) : decimal.Round(value, 6, MidpointRounding.ToEven);
    private static decimal NormalizePositive(decimal value) => value <= 0m ? throw new ArgumentOutOfRangeException(nameof(value)) : decimal.Round(value, 6, MidpointRounding.ToEven);
    private static string NormalizeCurrency(string value) => NormalizeRequired(value, nameof(value), 3).ToUpperInvariant();
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }
    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }
}

public sealed class SupplierBillAccountingLine : ICompanyOwnedEntity
{
    private SupplierBillAccountingLine() { }

    public SupplierBillAccountingLine(
        Guid id, Guid companyId, Guid profileId, int sequence, string description,
        Guid costAccountId, string accountClassification, string taxRuleKey, string taxMethod,
        string taxTreatment, decimal taxRate, decimal netAmount, decimal taxAmount,
        decimal recoverableTaxAmount, decimal nonRecoverableTaxAmount, decimal grossAmount,
        decimal costBaseAmount, decimal recoverableTaxBaseAmount, Guid? recoverableTaxAccountId)
    {
        if (companyId == Guid.Empty || profileId == Guid.Empty || costAccountId == Guid.Empty || sequence < 1)
            throw new ArgumentException("Company, profile, cost account, and sequence are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ProfileId = profileId;
        Sequence = sequence;
        Description = Normalize(description, nameof(description), 500);
        CostAccountId = costAccountId;
        AccountClassification = Normalize(accountClassification, nameof(accountClassification), 32).ToLowerInvariant();
        TaxRuleKey = Normalize(taxRuleKey, nameof(taxRuleKey), 96).ToLowerInvariant();
        TaxMethod = Normalize(taxMethod, nameof(taxMethod), 32).ToLowerInvariant();
        TaxTreatment = Normalize(taxTreatment, nameof(taxTreatment), 32).ToLowerInvariant();
        TaxRate = decimal.Round(taxRate, 6, MidpointRounding.ToEven);
        NetAmount = decimal.Round(netAmount, 6, MidpointRounding.ToEven);
        TaxAmount = decimal.Round(taxAmount, 6, MidpointRounding.ToEven);
        RecoverableTaxAmount = decimal.Round(recoverableTaxAmount, 6, MidpointRounding.ToEven);
        NonRecoverableTaxAmount = decimal.Round(nonRecoverableTaxAmount, 6, MidpointRounding.ToEven);
        GrossAmount = decimal.Round(grossAmount, 6, MidpointRounding.ToEven);
        CostBaseAmount = decimal.Round(costBaseAmount, 6, MidpointRounding.ToEven);
        RecoverableTaxBaseAmount = decimal.Round(recoverableTaxBaseAmount, 6, MidpointRounding.ToEven);
        RecoverableTaxAccountId = recoverableTaxAccountId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int Sequence { get; private set; }
    public string Description { get; private set; } = null!;
    public Guid CostAccountId { get; private set; }
    public string AccountClassification { get; private set; } = null!;
    public string TaxRuleKey { get; private set; } = null!;
    public string TaxMethod { get; private set; } = null!;
    public string TaxTreatment { get; private set; } = null!;
    public decimal TaxRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal RecoverableTaxAmount { get; private set; }
    public decimal NonRecoverableTaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CostBaseAmount { get; private set; }
    public decimal RecoverableTaxBaseAmount { get; private set; }
    public Guid? RecoverableTaxAccountId { get; private set; }
    public SupplierBillAccountingProfile Profile { get; private set; } = null!;

    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }
}
