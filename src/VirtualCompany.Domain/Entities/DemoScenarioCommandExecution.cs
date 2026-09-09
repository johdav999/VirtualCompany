namespace VirtualCompany.Domain.Entities;

public sealed class DemoScenarioCommandExecution : ICompanyOwnedEntity
{
    private DemoScenarioCommandExecution() { }

    public DemoScenarioCommandExecution(
        Guid id,
        Guid companyId,
        Guid runId,
        int resetGeneration,
        int stepNumber,
        string commandName,
        string idempotencyKey,
        Guid actorUserId,
        string disposition,
        string resultJson,
        DateTime executedUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (runId == Guid.Empty) throw new ArgumentException("RunId is required.", nameof(runId));
        if (resetGeneration < 1) throw new ArgumentOutOfRangeException(nameof(resetGeneration));
        if (stepNumber < 1) throw new ArgumentOutOfRangeException(nameof(stepNumber));
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunId = runId;
        ResetGeneration = resetGeneration;
        StepNumber = stepNumber;
        CommandName = Required(commandName, nameof(commandName), 100);
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 160);
        ActorUserId = actorUserId;
        Disposition = Required(disposition, nameof(disposition), 32);
        ResultJson = Required(resultJson, nameof(resultJson), 8000, normalizeCase: false);
        ExecutedUtc = executedUtc.Kind == DateTimeKind.Utc ? executedUtc : executedUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public int ResetGeneration { get; private set; }
    public int StepNumber { get; private set; }
    public string CommandName { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public string Disposition { get; private set; } = null!;
    public string ResultJson { get; private set; } = null!;
    public DateTime ExecutedUtc { get; private set; }
    public DemoScenarioRun Run { get; private set; } = null!;

    private static string Required(string value, string name, int maxLength, bool normalizeCase = true)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return normalizeCase ? normalized.ToLowerInvariant() : normalized;
    }
}

