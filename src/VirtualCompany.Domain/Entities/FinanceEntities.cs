using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAccount : ICompanyOwnedEntity
{
    private FinanceAccount()
    {
    }

    public FinanceAccount(
        Guid id,
        Guid companyId,
        string code,
        string name,
        string accountType,
        string currency,
        decimal openingBalance,
        DateTime openedUtc,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Code = NormalizeRequired(code, nameof(code), 32);
        Name = NormalizeRequired(name, nameof(name), 160);
        AccountType = NormalizeRequired(accountType, nameof(accountType), 64);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        OpeningBalance = openingBalance;
        OpenedUtc = EntityTimestampNormalizer.NormalizeUtc(openedUtc, nameof(openedUtc));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? OpenedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string AccountType { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal OpeningBalance { get; private set; }
    public DateTime OpenedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceBalance> Balances { get; } = new List<FinanceBalance>();
    public ICollection<FinancialStatementMapping> FinancialStatementMappings { get; } = new List<FinancialStatementMapping>();

    public void Rename(string name)
    {
        Name = NormalizeRequired(name, nameof(name), 160);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

}

public sealed class FinanceCounterparty : ICompanyOwnedEntity
{
    private FinanceCounterparty()
    {
    }

    public FinanceCounterparty(
        Guid id,
        Guid companyId,
        string name,
        string counterpartyType,
        string? email = null,
        string? paymentTerms = null,
        string? taxId = null,
        decimal? creditLimit = null,
        string? preferredPaymentMethod = null,
        string? defaultAccountMapping = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyType = NormalizeCounterpartyType(counterpartyType);
        Name = NormalizeRequired(name, nameof(name), 200);
        Email = NormalizeOptional(email, nameof(email), 256);
        PaymentTerms = NormalizeOptionalOrDefault(paymentTerms, nameof(paymentTerms), 64, ResolveDefaultPaymentTerms(CounterpartyType));
        TaxId = NormalizeOptional(taxId, nameof(taxId), 64);
        CreditLimit = NormalizeCreditLimit(creditLimit, nameof(creditLimit));
        PreferredPaymentMethod = NormalizeOptionalOrDefault(preferredPaymentMethod, nameof(preferredPaymentMethod), 64, DefaultPreferredPaymentMethod);
        DefaultAccountMapping = NormalizeOptionalOrDefault(defaultAccountMapping, nameof(defaultAccountMapping), 64, ResolveDefaultAccountMapping(CounterpartyType));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string CounterpartyType { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? TaxId { get; private set; }
    public decimal? CreditLimit { get; private set; }
    public string? PreferredPaymentMethod { get; private set; }
    public string? DefaultAccountMapping { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceInvoice> Invoices { get; } = new List<FinanceInvoice>();
    public ICollection<FinanceBill> Bills { get; } = new List<FinanceBill>();

    public void UpdateMasterData(
        string name,
        string counterpartyType,
        string? email = null,
        string? paymentTerms = null,
        string? taxId = null,
        decimal? creditLimit = null,
        string? preferredPaymentMethod = null,
        string? defaultAccountMapping = null)
    {
        CounterpartyType = NormalizeCounterpartyType(counterpartyType);
        Name = NormalizeRequired(name, nameof(name), 200);
        Email = NormalizeOptional(email, nameof(email), 256);
        PaymentTerms = NormalizeOptionalOrDefault(paymentTerms, nameof(paymentTerms), 64, ResolveDefaultPaymentTerms(CounterpartyType));
        TaxId = NormalizeOptional(taxId, nameof(taxId), 64);
        CreditLimit = NormalizeCreditLimit(creditLimit, nameof(creditLimit));
        PreferredPaymentMethod = NormalizeOptionalOrDefault(preferredPaymentMethod, nameof(preferredPaymentMethod), 64, DefaultPreferredPaymentMethod);
        DefaultAccountMapping = NormalizeOptionalOrDefault(defaultAccountMapping, nameof(defaultAccountMapping), 64, ResolveDefaultAccountMapping(CounterpartyType));
        UpdatedUtc = DateTime.UtcNow;
    }

    private const string DefaultPreferredPaymentMethod = "bank_transfer";

    private static string NormalizeOptionalOrDefault(string? value, string name, int maxLength, string fallback)
    {
        var normalized = NormalizeOptional(value, name, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string ResolveDefaultPaymentTerms(string counterpartyType) =>
        counterpartyType switch
        {
            "customer" => "Net30",
            "supplier" => "Net30",
            _ => "Net30"
        };

    private static string ResolveDefaultAccountMapping(string counterpartyType) =>
        counterpartyType switch
        {
            "customer" => "1100",
            "supplier" => "2000",
            _ => "2000"
        };

    public static string NormalizeCounterpartyKind(string value) =>
        NormalizeCounterpartyType(value) switch
        {
            "supplier" => "supplier",
            _ => "customer"
        };

    private static string NormalizeCounterpartyType(string value) =>
        NormalizeRequired(value, nameof(value), 64).ToLowerInvariant() switch
        {
            "vendor" => "supplier",
            var normalized => normalized
        };

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static decimal NormalizeCreditLimit(decimal? value, string name)
    {
        var normalized = value ?? 0m;
        if (normalized < 0m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} cannot be negative.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinanceTransaction : ICompanyOwnedEntity
{
    private FinanceTransaction()
    {
    }

    public FinanceTransaction(
        Guid id,
        Guid companyId,
        Guid accountId,
        Guid? counterpartyId,
        Guid? invoiceId,
        Guid? billId,
        DateTime transactionUtc,
        string transactionType,
        decimal amount,
        string currency,
        string description,
        string externalReference,
        Guid? documentId = null,
        DateTime? createdUtc = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("AccountId is required.", nameof(accountId));
        }

        if (counterpartyId == Guid.Empty)
        {
            throw new ArgumentException("CounterpartyId cannot be empty.", nameof(counterpartyId));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("InvoiceId cannot be empty.", nameof(invoiceId));
        }

        if (billId == Guid.Empty)
        {
            throw new ArgumentException("BillId cannot be empty.", nameof(billId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AccountId = accountId;
        CounterpartyId = counterpartyId;
        InvoiceId = invoiceId;
        BillId = billId;
        DocumentId = documentId;
        TransactionUtc = EntityTimestampNormalizer.NormalizeUtc(transactionUtc, nameof(transactionUtc));
        TransactionType = NormalizeRequired(transactionType, nameof(transactionType), 64);
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Description = NormalizeRequired(description, nameof(description), 500);
        ExternalReference = NormalizeRequired(externalReference, nameof(externalReference), 100);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? TransactionUtc, nameof(createdUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? CounterpartyId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public Guid? BillId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public DateTime TransactionUtc { get; private set; }
    public string TransactionType { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string ExternalReference { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount Account { get; private set; } = null!;
    public FinanceCounterparty? Counterparty { get; private set; }
    public FinanceInvoice? Invoice { get; private set; }
    public FinanceBill? Bill { get; private set; }
    public CompanyKnowledgeDocument? Document { get; private set; }

    public void ChangeCategory(string category)
    {
        TransactionType = NormalizeRequired(category, nameof(category), 64);
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinanceInvoice : ICompanyOwnedEntity
{
    private FinanceInvoice()
    {
    }

    public FinanceInvoice(
        Guid id,
        Guid companyId,
        Guid counterpartyId,
        string invoiceNumber,
        DateTime issuedUtc,
        DateTime dueUtc,
        decimal amount,
        string currency,
        string status,
        Guid? documentId = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        string? settlementStatus = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (counterpartyId == Guid.Empty)
        {
            throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyId = counterpartyId;
        InvoiceNumber = NormalizeRequired(invoiceNumber, nameof(invoiceNumber), 64);
        IssuedUtc = EntityTimestampNormalizer.NormalizeUtc(issuedUtc, nameof(issuedUtc));
        DueUtc = EntityTimestampNormalizer.NormalizeUtc(dueUtc, nameof(dueUtc));
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Status = NormalizeRequired(status, nameof(status), 32);
        SettlementStatus = ResolveInitialSettlementStatus(status, settlementStatus);
        DocumentId = documentId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? IssuedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public DateTime IssuedUtc { get; private set; }
    public DateTime DueUtc { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string SettlementStatus { get; private set; } = null!;
    public Guid? DocumentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceCounterparty Counterparty { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<PaymentAllocation> Allocations { get; } = new List<PaymentAllocation>();
    public CompanyKnowledgeDocument? Document { get; private set; }

    public void ChangeApprovalStatus(string status)
    {
        var normalized = NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        if (!IsValidApprovalStatusTransition(Status, normalized))
        {
            throw new InvalidOperationException($"Invoice status cannot transition from '{Status}' to '{normalized}'.");
        }

        Status = normalized;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ApplySettlementStatus(string settlementStatus)
    {
        SettlementStatus = NormalizeSettlementStatus(settlementStatus);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static bool IsValidApprovalStatusTransition(string current, string next)
    {
        var normalizedCurrent = NormalizeRequired(current, nameof(current), 32).ToLowerInvariant();
        if (string.Equals(normalizedCurrent, next, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (normalizedCurrent, next) switch
        {
            ("open", "pending_approval" or "approved" or "rejected") => true,
            ("pending", "pending_approval" or "approved" or "rejected") => true,
            ("pending_approval", "approved" or "rejected") => true,
            ("approved", "paid" or "void") => true,
            ("rejected", "open" or "void") => true,
            _ => false
        };
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string ResolveInitialSettlementStatus(string status, string? settlementStatus)
    {
        if (!string.IsNullOrWhiteSpace(settlementStatus))
        {
            return NormalizeSettlementStatus(settlementStatus);
        }

        return string.Equals(status?.Trim(), "paid", StringComparison.OrdinalIgnoreCase)
            ? FinanceSettlementStatuses.Paid
            : FinanceSettlementStatuses.Unpaid;
    }

    private static string NormalizeSettlementStatus(string value)
    {
        var normalized = FinanceSettlementStatuses.Normalize(value);
        return FinanceSettlementStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported settlement status.");
    }
}

public sealed class FinanceBill : ICompanyOwnedEntity
{
    private FinanceBill()
    {
    }

    public FinanceBill(
        Guid id,
        Guid companyId,
        Guid counterpartyId,
        string billNumber,
        DateTime receivedUtc,
        DateTime dueUtc,
        decimal amount,
        string currency,
        string status,
        Guid? documentId = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        string? settlementStatus = null,
        Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (counterpartyId == Guid.Empty)
        {
            throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId));
        }

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId cannot be empty.", nameof(documentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyId = counterpartyId;
        BillNumber = NormalizeRequired(billNumber, nameof(billNumber), 64);
        ReceivedUtc = EntityTimestampNormalizer.NormalizeUtc(receivedUtc, nameof(receivedUtc));
        DueUtc = EntityTimestampNormalizer.NormalizeUtc(dueUtc, nameof(dueUtc));
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Status = NormalizeRequired(status, nameof(status), 32);
        SettlementStatus = ResolveInitialSettlementStatus(status, settlementStatus);
        DocumentId = documentId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? ReceivedUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string BillNumber { get; private set; } = null!;
    public DateTime ReceivedUtc { get; private set; }
    public DateTime DueUtc { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string SettlementStatus { get; private set; } = null!;
    public Guid? DocumentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceCounterparty Counterparty { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<PaymentAllocation> Allocations { get; } = new List<PaymentAllocation>();
    public CompanyKnowledgeDocument? Document { get; private set; }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public void ApplySettlementStatus(string settlementStatus)
    {
        SettlementStatus = NormalizeSettlementStatus(settlementStatus);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string ResolveInitialSettlementStatus(string status, string? settlementStatus)
    {
        if (!string.IsNullOrWhiteSpace(settlementStatus))
        {
            return NormalizeSettlementStatus(settlementStatus);
        }

        return string.Equals(status?.Trim(), "paid", StringComparison.OrdinalIgnoreCase)
            ? FinanceSettlementStatuses.Paid
            : FinanceSettlementStatuses.Unpaid;
    }

    private static string NormalizeSettlementStatus(string value) =>
        FinanceSettlementStatuses.Normalize(value) is var normalized && FinanceSettlementStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported settlement status.");
}

public sealed class FinanceBalance : ICompanyOwnedEntity
{
    private FinanceBalance()
    {
    }

    public FinanceBalance(Guid id, Guid companyId, Guid accountId, DateTime asOfUtc, decimal amount, string currency,
        DateTime? createdUtc = null, Guid? sourceSimulationEventRecordId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("AccountId is required.", nameof(accountId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AccountId = accountId;
        AsOfUtc = EntityTimestampNormalizer.NormalizeUtc(asOfUtc, nameof(asOfUtc));
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? AsOfUtc, nameof(createdUtc));
        if (sourceSimulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SourceSimulationEventRecordId cannot be empty.", nameof(sourceSimulationEventRecordId));
        }

        SourceSimulationEventRecordId = sourceSimulationEventRecordId;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AccountId { get; private set; }
    public DateTime AsOfUtc { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Guid? SourceSimulationEventRecordId { get; private set; }
    public SimulationEventRecord? SourceSimulationEventRecord { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount Account { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinancePolicyConfiguration : ICompanyOwnedEntity
{
    private FinancePolicyConfiguration()
    {
    }

    public FinancePolicyConfiguration(
        Guid id,
        Guid companyId,
        string approvalCurrency,
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        bool requireCounterpartyForTransactions,
        decimal anomalyDetectionLowerBound = -10000m,
        decimal anomalyDetectionUpperBound = 10000m,
        int cashRunwayWarningThresholdDays = 90,
        int cashRunwayCriticalThresholdDays = 30)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        ValidateControls(
            invoiceApprovalThreshold,
            billApprovalThreshold,
            anomalyDetectionLowerBound,
            anomalyDetectionUpperBound,
            cashRunwayWarningThresholdDays,
            cashRunwayCriticalThresholdDays);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ApprovalCurrency = NormalizeRequired(approvalCurrency, nameof(approvalCurrency), 3).ToUpperInvariant();
        InvoiceApprovalThreshold = invoiceApprovalThreshold;
        BillApprovalThreshold = billApprovalThreshold;
        RequireCounterpartyForTransactions = requireCounterpartyForTransactions;
        AnomalyDetectionLowerBound = anomalyDetectionLowerBound;
        AnomalyDetectionUpperBound = anomalyDetectionUpperBound;
        CashRunwayWarningThresholdDays = cashRunwayWarningThresholdDays;
        CashRunwayCriticalThresholdDays = cashRunwayCriticalThresholdDays;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ApprovalCurrency { get; private set; } = null!;
    public decimal InvoiceApprovalThreshold { get; private set; }
    public decimal BillApprovalThreshold { get; private set; }
    public bool RequireCounterpartyForTransactions { get; private set; }
    public decimal AnomalyDetectionLowerBound { get; private set; }
    public decimal AnomalyDetectionUpperBound { get; private set; }
    public int CashRunwayWarningThresholdDays { get; private set; }
    public int CashRunwayCriticalThresholdDays { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Update(
        string approvalCurrency,
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        bool requireCounterpartyForTransactions,
        decimal anomalyDetectionLowerBound,
        decimal anomalyDetectionUpperBound,
        int cashRunwayWarningThresholdDays,
        int cashRunwayCriticalThresholdDays)
    {
        ValidateControls(
            invoiceApprovalThreshold,
            billApprovalThreshold,
            anomalyDetectionLowerBound,
            anomalyDetectionUpperBound,
            cashRunwayWarningThresholdDays,
            cashRunwayCriticalThresholdDays);

        ApprovalCurrency = NormalizeRequired(approvalCurrency, nameof(approvalCurrency), 3).ToUpperInvariant();
        InvoiceApprovalThreshold = invoiceApprovalThreshold;
        BillApprovalThreshold = billApprovalThreshold;
        RequireCounterpartyForTransactions = requireCounterpartyForTransactions;
        AnomalyDetectionLowerBound = anomalyDetectionLowerBound;
        AnomalyDetectionUpperBound = anomalyDetectionUpperBound;
        CashRunwayWarningThresholdDays = cashRunwayWarningThresholdDays;
        CashRunwayCriticalThresholdDays = cashRunwayCriticalThresholdDays;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static void ValidateControls(
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        decimal anomalyDetectionLowerBound,
        decimal anomalyDetectionUpperBound,
        int cashRunwayWarningThresholdDays,
        int cashRunwayCriticalThresholdDays)
    {
        if (invoiceApprovalThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(invoiceApprovalThreshold), "Invoice approval threshold cannot be negative.");
        }

        if (billApprovalThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(billApprovalThreshold), "Bill approval threshold cannot be negative.");
        }

        if (anomalyDetectionLowerBound >= anomalyDetectionUpperBound)
        {
            throw new ArgumentException("Anomaly detection lower bound must be less than upper bound.", nameof(anomalyDetectionLowerBound));
        }

        if (cashRunwayCriticalThresholdDays <= 0 || cashRunwayWarningThresholdDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cashRunwayWarningThresholdDays), "Cash runway thresholds must be positive.");
        }

        if (cashRunwayCriticalThresholdDays > cashRunwayWarningThresholdDays)
        {
            throw new ArgumentException("Cash runway critical threshold cannot exceed warning threshold.", nameof(cashRunwayCriticalThresholdDays));
        }
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

internal static class EntityTimestampNormalizer
{
    public static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

public sealed class FinanceSeedAnomaly : ICompanyOwnedEntity
{
    private FinanceSeedAnomaly()
    {
    }

    public FinanceSeedAnomaly(
        Guid id,
        Guid companyId,
        string anomalyType,
        string scenarioProfile,
        IReadOnlyCollection<Guid> affectedRecordIds,
        string expectedDetectionMetadataJson)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (affectedRecordIds.Count == 0)
        {
            throw new ArgumentException("At least one affected record id is required.", nameof(affectedRecordIds));
        }

        if (affectedRecordIds.Any(x => x == Guid.Empty))
        {
            throw new ArgumentException("Affected record ids cannot contain empty values.", nameof(affectedRecordIds));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AnomalyType = NormalizeRequired(anomalyType, nameof(anomalyType), 64);
        ScenarioProfile = NormalizeRequired(scenarioProfile, nameof(scenarioProfile), 64);
        AffectedRecordIdsJson = new JsonArray(affectedRecordIds.Select(id => JsonValue.Create(id)).Cast<JsonNode?>().ToArray()).ToJsonString();
        ExpectedDetectionMetadataJson = NormalizeRequired(expectedDetectionMetadataJson, nameof(expectedDetectionMetadataJson), 4000);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string AnomalyType { get; private set; } = null!;
    public string ScenarioProfile { get; private set; } = null!;
    public string AffectedRecordIdsJson { get; private set; } = null!;
    public string ExpectedDetectionMetadataJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public IReadOnlyList<Guid> GetAffectedRecordIds() =>
        JsonNode.Parse(AffectedRecordIdsJson)?.AsArray()
            .Select(x => x?.GetValue<Guid>() ?? Guid.Empty)
            .Where(x => x != Guid.Empty)
            .ToArray() ?? [];

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class FinanceSimulationStepLog : ICompanyOwnedEntity
{
    private FinanceSimulationStepLog()
    {
    }

    public FinanceSimulationStepLog(
        Guid id,
        Guid companyId,
        Guid runId,
        int stepNumber,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        int executionStepHours,
        int totalHoursProcessed,
        bool isAccelerated,
        int transactionsGenerated,
        int invoicesGenerated,
        int billsGenerated,
        int recurringExpenseInstancesGenerated,
        int eventsEmitted,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId is required.", nameof(runId));
        }

        if (stepNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepNumber), "Step number must be positive.");
        }

        if (executionStepHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(executionStepHours), "Execution step hours must be positive.");
        }

        if (totalHoursProcessed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalHoursProcessed), "Total processed hours must be positive.");
        }

        var normalizedWindowStartUtc = EntityTimestampNormalizer.NormalizeUtc(windowStartUtc, nameof(windowStartUtc));
        var normalizedWindowEndUtc = EntityTimestampNormalizer.NormalizeUtc(windowEndUtc, nameof(windowEndUtc));
        if (normalizedWindowEndUtc <= normalizedWindowStartUtc)
        {
            throw new ArgumentException("Window end must be after window start.", nameof(windowEndUtc));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunId = runId;
        StepNumber = stepNumber;
        WindowStartUtc = normalizedWindowStartUtc;
        WindowEndUtc = normalizedWindowEndUtc;
        ExecutionStepHours = executionStepHours;
        TotalHoursProcessed = totalHoursProcessed;
        IsAccelerated = isAccelerated;
        TransactionsGenerated = Math.Max(0, transactionsGenerated);
        InvoicesGenerated = Math.Max(0, invoicesGenerated);
        BillsGenerated = Math.Max(0, billsGenerated);
        RecurringExpenseInstancesGenerated = Math.Max(0, recurringExpenseInstancesGenerated);
        EventsEmitted = Math.Max(0, eventsEmitted);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? normalizedWindowEndUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public int StepNumber { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public int ExecutionStepHours { get; private set; }
    public int TotalHoursProcessed { get; private set; }
    public bool IsAccelerated { get; private set; }
    public int TransactionsGenerated { get; private set; }
    public int InvoicesGenerated { get; private set; }
    public int BillsGenerated { get; private set; }
    public int RecurringExpenseInstancesGenerated { get; private set; }
    public int EventsEmitted { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}
