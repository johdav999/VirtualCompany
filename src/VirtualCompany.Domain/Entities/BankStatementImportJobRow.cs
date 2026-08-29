using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BankStatementImportJobRow : ICompanyOwnedEntity
{
    private BankStatementImportJobRow() { }

    public BankStatementImportJobRow(Guid id, Guid companyId, Guid jobId, int rowNumber, string rowIdentity,
        string rowHash, DateTime? bookingDateUtc, DateTime? valueDateUtc, decimal? amount, string? currency,
        string? referenceText, string? counterparty, string? externalReference, string outcome,
        string? issueCode, string? issueSeverity, string? issueMessage, string? paymentStatus, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        JobId = jobId == Guid.Empty ? throw new ArgumentException("JobId is required.", nameof(jobId)) : jobId;
        RowNumber = rowNumber > 0 ? rowNumber : throw new ArgumentOutOfRangeException(nameof(rowNumber));
        RowIdentity = Required(rowIdentity, nameof(rowIdentity), 128);
        RowHash = Required(rowHash, nameof(rowHash), 64).ToLowerInvariant();
        BookingDateUtc = bookingDateUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(bookingDateUtc.Value, nameof(bookingDateUtc)) : null;
        ValueDateUtc = valueDateUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(valueDateUtc.Value, nameof(valueDateUtc)) : null;
        Amount = amount;
        Currency = Optional(currency, 3)?.ToUpperInvariant();
        ReferenceText = Optional(referenceText, 500);
        Counterparty = Optional(counterparty, 240);
        ExternalReference = Optional(externalReference, 160);
        Outcome = Required(outcome, nameof(outcome), 32);
        IssueCode = Optional(issueCode, 64);
        IssueSeverity = Optional(issueSeverity, 16);
        IssueMessage = Optional(issueMessage, 500);
        PaymentStatus = Optional(paymentStatus, 64);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid JobId { get; private set; }
    public int RowNumber { get; private set; }
    public string RowIdentity { get; private set; } = null!;
    public string RowHash { get; private set; } = null!;
    public DateTime? BookingDateUtc { get; private set; }
    public DateTime? ValueDateUtc { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }
    public string? ReferenceText { get; private set; }
    public string? Counterparty { get; private set; }
    public string? ExternalReference { get; private set; }
    public string Outcome { get; private set; } = null!;
    public string? IssueCode { get; private set; }
    public string? IssueSeverity { get; private set; }
    public string? IssueMessage { get; private set; }
    public string? PaymentStatus { get; private set; }
    public string? ConflictDecision { get; private set; }
    public string? ConflictDecisionReason { get; private set; }
    public Guid? ImportedBankTransactionId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public BankStatementImportJob Job { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public BankTransaction? ImportedBankTransaction { get; private set; }

    public bool IsCommitCandidate => Outcome == BankStatementImportRowOutcomes.Accepted && ConflictDecision is null;

    public void MarkImported(Guid? transactionId, DateTime nowUtc)
    {
        Outcome = BankStatementImportRowOutcomes.Imported;
        ImportedBankTransactionId = transactionId;
        ProcessedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
    }

    public void Skip(string reason, DateTime nowUtc)
    {
        if (Outcome == BankStatementImportRowOutcomes.Imported) throw new InvalidOperationException("An imported row cannot be skipped.");
        ConflictDecision = BankStatementImportConflictDecisions.Skip;
        ConflictDecisionReason = Required(reason, nameof(reason), 500);
        Outcome = BankStatementImportRowOutcomes.Skipped;
        ProcessedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
    }

    public void MarkConflict(string message, DateTime nowUtc)
    {
        Outcome = BankStatementImportRowOutcomes.Error;
        IssueCode = "bank_row_identity_conflict";
        IssueSeverity = BankStatementImportIssueSeverities.Error;
        IssueMessage = Required(message, nameof(message), 500);
        ProcessedUtc = null;
    }

    private static string Required(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        return result.Length <= max ? result : throw new ArgumentOutOfRangeException(name);
    }
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}
