using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
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

