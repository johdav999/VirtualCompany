namespace VirtualCompany.Domain.Enums;

public enum SalesPresentationPresetLifecycle { Draft = 1, Published = 2, Archived = 3 }
public enum SalesPresentationPresetVersionLifecycle { Draft = 1, Published = 2, Archived = 3 }
public enum SalesPresentationPresetAssetStatus { PendingScan = 1, Processing = 2, Processed = 3, Failed = 4, Blocked = 5 }
public enum SalesPresentationPresetContextType { SalesMeeting = 1, CampaignActivity = 2, AdHoc = 3 }
public enum SalesPresentationRunPreparationStatus { Pending = 1, Preparing = 2, Ready = 3, NeedsReview = 4, Failed = 5 }
public enum SalesPresentationRunArtifactType { ConfirmedFact = 1, Need = 2, Risk = 3, Question = 4, Recommendation = 5, Positioning = 6, DesiredNextStep = 7, MissingEvidence = 8, SlideGuidance = 9 }

public static class SalesPresentationPresetEnumValues
{
    public static string ToStorageValue(this SalesPresentationPresetLifecycle value) => value switch
    {
        SalesPresentationPresetLifecycle.Draft => "draft", SalesPresentationPresetLifecycle.Published => "published",
        SalesPresentationPresetLifecycle.Archived => "archived", _ => throw Unsupported(value)
    };
    public static SalesPresentationPresetLifecycle ParsePresetLifecycle(string value) => value switch
    {
        "draft" => SalesPresentationPresetLifecycle.Draft, "published" => SalesPresentationPresetLifecycle.Published,
        "archived" => SalesPresentationPresetLifecycle.Archived, _ => throw Unsupported(value)
    };
    public static string ToStorageValue(this SalesPresentationPresetVersionLifecycle value) => value switch
    {
        SalesPresentationPresetVersionLifecycle.Draft => "draft", SalesPresentationPresetVersionLifecycle.Published => "published",
        SalesPresentationPresetVersionLifecycle.Archived => "archived", _ => throw Unsupported(value)
    };
    public static SalesPresentationPresetVersionLifecycle ParseVersionLifecycle(string value) => value switch
    {
        "draft" => SalesPresentationPresetVersionLifecycle.Draft, "published" => SalesPresentationPresetVersionLifecycle.Published,
        "archived" => SalesPresentationPresetVersionLifecycle.Archived, _ => throw Unsupported(value)
    };
    public static string ToStorageValue(this SalesPresentationPresetAssetStatus value) => value switch
    {
        SalesPresentationPresetAssetStatus.PendingScan => "pending_scan", SalesPresentationPresetAssetStatus.Processing => "processing",
        SalesPresentationPresetAssetStatus.Processed => "processed", SalesPresentationPresetAssetStatus.Failed => "failed",
        SalesPresentationPresetAssetStatus.Blocked => "blocked", _ => throw Unsupported(value)
    };
    public static SalesPresentationPresetAssetStatus ParseAssetStatus(string value) => value switch
    {
        "pending_scan" => SalesPresentationPresetAssetStatus.PendingScan, "processing" => SalesPresentationPresetAssetStatus.Processing,
        "processed" => SalesPresentationPresetAssetStatus.Processed, "failed" => SalesPresentationPresetAssetStatus.Failed,
        "blocked" => SalesPresentationPresetAssetStatus.Blocked, _ => throw Unsupported(value)
    };
    public static string ToStorageValue(this SalesPresentationPresetContextType value) => value switch
    {
        SalesPresentationPresetContextType.SalesMeeting => "sales_meeting", SalesPresentationPresetContextType.CampaignActivity => "campaign_activity",
        SalesPresentationPresetContextType.AdHoc => "ad_hoc", _ => throw Unsupported(value)
    };
    public static SalesPresentationPresetContextType ParseContextType(string value) => value switch
    {
        "sales_meeting" => SalesPresentationPresetContextType.SalesMeeting, "campaign_activity" => SalesPresentationPresetContextType.CampaignActivity,
        "ad_hoc" => SalesPresentationPresetContextType.AdHoc, _ => throw Unsupported(value)
    };
    public static string ToStorageValue(this SalesPresentationRunPreparationStatus value) => value switch
    {
        SalesPresentationRunPreparationStatus.Pending => "pending", SalesPresentationRunPreparationStatus.Preparing => "preparing",
        SalesPresentationRunPreparationStatus.Ready => "ready", SalesPresentationRunPreparationStatus.NeedsReview => "needs_review",
        SalesPresentationRunPreparationStatus.Failed => "failed", _ => throw Unsupported(value)
    };
    public static SalesPresentationRunPreparationStatus ParseRunStatus(string value) => value switch
    {
        "pending" => SalesPresentationRunPreparationStatus.Pending, "preparing" => SalesPresentationRunPreparationStatus.Preparing,
        "ready" => SalesPresentationRunPreparationStatus.Ready, "needs_review" => SalesPresentationRunPreparationStatus.NeedsReview,
        "failed" => SalesPresentationRunPreparationStatus.Failed, _ => throw Unsupported(value)
    };
    public static string ToStorageValue(this SalesPresentationRunArtifactType value) => value switch
    {
        SalesPresentationRunArtifactType.ConfirmedFact => "confirmed_fact", SalesPresentationRunArtifactType.Need => "need",
        SalesPresentationRunArtifactType.Risk => "risk", SalesPresentationRunArtifactType.Question => "question",
        SalesPresentationRunArtifactType.Recommendation => "recommendation", SalesPresentationRunArtifactType.Positioning => "positioning",
        SalesPresentationRunArtifactType.DesiredNextStep => "desired_next_step", SalesPresentationRunArtifactType.MissingEvidence => "missing_evidence",
        SalesPresentationRunArtifactType.SlideGuidance => "slide_guidance", _ => throw Unsupported(value)
    };
    public static SalesPresentationRunArtifactType ParseRunArtifactType(string value) => value switch
    {
        "confirmed_fact" => SalesPresentationRunArtifactType.ConfirmedFact, "need" => SalesPresentationRunArtifactType.Need,
        "risk" => SalesPresentationRunArtifactType.Risk, "question" => SalesPresentationRunArtifactType.Question,
        "recommendation" => SalesPresentationRunArtifactType.Recommendation, "positioning" => SalesPresentationRunArtifactType.Positioning,
        "desired_next_step" => SalesPresentationRunArtifactType.DesiredNextStep, "missing_evidence" => SalesPresentationRunArtifactType.MissingEvidence,
        "slide_guidance" => SalesPresentationRunArtifactType.SlideGuidance, _ => throw Unsupported(value)
    };
    private static ArgumentOutOfRangeException Unsupported(object value) => new(nameof(value), value, "Unsupported sales presentation preset value.");
}
