using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
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
        Guid? sourceSimulationEventRecordId = null,
        string? postingStatus = null,
        string? dueStatus = null,
        string? documentKind = null,
        string? providerStatus = null,
        string? processingStatus = null,
        decimal paidAmount = 0m)
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
        PaidAmount = NormalizePaidAmount(paidAmount, amount);
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Status = NormalizeRequired(status, nameof(status), 32);
        SettlementStatus = ResolveInitialSettlementStatus(status, settlementStatus);
        PostingStatus = ResolveInitialPostingStatus(status, postingStatus);
        DueStatus = ResolveInitialDueStatus(DueUtc, SettlementStatus, dueStatus);
        DocumentKind = NormalizeDocumentKind(documentKind ?? FinanceDocumentKinds.Invoice);
        ProviderStatus = NormalizeOptional(providerStatus, nameof(providerStatus), 128);
        ProcessingStatus = ResolveInitialProcessingStatus(processingStatus);
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
    public decimal PaidAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string SettlementStatus { get; private set; } = null!;
    public string PostingStatus { get; private set; } = null!;
    public string DueStatus { get; private set; } = null!;
    public string DocumentKind { get; private set; } = null!;
    public string? ProviderStatus { get; private set; }
    public string ProcessingStatus { get; private set; } = null!;
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

    public void ApplySyncedSnapshot(
        Guid counterpartyId,
        DateTime issuedUtc,
        DateTime dueUtc,
        decimal amount,
        string currency,
        string status,
        string settlementStatus,
        string? postingStatus = null,
        string? dueStatus = null,
        string? documentKind = null,
        string? providerStatus = null,
        string? processingStatus = null,
        decimal? paidAmount = null)
    {
        CounterpartyId = counterpartyId == Guid.Empty ? throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId)) : counterpartyId;
        IssuedUtc = EntityTimestampNormalizer.NormalizeUtc(issuedUtc, nameof(issuedUtc));
        DueUtc = EntityTimestampNormalizer.NormalizeUtc(dueUtc, nameof(dueUtc));
        Amount = amount;
        if (paidAmount.HasValue)
        {
            PaidAmount = NormalizePaidAmount(paidAmount.Value, amount);
        }
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Status = NormalizeRequired(status, nameof(status), 32);
        SettlementStatus = NormalizeSettlementStatus(settlementStatus);
        PostingStatus = postingStatus is null
            ? ResolvePostingStatus(Status)
            : NormalizePostingStatus(postingStatus);
        DueStatus = dueStatus is null
            ? ResolveDueStatus(DueUtc, SettlementStatus)
            : NormalizeDueStatus(dueStatus);
        DocumentKind = documentKind is null
            ? ResolveDocumentKind(FinanceDocumentKinds.Invoice, Amount)
            : NormalizeDocumentKind(documentKind);
        ProviderStatus = NormalizeOptional(providerStatus, nameof(providerStatus), 128);
        ProcessingStatus = ResolveInitialProcessingStatus(processingStatus);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ChangeApprovalStatus(string status)
    {
        var normalized = NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        if (!IsValidApprovalStatusTransition(Status, normalized))
        {
            throw new InvalidOperationException($"Invoice status cannot transition from '{Status}' to '{normalized}'.");
        }

        Status = normalized;
        PostingStatus = ResolvePostingStatus(normalized);
        DueStatus = ResolveDueStatus(DueUtc, SettlementStatus);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ApplySettlementStatus(string settlementStatus)
    {
        SettlementStatus = NormalizeSettlementStatus(settlementStatus);
        DueStatus = ResolveDueStatus(DueUtc, SettlementStatus);
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
            ("pending_approval", "open" or "approved" or "rejected") => true,
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

    private static decimal NormalizePaidAmount(decimal paidAmount, decimal totalAmount)
    {
        var normalized = decimal.Round(Math.Abs(paidAmount), 2, MidpointRounding.AwayFromZero);
        var cap = decimal.Round(Math.Abs(totalAmount), 2, MidpointRounding.AwayFromZero);
        return cap == 0m ? 0m : Math.Min(normalized, cap);
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

    private static string ResolveInitialPostingStatus(string status, string? postingStatus) =>
        string.IsNullOrWhiteSpace(postingStatus)
            ? ResolvePostingStatus(status)
            : NormalizePostingStatus(postingStatus);

    private static string ResolvePostingStatus(string status)
    {
        var normalized = NormalizeRequired(status, nameof(status), 32).Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return normalized switch
        {
            "draft" or "unbooked" or "pending" or "pending_approval" => FinanceDocumentPostingStatuses.Draft,
            "cancelled" or "canceled" or "void" or "rejected" => FinanceDocumentPostingStatuses.Cancelled,
            _ => FinanceDocumentPostingStatuses.Booked
        };
    }

    private static string NormalizePostingStatus(string value)
    {
        var normalized = FinanceDocumentPostingStatuses.Normalize(value);
        return FinanceDocumentPostingStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported document posting status.");
    }

    private static string ResolveInitialDueStatus(DateTime dueUtc, string settlementStatus, string? dueStatus) =>
        string.IsNullOrWhiteSpace(dueStatus)
            ? ResolveDueStatus(dueUtc, settlementStatus)
            : NormalizeDueStatus(dueStatus);

    private static string ResolveDueStatus(DateTime dueUtc, string settlementStatus)
    {
        if (string.Equals(settlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(settlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase))
        {
            return FinanceDocumentDueStatuses.NotDue;
        }

        var dueDate = dueUtc.Date;
        var today = DateTime.UtcNow.Date;
        if (dueDate < today)
        {
            return FinanceDocumentDueStatuses.Overdue;
        }

        return dueDate <= today.AddDays(7)
            ? FinanceDocumentDueStatuses.DueSoon
            : FinanceDocumentDueStatuses.NotDue;
    }

    private static string NormalizeDueStatus(string value)
    {
        var normalized = FinanceDocumentDueStatuses.Normalize(value);
        return FinanceDocumentDueStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported document due status.");
    }

    private static string ResolveDocumentKind(string defaultKind, decimal amount) =>
        amount < 0m && string.Equals(defaultKind, FinanceDocumentKinds.Invoice, StringComparison.OrdinalIgnoreCase)
            ? FinanceDocumentKinds.CreditNote
            : amount < 0m && string.Equals(defaultKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase)
                ? FinanceDocumentKinds.SupplierCreditNote
                : defaultKind;

    private static string ResolveInitialProcessingStatus(string? processingStatus) =>
        string.IsNullOrWhiteSpace(processingStatus)
            ? FinanceDocumentProcessingStatuses.None
            : NormalizeProcessingStatus(processingStatus);

    private static string NormalizeProcessingStatus(string value)
    {
        var normalized = FinanceDocumentProcessingStatuses.Normalize(value);
        return FinanceDocumentProcessingStatuses.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported document processing status.");
    }

    private static string NormalizeDocumentKind(string value)
    {
        var normalized = FinanceDocumentKinds.Normalize(value);
        return FinanceDocumentKinds.IsSupported(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported finance document kind.");
    }
}

