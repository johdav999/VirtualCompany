namespace VirtualCompany.Domain.Entities;

public sealed class SimulationEventRecord : ICompanyOwnedEntity
{
    private SimulationEventRecord()
    {
    }

    public SimulationEventRecord(
        Guid id,
        Guid companyId,
        Guid? simulationSessionId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulationDateUtc,
        string eventType,
        string sourceEntityType,
        Guid? sourceEntityId,
        string? sourceReference,
        Guid? parentEventId,
        int sequenceNumber,
        string deterministicKey,
        decimal? cashBefore,
        decimal? cashDelta,
        decimal? cashAfter,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (sourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId cannot be empty.", nameof(sourceEntityId));
        }

        if (parentEventId == Guid.Empty)
        {
            throw new ArgumentException("ParentEventId cannot be empty.", nameof(parentEventId));
        }

        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "SequenceNumber must be positive.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SimulationSessionId = simulationSessionId;
        Seed = seed;
        StartSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(startSimulatedUtc, nameof(startSimulatedUtc));
        SimulationDateUtc = EntityTimestampNormalizer.NormalizeUtc(simulationDateUtc, nameof(simulationDateUtc));
        EventType = NormalizeRequired(eventType, nameof(eventType), 64).ToLowerInvariant();
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), 64).ToLowerInvariant();
        SourceEntityId = sourceEntityId;
        SourceReference = NormalizeOptional(sourceReference, nameof(sourceReference), 128);
        ParentEventId = parentEventId;
        SequenceNumber = sequenceNumber;
        DeterministicKey = NormalizeRequired(deterministicKey, nameof(deterministicKey), 256).ToLowerInvariant();
        (CashBefore, CashDelta, CashAfter) = NormalizeCashSnapshot(cashBefore, cashDelta, cashAfter);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? SimulationDateUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SimulationSessionId { get; private set; }
    public int Seed { get; private set; }
    public DateTime StartSimulatedUtc { get; private set; }
    public DateTime SimulationDateUtc { get; private set; }
    public string EventType { get; private set; } = null!;
    public string SourceEntityType { get; private set; } = null!;
    public Guid? SourceEntityId { get; private set; }
    public string? SourceReference { get; private set; }
    public Guid? ParentEventId { get; private set; }
    public int SequenceNumber { get; private set; }
    public string DeterministicKey { get; private set; } = null!;
    public decimal? CashBefore { get; private set; }
    public decimal? CashDelta { get; private set; }
    public decimal? CashAfter { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public SimulationEventRecord? ParentEvent { get; private set; }

    private static (decimal? CashBefore, decimal? CashDelta, decimal? CashAfter) NormalizeCashSnapshot(
        decimal? cashBefore,
        decimal? cashDelta,
        decimal? cashAfter)
    {
        var hasBefore = cashBefore.HasValue;
        var hasDelta = cashDelta.HasValue;
        var hasAfter = cashAfter.HasValue;

        if (!hasBefore && !hasDelta && !hasAfter)
        {
            return (null, null, null);
        }

        if (!hasBefore || !hasDelta || !hasAfter)
        {
            throw new ArgumentException("CashBefore, CashDelta, and CashAfter must either all be provided or all be omitted.");
        }

        var normalizedBefore = NormalizeMoney(cashBefore!.Value);
        var normalizedDelta = NormalizeMoney(cashDelta!.Value);
        var normalizedAfter = NormalizeMoney(cashAfter!.Value);

        if (NormalizeMoney(normalizedBefore + normalizedDelta) != normalizedAfter)
        {
            throw new ArgumentException("CashAfter must equal CashBefore plus CashDelta.");
        }

        return (normalizedBefore, normalizedDelta, normalizedAfter);
    }

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