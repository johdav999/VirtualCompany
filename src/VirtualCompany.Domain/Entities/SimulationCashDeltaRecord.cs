namespace VirtualCompany.Domain.Entities;

public sealed class SimulationCashDeltaRecord : ICompanyOwnedEntity
{
    private SimulationCashDeltaRecord()
    {
    }

    public SimulationCashDeltaRecord(
        Guid id,
        Guid companyId,
        Guid simulationEventRecordId,
        DateTime simulationDateUtc,
        string sourceEntityType,
        Guid? sourceEntityId,
        decimal cashBefore,
        decimal cashDelta,
        decimal cashAfter,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (simulationEventRecordId == Guid.Empty)
        {
            throw new ArgumentException("SimulationEventRecordId is required.", nameof(simulationEventRecordId));
        }

        if (sourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId cannot be empty.", nameof(sourceEntityId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SimulationEventRecordId = simulationEventRecordId;
        SimulationDateUtc = EntityTimestampNormalizer.NormalizeUtc(simulationDateUtc, nameof(simulationDateUtc));
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), 64).ToLowerInvariant();
        SourceEntityId = sourceEntityId;
        CashBefore = NormalizeMoney(cashBefore);
        CashDelta = NormalizeMoney(cashDelta);
        CashAfter = NormalizeMoney(cashAfter);
        if (NormalizeMoney(CashBefore + CashDelta) != CashAfter)
        {
            throw new ArgumentException("CashAfter must equal CashBefore plus CashDelta.");
        }

        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? SimulationDateUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SimulationEventRecordId { get; private set; }
    public DateTime SimulationDateUtc { get; private set; }
    public string SourceEntityType { get; private set; } = null!;
    public Guid? SourceEntityId { get; private set; }
    public decimal CashBefore { get; private set; }
    public decimal CashDelta { get; private set; }
    public decimal CashAfter { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public SimulationEventRecord SimulationEventRecord { get; private set; } = null!;

    private static decimal NormalizeMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

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
