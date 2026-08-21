using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public static class CustomerInvoiceAccountingStatuses
{
    public const string NotReady = "not_ready";
    public const string AwaitingApproval = "awaiting_approval";
    public const string ReadyToPost = "ready_to_post";
    public const string Posted = "posted";
    public const string Reversed = "reversed";
    public const string Blocked = "blocked";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Customer invoice accounting status is required.", nameof(value))
            : value.Trim().Replace('-', '_').ToLowerInvariant() switch
            {
                NotReady => NotReady,
                AwaitingApproval => AwaitingApproval,
                ReadyToPost => ReadyToPost,
                Posted => Posted,
                Reversed => Reversed,
                Blocked => Blocked,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Customer invoice accounting status is not supported.")
            };
}

public sealed class CustomerInvoiceAccountingProfile : ICompanyOwnedEntity
{
    private CustomerInvoiceAccountingProfile() { }

    public CustomerInvoiceAccountingProfile(
        Guid id,
        Guid companyId,
        Guid invoiceId,
        Guid fiscalPeriodId,
        string voucherSeriesCode,
        string documentCurrency,
        string baseCurrency,
        decimal exchangeRate,
        decimal netAmount,
        decimal taxAmount,
        decimal grossAmount,
        decimal netBaseAmount,
        decimal taxBaseAmount,
        decimal grossBaseAmount,
        decimal roundingBaseAmount,
        Guid receivableAccountId,
        Guid revenueAccountId,
        string taxMethod,
        string policyPackKey,
        string policyPackVersion,
        string policyDefinitionHash,
        Guid? originalInvoiceId,
        Guid actorUserId,
        DateTime nowUtc)
    {
        if (companyId == Guid.Empty || invoiceId == Guid.Empty || fiscalPeriodId == Guid.Empty || actorUserId == Guid.Empty)
            throw new ArgumentException("Company, invoice, period, and actor are required.");
        if (exchangeRate <= 0m || netAmount < 0m || taxAmount < 0m || grossAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(grossAmount), "Invoice accounting amounts and exchange rate must be positive.");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        InvoiceId = invoiceId;
        CreatedUtc = NormalizeUtc(nowUtc);
        Version = 1;
        ApplyFacts(fiscalPeriodId, voucherSeriesCode, documentCurrency, baseCurrency, exchangeRate,
            netAmount, taxAmount, grossAmount, netBaseAmount, taxBaseAmount, grossBaseAmount,
            roundingBaseAmount, receivableAccountId, revenueAccountId, taxMethod, policyPackKey, policyPackVersion, policyDefinitionHash,
            originalInvoiceId, actorUserId, nowUtc, incrementVersion: false);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string VoucherSeriesCode { get; private set; } = null!;
    public string DocumentCurrency { get; private set; } = null!;
    public string BaseCurrency { get; private set; } = null!;
    public decimal ExchangeRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal NetBaseAmount { get; private set; }
    public decimal TaxBaseAmount { get; private set; }
    public decimal GrossBaseAmount { get; private set; }
    public decimal RoundingBaseAmount { get; private set; }
    public Guid ReceivableAccountId { get; private set; }
    public Guid RevenueAccountId { get; private set; }
    public string TaxMethod { get; private set; } = null!;
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string PolicyDefinitionHash { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string PayloadHash { get; private set; } = string.Empty;
    public long Version { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public Guid? OriginalInvoiceId { get; private set; }
    public string? BlockingReasonCode { get; private set; }
    public string? BlockingReason { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public FinanceInvoice Invoice { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public LedgerEntry? LedgerEntry { get; private set; }
    public FinanceInvoice? OriginalInvoice { get; private set; }
    public ICollection<CustomerInvoiceAccountingLine> Lines { get; } = new List<CustomerInvoiceAccountingLine>();

    public void ReplaceFacts(
        Guid fiscalPeriodId, string voucherSeriesCode, string documentCurrency, string baseCurrency,
        decimal exchangeRate, decimal netAmount, decimal taxAmount, decimal grossAmount,
        decimal netBaseAmount, decimal taxBaseAmount, decimal grossBaseAmount, decimal roundingBaseAmount,
        Guid receivableAccountId, Guid revenueAccountId,
        string taxMethod, string policyPackKey, string policyPackVersion, string policyDefinitionHash,
        Guid? originalInvoiceId, Guid actorUserId, DateTime nowUtc) =>
        ApplyFacts(fiscalPeriodId, voucherSeriesCode, documentCurrency, baseCurrency, exchangeRate,
            netAmount, taxAmount, grossAmount, netBaseAmount, taxBaseAmount, grossBaseAmount,
            roundingBaseAmount, receivableAccountId, revenueAccountId, taxMethod, policyPackKey, policyPackVersion, policyDefinitionHash,
            originalInvoiceId, actorUserId, nowUtc, incrementVersion: true);

    public void SetPayloadHash(string payloadHash)
    {
        PayloadHash = NormalizeRequired(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();
    }

    public void BindApproval(Guid approvalRequestId, Guid actorUserId, DateTime nowUtc)
    {
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        ApprovalRequestId = approvalRequestId;
        Status = CustomerInvoiceAccountingStatuses.AwaitingApproval;
        BlockingReasonCode = null;
        BlockingReason = null;
        Touch(actorUserId, nowUtc);
    }

    public void MarkReady(Guid actorUserId, DateTime nowUtc)
    {
        Status = CustomerInvoiceAccountingStatuses.ReadyToPost;
        BlockingReasonCode = null;
        BlockingReason = null;
        Touch(actorUserId, nowUtc);
    }

    public void MarkBlocked(string reasonCode, string reason, Guid actorUserId, DateTime nowUtc)
    {
        Status = CustomerInvoiceAccountingStatuses.Blocked;
        BlockingReasonCode = NormalizeRequired(reasonCode, nameof(reasonCode), 96);
        BlockingReason = NormalizeRequired(reason, nameof(reason), 1000);
        Touch(actorUserId, nowUtc);
    }

    public void MarkPosted(Guid ledgerEntryId, Guid actorUserId, DateTime nowUtc)
    {
        if (ledgerEntryId == Guid.Empty) throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        LedgerEntryId = ledgerEntryId;
        Status = CustomerInvoiceAccountingStatuses.Posted;
        BlockingReasonCode = null;
        BlockingReason = null;
        Touch(actorUserId, nowUtc);
    }

    public void MarkReversed(Guid actorUserId, DateTime nowUtc)
    {
        Status = CustomerInvoiceAccountingStatuses.Reversed;
        Touch(actorUserId, nowUtc);
    }

    private void ApplyFacts(
        Guid fiscalPeriodId, string voucherSeriesCode, string documentCurrency, string baseCurrency,
        decimal exchangeRate, decimal netAmount, decimal taxAmount, decimal grossAmount,
        decimal netBaseAmount, decimal taxBaseAmount, decimal grossBaseAmount, decimal roundingBaseAmount,
        Guid receivableAccountId, Guid revenueAccountId,
        string taxMethod, string policyPackKey, string policyPackVersion, string policyDefinitionHash,
        Guid? originalInvoiceId, Guid actorUserId, DateTime nowUtc, bool incrementVersion)
    {
        if (fiscalPeriodId == Guid.Empty || actorUserId == Guid.Empty) throw new ArgumentException("Period and actor are required.");
        FiscalPeriodId = fiscalPeriodId;
        VoucherSeriesCode = NormalizeRequired(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        DocumentCurrency = NormalizeCurrency(documentCurrency);
        BaseCurrency = NormalizeCurrency(baseCurrency);
        ExchangeRate = exchangeRate > 0m ? exchangeRate : throw new ArgumentOutOfRangeException(nameof(exchangeRate));
        NetAmount = NormalizeAmount(netAmount);
        TaxAmount = NormalizeAmount(taxAmount);
        GrossAmount = NormalizePositive(grossAmount);
        NetBaseAmount = NormalizeAmount(netBaseAmount);
        TaxBaseAmount = NormalizeAmount(taxBaseAmount);
        GrossBaseAmount = NormalizePositive(grossBaseAmount);
        RoundingBaseAmount = decimal.Round(roundingBaseAmount, 6, MidpointRounding.ToEven);
        ReceivableAccountId = receivableAccountId == Guid.Empty ? throw new ArgumentException("ReceivableAccountId is required.", nameof(receivableAccountId)) : receivableAccountId;
        RevenueAccountId = revenueAccountId == Guid.Empty ? throw new ArgumentException("RevenueAccountId is required.", nameof(revenueAccountId)) : revenueAccountId;
        TaxMethod = NormalizeRequired(taxMethod, nameof(taxMethod), 32).ToLowerInvariant();
        PolicyPackKey = NormalizeRequired(policyPackKey, nameof(policyPackKey), 96).ToLowerInvariant();
        PolicyPackVersion = NormalizeRequired(policyPackVersion, nameof(policyPackVersion), 32);
        PolicyDefinitionHash = NormalizeRequired(policyDefinitionHash, nameof(policyDefinitionHash), 64).ToLowerInvariant();
        OriginalInvoiceId = originalInvoiceId;
        Status = CustomerInvoiceAccountingStatuses.NotReady;
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
}

public sealed class CustomerInvoiceAccountingLine : ICompanyOwnedEntity
{
    private CustomerInvoiceAccountingLine() { }

    public CustomerInvoiceAccountingLine(
        Guid id, Guid companyId, Guid profileId, int sequence, string description,
        string taxRuleKey, string taxMethod, decimal taxRate, decimal netAmount,
        decimal taxAmount, decimal grossAmount, decimal netBaseAmount, decimal taxBaseAmount, Guid? taxPayableAccountId)
    {
        if (companyId == Guid.Empty || profileId == Guid.Empty || sequence < 1) throw new ArgumentException("Company, profile, and sequence are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ProfileId = profileId;
        Sequence = sequence;
        Description = Normalize(description, nameof(description), 500);
        TaxRuleKey = Normalize(taxRuleKey, nameof(taxRuleKey), 96).ToLowerInvariant();
        TaxMethod = Normalize(taxMethod, nameof(taxMethod), 32).ToLowerInvariant();
        TaxRate = decimal.Round(taxRate, 6, MidpointRounding.ToEven);
        NetAmount = decimal.Round(netAmount, 6, MidpointRounding.ToEven);
        TaxAmount = decimal.Round(taxAmount, 6, MidpointRounding.ToEven);
        GrossAmount = decimal.Round(grossAmount, 6, MidpointRounding.ToEven);
        NetBaseAmount = decimal.Round(netBaseAmount, 6, MidpointRounding.ToEven);
        TaxBaseAmount = decimal.Round(taxBaseAmount, 6, MidpointRounding.ToEven);
        TaxPayableAccountId = taxPayableAccountId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int Sequence { get; private set; }
    public string Description { get; private set; } = null!;
    public string TaxRuleKey { get; private set; } = null!;
    public string TaxMethod { get; private set; } = null!;
    public decimal TaxRate { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal NetBaseAmount { get; private set; }
    public decimal TaxBaseAmount { get; private set; }
    public Guid? TaxPayableAccountId { get; private set; }
    public CustomerInvoiceAccountingProfile Profile { get; private set; } = null!;

    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalized;
    }
}
