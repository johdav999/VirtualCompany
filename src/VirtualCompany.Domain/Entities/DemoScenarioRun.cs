using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class DemoScenarioRun : ICompanyOwnedEntity
{
    private DemoScenarioRun() { }

    public DemoScenarioRun(
        Guid id,
        Guid companyId,
        string scenarioKey,
        int scenarioVersion,
        Guid provisionedByUserId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (scenarioVersion < 1) throw new ArgumentOutOfRangeException(nameof(scenarioVersion));
        if (provisionedByUserId == Guid.Empty) throw new ArgumentException("ProvisionedByUserId is required.", nameof(provisionedByUserId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ScenarioKey = Required(scenarioKey, nameof(scenarioKey), 100);
        ScenarioVersion = scenarioVersion;
        ProvisionedByUserId = provisionedByUserId;
        Status = DemoScenarioRunStatus.Ready;
        ResetGeneration = 1;
        ConcurrencyVersion = 1;
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        LastResetUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ScenarioKey { get; private set; } = null!;
    public int ScenarioVersion { get; private set; }
    public Guid ProvisionedByUserId { get; private set; }
    public Guid? MeetingSessionId { get; private set; }
    public Guid? LinkedByUserId { get; private set; }
    public DemoScenarioRunStatus Status { get; private set; }
    public int CurrentStep { get; private set; }
    public int ResetGeneration { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime LastResetUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }

    public void LinkMeeting(Guid meetingSessionId, Guid userId, DateTime utcNow)
    {
        if (meetingSessionId == Guid.Empty) throw new ArgumentException("MeetingSessionId is required.", nameof(meetingSessionId));
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        if (MeetingSessionId.HasValue && MeetingSessionId.Value != meetingSessionId)
            throw new InvalidOperationException("This demo tenant is already linked to another meeting.");

        MeetingSessionId = meetingSessionId;
        LinkedByUserId = userId;
        UpdatedUtc = Utc(utcNow);
        ConcurrencyVersion++;
    }

    public void Start(DateTime utcNow)
    {
        if (!MeetingSessionId.HasValue)
            throw new InvalidOperationException("Link the demo scenario to a meeting before starting it.");
        if (Status == DemoScenarioRunStatus.Completed)
            throw new InvalidOperationException("Reset the completed demo before starting it again.");

        Status = DemoScenarioRunStatus.Running;
        StartedUtc ??= Utc(utcNow);
        UpdatedUtc = Utc(utcNow);
        ConcurrencyVersion++;
    }

    public void CompleteStep(int expectedCurrentStep, int stepCount, DateTime utcNow)
    {
        if (Status != DemoScenarioRunStatus.Running)
            throw new InvalidOperationException("Start the demo before running a step.");
        if (CurrentStep != expectedCurrentStep)
            throw new InvalidOperationException("The requested demo step is not current.");
        if (stepCount < 1 || CurrentStep >= stepCount)
            throw new InvalidOperationException("The demo has no remaining steps.");

        CurrentStep++;
        UpdatedUtc = Utc(utcNow);
        ConcurrencyVersion++;
        if (CurrentStep == stepCount)
        {
            Status = DemoScenarioRunStatus.Completed;
            CompletedUtc = UpdatedUtc;
        }
    }

    public void Reset(DateTime utcNow)
    {
        ResetGeneration++;
        CurrentStep = 0;
        Status = DemoScenarioRunStatus.Ready;
        StartedUtc = null;
        CompletedUtc = null;
        LastResetUtc = Utc(utcNow);
        UpdatedUtc = LastResetUtc;
        ConcurrencyVersion++;
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
