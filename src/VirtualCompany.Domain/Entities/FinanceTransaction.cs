using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
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

    public void ReassignCounterpartyForApprovedMerge(Guid targetCounterpartyId)
    {
        CounterpartyId = targetCounterpartyId == Guid.Empty
            ? throw new ArgumentException("Target counterparty id is required.", nameof(targetCounterpartyId))
            : targetCounterpartyId;
    }
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

    public void ApplySyncedSnapshot(
        Guid accountId,
        Guid? counterpartyId,
        Guid? invoiceId,
        Guid? billId,
        DateTime transactionUtc,
        string transactionType,
        decimal amount,
        string currency,
        string description,
        string externalReference)
    {
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

        AccountId = accountId;
        CounterpartyId = counterpartyId;
        InvoiceId = invoiceId;
        BillId = billId;
        TransactionUtc = EntityTimestampNormalizer.NormalizeUtc(transactionUtc, nameof(transactionUtc));
        TransactionType = NormalizeRequired(transactionType, nameof(transactionType), 64);
        Amount = amount;
        Currency = NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Description = NormalizeRequired(description, nameof(description), 500);
        ExternalReference = NormalizeRequired(externalReference, nameof(externalReference), 100);
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

