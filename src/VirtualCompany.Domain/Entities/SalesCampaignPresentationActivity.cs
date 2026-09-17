namespace VirtualCompany.Domain.Entities;

public enum SalesCampaignPresentationExecutionScope { PerContact = 1, PerAccount = 2, CampaignEvent = 3 }
public enum SalesCampaignPresentationPresenterStrategy { Explicit = 1, ActivityOwner = 2, CampaignOwner = 3 }
public enum SalesCampaignPresentationWorkStrategy { PreparationTask = 1, Handoff = 2, MeetingBinding = 3 }

public static class SalesCampaignPresentationValues
{
    public const string ActivityType = "presentation";
    public const string Channel = "presentation";
    public static string ToValue(this SalesCampaignPresentationExecutionScope value) => value switch
    {
        SalesCampaignPresentationExecutionScope.PerContact => "per_contact",
        SalesCampaignPresentationExecutionScope.PerAccount => "per_account",
        SalesCampaignPresentationExecutionScope.CampaignEvent => "campaign_event",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static SalesCampaignPresentationExecutionScope ParseScope(string value) => value switch
    {
        "per_contact" => SalesCampaignPresentationExecutionScope.PerContact,
        "per_account" => SalesCampaignPresentationExecutionScope.PerAccount,
        "campaign_event" => SalesCampaignPresentationExecutionScope.CampaignEvent,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static string ToValue(this SalesCampaignPresentationPresenterStrategy value) => value switch
    {
        SalesCampaignPresentationPresenterStrategy.Explicit => "explicit",
        SalesCampaignPresentationPresenterStrategy.ActivityOwner => "activity_owner",
        SalesCampaignPresentationPresenterStrategy.CampaignOwner => "campaign_owner",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static SalesCampaignPresentationPresenterStrategy ParsePresenterStrategy(string value) => value switch
    {
        "explicit" => SalesCampaignPresentationPresenterStrategy.Explicit,
        "activity_owner" => SalesCampaignPresentationPresenterStrategy.ActivityOwner,
        "campaign_owner" => SalesCampaignPresentationPresenterStrategy.CampaignOwner,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static string ToValue(this SalesCampaignPresentationWorkStrategy value) => value switch
    {
        SalesCampaignPresentationWorkStrategy.PreparationTask => "preparation_task",
        SalesCampaignPresentationWorkStrategy.Handoff => "handoff",
        SalesCampaignPresentationWorkStrategy.MeetingBinding => "meeting_binding",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    public static SalesCampaignPresentationWorkStrategy ParseWorkStrategy(string value) => value switch
    {
        "preparation_task" => SalesCampaignPresentationWorkStrategy.PreparationTask,
        "handoff" => SalesCampaignPresentationWorkStrategy.Handoff,
        "meeting_binding" => SalesCampaignPresentationWorkStrategy.MeetingBinding,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

public sealed class SalesCampaignPresentationActivity : ICompanyOwnedEntity
{
    private SalesCampaignPresentationActivity() { }
    public SalesCampaignPresentationActivity(Guid id, Guid companyId, Guid activityId, Guid presetVersionId,
        SalesCampaignPresentationExecutionScope scope, SalesCampaignPresentationPresenterStrategy presenterStrategy,
        Guid? explicitPresenterAgentId, SalesCampaignPresentationWorkStrategy workStrategy, bool allowOverrides,
        Guid? eventSessionId, int preparationLeadTimeHours, Guid actorUserId, DateTime nowUtc)
    {
        Require(companyId, nameof(companyId)); Require(activityId, nameof(activityId)); Require(presetVersionId, nameof(presetVersionId)); Require(actorUserId, nameof(actorUserId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; SalesCampaignActivityId = activityId;
        CreatedByUserId = actorUserId; CreatedUtc = Utc(nowUtc); Version = 0;
        Configure(presetVersionId, scope, presenterStrategy, explicitPresenterAgentId, workStrategy, allowOverrides, eventSessionId, preparationLeadTimeHours, actorUserId, nowUtc, 0);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SalesCampaignActivityId { get; private set; }
    public Guid PresetVersionId { get; private set; } public SalesCampaignPresentationExecutionScope ExecutionScope { get; private set; }
    public SalesCampaignPresentationPresenterStrategy PresenterStrategy { get; private set; } public Guid? ExplicitPresenterAgentId { get; private set; }
    public SalesCampaignPresentationWorkStrategy WorkStrategy { get; private set; } public bool AllowOverrides { get; private set; }
    public Guid? EventSessionId { get; private set; } public int PreparationLeadTimeHours { get; private set; } public int Version { get; private set; }
    public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public SalesCampaignActivity Activity { get; private set; } = null!; public SalesPresentationPresetVersion PresetVersion { get; private set; } = null!;
    public Agent? ExplicitPresenterAgent { get; private set; } public SalesMeetingSession? EventSession { get; private set; }
    public ICollection<SalesCampaignPresentationRun> Runs { get; } = new List<SalesCampaignPresentationRun>();
    public void Configure(Guid presetVersionId, SalesCampaignPresentationExecutionScope scope, SalesCampaignPresentationPresenterStrategy presenterStrategy,
        Guid? explicitPresenterAgentId, SalesCampaignPresentationWorkStrategy workStrategy, bool allowOverrides, Guid? eventSessionId,
        int preparationLeadTimeHours, Guid actorUserId, DateTime nowUtc, int expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The presentation activity changed. Refresh before saving.");
        Require(presetVersionId, nameof(presetVersionId)); Require(actorUserId, nameof(actorUserId));
        if (preparationLeadTimeHours is < 0 or > 2160) throw new ArgumentOutOfRangeException(nameof(preparationLeadTimeHours));
        if (presenterStrategy == SalesCampaignPresentationPresenterStrategy.Explicit && explicitPresenterAgentId is null) throw new ArgumentException("Choose an explicit presenter.");
        if (scope == SalesCampaignPresentationExecutionScope.CampaignEvent && eventSessionId is null) throw new ArgumentException("Choose an event session for campaign-event scope.");
        if (workStrategy == SalesCampaignPresentationWorkStrategy.MeetingBinding && eventSessionId is null) throw new ArgumentException("Meeting binding requires an event session.");
        PresetVersionId = presetVersionId; ExecutionScope = scope; PresenterStrategy = presenterStrategy; ExplicitPresenterAgentId = explicitPresenterAgentId;
        WorkStrategy = workStrategy; AllowOverrides = allowOverrides; EventSessionId = eventSessionId; PreparationLeadTimeHours = preparationLeadTimeHours;
        UpdatedByUserId = actorUserId; UpdatedUtc = Utc(nowUtc); Version++;
    }
    private static void Require(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class SalesCampaignPresentationRun : ICompanyOwnedEntity
{
    private SalesCampaignPresentationRun() { }
    public SalesCampaignPresentationRun(Guid id, Guid companyId, Guid configurationId, Guid presentationRunId, string subjectType, Guid subjectId, string idempotencyKey, DateTime nowUtc)
    {
        if (companyId == Guid.Empty || configurationId == Guid.Empty || presentationRunId == Guid.Empty || subjectId == Guid.Empty) throw new ArgumentException("Company, configuration, presentation run, and subject are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; ConfigurationId = configurationId; PresentationRunId = presentationRunId;
        SubjectType = Required(subjectType, 32); SubjectId = subjectId; IdempotencyKey = Required(idempotencyKey, 128); Status = "created"; CreatedUtc = UpdatedUtc = Utc(nowUtc);
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ConfigurationId { get; private set; }
    public Guid PresentationRunId { get; private set; } public string SubjectType { get; private set; } = null!; public Guid SubjectId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!; public string Status { get; private set; } = null!; public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; } public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public SalesCampaignPresentationActivity Configuration { get; private set; } = null!; public SalesPresentationRun PresentationRun { get; private set; } = null!;
    public void MarkPrepared(DateTime now) { Status = "prepared"; FailureCode = FailureSummary = null; UpdatedUtc = Utc(now); }
    public void Fail(string code, string summary, DateTime now) { Status = "failed"; FailureCode = Required(code, 100); FailureSummary = Required(summary, 1000); UpdatedUtc = Utc(now); }
    public void QueueRetry(DateTime now) { if (Status != "failed") return; Status = "retrying"; FailureCode = FailureSummary = null; UpdatedUtc = Utc(now); }
    private static string Required(string? value, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A value is required."); var result = value.Trim(); return result.Length <= max ? result : throw new ArgumentOutOfRangeException(nameof(value)); }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
