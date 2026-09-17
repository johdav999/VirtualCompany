using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class SalesPresentationPresetVersion : ICompanyOwnedEntity
{
    private SalesPresentationPresetVersion() { }
    public SalesPresentationPresetVersion(Guid id, Guid companyId, Guid presetId, int versionNumber, Guid? defaultPresenterAgentId,
        string? behaviorSettingsJson, string goal, string audience, int durationMinutes, string? demoScenario, string controlMode,
        string language, bool allowSalesMeeting, bool allowCampaignActivity, bool allowAdHoc, string? requiredKnowledgeScope, DateTime nowUtc)
    {
        SalesPresentationPreset.RequiredId(companyId, nameof(companyId)); SalesPresentationPreset.RequiredId(presetId, nameof(presetId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; PresetId = presetId; VersionNumber = versionNumber;
        Lifecycle = SalesPresentationPresetVersionLifecycle.Draft; Apply(defaultPresenterAgentId, behaviorSettingsJson, goal, audience,
            durationMinutes, demoScenario, controlMode, language, allowSalesMeeting, allowCampaignActivity, allowAdHoc, requiredKnowledgeScope);
        CreatedUtc = UpdatedUtc = SalesPresentationPreset.Utc(nowUtc); ConcurrencyVersion = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PresetId { get; private set; }
    public int VersionNumber { get; private set; }
    public SalesPresentationPresetVersionLifecycle Lifecycle { get; private set; }
    public Guid? DefaultPresenterAgentId { get; private set; }
    public string? BehaviorSettingsJson { get; private set; }
    public string Goal { get; private set; } = null!;
    public string Audience { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public string? DemoScenario { get; private set; }
    public string ControlMode { get; private set; } = null!;
    public string Language { get; private set; } = null!;
    public bool AllowSalesMeeting { get; private set; }
    public bool AllowCampaignActivity { get; private set; }
    public bool AllowAdHoc { get; private set; }
    public string? RequiredKnowledgeScope { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTime? PublishedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public SalesPresentationPreset Preset { get; private set; } = null!;
    public Agent? DefaultPresenterAgent { get; private set; }
    public SalesPresentationPresetAsset? Asset { get; private set; }
    public void Update(Guid? presenter, string? behavior, string goal, string audience, int duration, string? demo, string control,
        string language, bool meeting, bool campaign, bool adHoc, string? knowledge, DateTime nowUtc)
    {
        EnsureDraft(); Apply(presenter, behavior, goal, audience, duration, demo, control, language, meeting, campaign, adHoc, knowledge); Touch(nowUtc);
    }
    public void Publish(Guid actorUserId, DateTime nowUtc)
    {
        EnsureDraft(); SalesPresentationPreset.RequiredId(actorUserId, nameof(actorUserId)); Lifecycle = SalesPresentationPresetVersionLifecycle.Published;
        PublishedByUserId = actorUserId; PublishedUtc = SalesPresentationPreset.Utc(nowUtc); Touch(nowUtc);
    }
    private void Apply(Guid? presenter, string? behavior, string goal, string audience, int duration, string? demo, string control,
        string language, bool meeting, bool campaign, bool adHoc, string? knowledge)
    {
        if (presenter == Guid.Empty) throw new ArgumentException("Default presenter cannot be empty.", nameof(presenter));
        if (duration is < 1 or > 480) throw new ArgumentOutOfRangeException(nameof(duration));
        if (control is not ("manual" or "assisted")) throw new ArgumentException("Preset control mode must be manual or assisted.", nameof(control));
        if (!meeting && !campaign && !adHoc) throw new ArgumentException("At least one presentation context is required.");
        DefaultPresenterAgentId = presenter; BehaviorSettingsJson = SalesPresentationPreset.Optional(behavior, 8000);
        Goal = SalesPresentationPreset.Required(goal, 2000); Audience = SalesPresentationPreset.Required(audience, 1000); DurationMinutes = duration;
        DemoScenario = SalesPresentationPreset.Optional(demo, 2000); ControlMode = control; Language = SalesPresentationPreset.Required(language, 20).ToLowerInvariant();
        AllowSalesMeeting = meeting; AllowCampaignActivity = campaign; AllowAdHoc = adHoc; RequiredKnowledgeScope = SalesPresentationPreset.Optional(knowledge, 200);
    }
    private void EnsureDraft() { if (Lifecycle != SalesPresentationPresetVersionLifecycle.Draft) throw new InvalidOperationException("Published presentation preset versions are immutable."); }
    private void Touch(DateTime nowUtc) { UpdatedUtc = SalesPresentationPreset.Utc(nowUtc); ConcurrencyVersion++; }
}
