namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingClosingArtifactStatus { Draft, InReview, Approved, Superseded }
public enum SalesMeetingMinutesItemType { Decision, Action, OutstandingQuestion, ProposedNextMeeting, ApprovedProductStatement }
public enum SalesMeetingInternalIntelligenceItemType { Objection, BuyingSignal, CompetitiveInformation, Risk, Recommendation, ProposedDealChange }

public static class SalesMeetingClosingEnumValues
{
    public static string ToStorageValue(this SalesMeetingClosingArtifactStatus value) => value switch
    {
        SalesMeetingClosingArtifactStatus.Draft => "draft",
        SalesMeetingClosingArtifactStatus.InReview => "in_review",
        SalesMeetingClosingArtifactStatus.Approved => "approved",
        SalesMeetingClosingArtifactStatus.Superseded => "superseded",
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingMinutesItemType value) => value switch
    {
        SalesMeetingMinutesItemType.Decision => "decision",
        SalesMeetingMinutesItemType.Action => "action",
        SalesMeetingMinutesItemType.OutstandingQuestion => "outstanding_question",
        SalesMeetingMinutesItemType.ProposedNextMeeting => "proposed_next_meeting",
        SalesMeetingMinutesItemType.ApprovedProductStatement => "approved_product_statement",
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingInternalIntelligenceItemType value) => value switch
    {
        SalesMeetingInternalIntelligenceItemType.Objection => "objection",
        SalesMeetingInternalIntelligenceItemType.BuyingSignal => "buying_signal",
        SalesMeetingInternalIntelligenceItemType.CompetitiveInformation => "competitive_information",
        SalesMeetingInternalIntelligenceItemType.Risk => "risk",
        SalesMeetingInternalIntelligenceItemType.Recommendation => "recommendation",
        SalesMeetingInternalIntelligenceItemType.ProposedDealChange => "proposed_deal_change",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingClosingArtifactStatus ParseStatus(string value) => Normalize(value) switch
    {
        "draft" => SalesMeetingClosingArtifactStatus.Draft,
        "in_review" => SalesMeetingClosingArtifactStatus.InReview,
        "approved" => SalesMeetingClosingArtifactStatus.Approved,
        "superseded" => SalesMeetingClosingArtifactStatus.Superseded,
        _ => throw Unsupported(value)
    };

    public static SalesMeetingMinutesItemType ParseMinutesItemType(string value) => Normalize(value) switch
    {
        "decision" => SalesMeetingMinutesItemType.Decision,
        "action" => SalesMeetingMinutesItemType.Action,
        "outstanding_question" => SalesMeetingMinutesItemType.OutstandingQuestion,
        "proposed_next_meeting" => SalesMeetingMinutesItemType.ProposedNextMeeting,
        "approved_product_statement" => SalesMeetingMinutesItemType.ApprovedProductStatement,
        _ => throw Unsupported(value)
    };

    public static SalesMeetingInternalIntelligenceItemType ParseInternalItemType(string value) => Normalize(value) switch
    {
        "objection" => SalesMeetingInternalIntelligenceItemType.Objection,
        "buying_signal" => SalesMeetingInternalIntelligenceItemType.BuyingSignal,
        "competitive_information" => SalesMeetingInternalIntelligenceItemType.CompetitiveInformation,
        "risk" => SalesMeetingInternalIntelligenceItemType.Risk,
        "recommendation" => SalesMeetingInternalIntelligenceItemType.Recommendation,
        "proposed_deal_change" => SalesMeetingInternalIntelligenceItemType.ProposedDealChange,
        _ => throw Unsupported(value)
    };

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A closing artifact value is required.", nameof(value))
        : value.Trim().ToLowerInvariant();
    private static ArgumentOutOfRangeException Unsupported<T>(T value) =>
        new(nameof(value), value, "Unsupported sales meeting closing artifact value.");
}
