namespace VirtualCompany.Domain.Enums;

public enum SalesPresentationDeckStatus
{
    PendingScan = 1,
    Processing = 2,
    Processed = 3,
    Failed = 4,
    Blocked = 5
}

public static class SalesPresentationDeckStatusValues
{
    private static readonly IReadOnlyDictionary<SalesPresentationDeckStatus, string> Values =
        new Dictionary<SalesPresentationDeckStatus, string>
        {
            [SalesPresentationDeckStatus.PendingScan] = "pending_scan",
            [SalesPresentationDeckStatus.Processing] = "processing",
            [SalesPresentationDeckStatus.Processed] = "processed",
            [SalesPresentationDeckStatus.Failed] = "failed",
            [SalesPresentationDeckStatus.Blocked] = "blocked"
        };

    private static readonly IReadOnlyDictionary<string, SalesPresentationDeckStatus> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesPresentationDeckStatus value) =>
        Values.TryGetValue(value, out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported presentation deck status.");

    public static SalesPresentationDeckStatus Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported presentation deck status.");
}

public enum SalesPresentationSlideStatus
{
    Processed = 1
}

public static class SalesPresentationSlideStatusValues
{
    public static string ToStorageValue(this SalesPresentationSlideStatus value) => value switch
    {
        SalesPresentationSlideStatus.Processed => "processed",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported presentation slide status.")
    };

    public static SalesPresentationSlideStatus Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "processed" => SalesPresentationSlideStatus.Processed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported presentation slide status.")
    };
}

public enum SalesMeetingArtifactType
{
    SlideTalkingPoint = 1,
    SlideClaim = 2,
    SlideSource = 3,
    BriefFact = 4,
    BriefNeed = 5,
    BriefRisk = 6,
    BriefQuestion = 7,
    BriefPositioning = 8,
    BriefNextStep = 9,
    BriefRecommendation = 10,
    BriefMissingEvidence = 11
}

public static class SalesMeetingArtifactTypeValues
{
    private static readonly IReadOnlyDictionary<SalesMeetingArtifactType, string> Values =
        new Dictionary<SalesMeetingArtifactType, string>
        {
            [SalesMeetingArtifactType.SlideTalkingPoint] = "slide_talking_point",
            [SalesMeetingArtifactType.SlideClaim] = "slide_claim",
            [SalesMeetingArtifactType.SlideSource] = "slide_source",
            [SalesMeetingArtifactType.BriefFact] = "brief_fact",
            [SalesMeetingArtifactType.BriefNeed] = "brief_need",
            [SalesMeetingArtifactType.BriefRisk] = "brief_risk",
            [SalesMeetingArtifactType.BriefQuestion] = "brief_question",
            [SalesMeetingArtifactType.BriefPositioning] = "brief_positioning",
            [SalesMeetingArtifactType.BriefNextStep] = "brief_next_step",
            [SalesMeetingArtifactType.BriefRecommendation] = "brief_recommendation",
            [SalesMeetingArtifactType.BriefMissingEvidence] = "brief_missing_evidence"
        };

    private static readonly IReadOnlyDictionary<string, SalesMeetingArtifactType> ReverseValues =
        Values.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    public static string ToStorageValue(this SalesMeetingArtifactType value) =>
        Values.TryGetValue(value, out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported meeting artifact type.");

    public static SalesMeetingArtifactType Parse(string value) =>
        !string.IsNullOrWhiteSpace(value) && ReverseValues.TryGetValue(value.Trim(), out var result)
            ? result
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported meeting artifact type.");
}

public enum SalesMeetingArtifactClassification
{
    ConfirmedFact = 1,
    Recommendation = 2,
    Inference = 3,
    NeedsConfirmation = 4
}

public static class SalesMeetingArtifactClassificationValues
{
    public static string ToStorageValue(this SalesMeetingArtifactClassification value) => value switch
    {
        SalesMeetingArtifactClassification.ConfirmedFact => "confirmed_fact",
        SalesMeetingArtifactClassification.Recommendation => "recommendation",
        SalesMeetingArtifactClassification.Inference => "inference",
        SalesMeetingArtifactClassification.NeedsConfirmation => "needs_confirmation",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported meeting artifact classification.")
    };

    public static SalesMeetingArtifactClassification Parse(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "confirmed_fact" => SalesMeetingArtifactClassification.ConfirmedFact,
        "recommendation" => SalesMeetingArtifactClassification.Recommendation,
        "inference" => SalesMeetingArtifactClassification.Inference,
        "needs_confirmation" => SalesMeetingArtifactClassification.NeedsConfirmation,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported meeting artifact classification.")
    };
}
