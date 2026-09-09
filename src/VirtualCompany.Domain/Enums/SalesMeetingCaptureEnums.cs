namespace VirtualCompany.Domain.Enums;

public enum SalesMeetingSpeakerType { Host, Customer, Agent, Unknown }
public enum SalesMeetingInputSource { Typed, HostMediated, TranscriptAdapter, Voice }
public enum SalesMeetingReviewState { Unreviewed, Reviewed, Rejected }
public enum SalesMeetingQuestionStatus { Pending, Answering, Completed, Unverified, Failed, Cancelled }
public enum SalesMeetingAnswerVisibility { Private, ApprovedForStage }
public enum SalesMeetingObservationCategory
{
    CustomerNeed,
    PainPoint,
    ProductInterest,
    Objection,
    CompetitiveReference,
    BuyingSignal,
    Commitment,
    OpenQuestion,
    InternalSalesIntelligence
}
public enum SalesMeetingActionItemStatus { Open, Completed, Dismissed }

public static class SalesMeetingCaptureEnumValues
{
    public static string ToStorageValue(this SalesMeetingSpeakerType value) => value switch
    {
        SalesMeetingSpeakerType.Host => "host",
        SalesMeetingSpeakerType.Customer => "customer",
        SalesMeetingSpeakerType.Agent => "agent",
        SalesMeetingSpeakerType.Unknown => "unknown",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingSpeakerType ParseSpeakerType(string value) => Normalize(value) switch
    {
        "host" => SalesMeetingSpeakerType.Host,
        "customer" => SalesMeetingSpeakerType.Customer,
        "agent" => SalesMeetingSpeakerType.Agent,
        "unknown" => SalesMeetingSpeakerType.Unknown,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingInputSource value) => value switch
    {
        SalesMeetingInputSource.Typed => "typed",
        SalesMeetingInputSource.HostMediated => "host_mediated",
        SalesMeetingInputSource.TranscriptAdapter => "transcript_adapter",
        SalesMeetingInputSource.Voice => "voice",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingInputSource ParseInputSource(string value) => Normalize(value) switch
    {
        "typed" => SalesMeetingInputSource.Typed,
        "host_mediated" => SalesMeetingInputSource.HostMediated,
        "transcript_adapter" => SalesMeetingInputSource.TranscriptAdapter,
        "voice" => SalesMeetingInputSource.Voice,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingReviewState value) => value switch
    {
        SalesMeetingReviewState.Unreviewed => "unreviewed",
        SalesMeetingReviewState.Reviewed => "reviewed",
        SalesMeetingReviewState.Rejected => "rejected",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingReviewState ParseReviewState(string value) => Normalize(value) switch
    {
        "unreviewed" => SalesMeetingReviewState.Unreviewed,
        "reviewed" => SalesMeetingReviewState.Reviewed,
        "rejected" => SalesMeetingReviewState.Rejected,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingQuestionStatus value) => value switch
    {
        SalesMeetingQuestionStatus.Pending => "pending",
        SalesMeetingQuestionStatus.Answering => "answering",
        SalesMeetingQuestionStatus.Completed => "completed",
        SalesMeetingQuestionStatus.Unverified => "unverified",
        SalesMeetingQuestionStatus.Failed => "failed",
        SalesMeetingQuestionStatus.Cancelled => "cancelled",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingQuestionStatus ParseQuestionStatus(string value) => Normalize(value) switch
    {
        "pending" => SalesMeetingQuestionStatus.Pending,
        "answering" => SalesMeetingQuestionStatus.Answering,
        "completed" => SalesMeetingQuestionStatus.Completed,
        "unverified" => SalesMeetingQuestionStatus.Unverified,
        "failed" => SalesMeetingQuestionStatus.Failed,
        "cancelled" => SalesMeetingQuestionStatus.Cancelled,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingAnswerVisibility value) => value switch
    {
        SalesMeetingAnswerVisibility.Private => "private",
        SalesMeetingAnswerVisibility.ApprovedForStage => "approved_for_stage",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingAnswerVisibility ParseAnswerVisibility(string value) => Normalize(value) switch
    {
        "private" => SalesMeetingAnswerVisibility.Private,
        "approved_for_stage" => SalesMeetingAnswerVisibility.ApprovedForStage,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingObservationCategory value) => value switch
    {
        SalesMeetingObservationCategory.CustomerNeed => "customer_need",
        SalesMeetingObservationCategory.PainPoint => "pain_point",
        SalesMeetingObservationCategory.ProductInterest => "product_interest",
        SalesMeetingObservationCategory.Objection => "objection",
        SalesMeetingObservationCategory.CompetitiveReference => "competitive_reference",
        SalesMeetingObservationCategory.BuyingSignal => "buying_signal",
        SalesMeetingObservationCategory.Commitment => "commitment",
        SalesMeetingObservationCategory.OpenQuestion => "open_question",
        SalesMeetingObservationCategory.InternalSalesIntelligence => "internal_sales_intelligence",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingObservationCategory ParseObservationCategory(string value) => Normalize(value) switch
    {
        "customer_need" => SalesMeetingObservationCategory.CustomerNeed,
        "pain_point" => SalesMeetingObservationCategory.PainPoint,
        "product_interest" => SalesMeetingObservationCategory.ProductInterest,
        "objection" => SalesMeetingObservationCategory.Objection,
        "competitive_reference" => SalesMeetingObservationCategory.CompetitiveReference,
        "buying_signal" => SalesMeetingObservationCategory.BuyingSignal,
        "commitment" => SalesMeetingObservationCategory.Commitment,
        "open_question" => SalesMeetingObservationCategory.OpenQuestion,
        "internal_sales_intelligence" => SalesMeetingObservationCategory.InternalSalesIntelligence,
        _ => throw Unsupported(value)
    };

    public static string ToStorageValue(this SalesMeetingActionItemStatus value) => value switch
    {
        SalesMeetingActionItemStatus.Open => "open",
        SalesMeetingActionItemStatus.Completed => "completed",
        SalesMeetingActionItemStatus.Dismissed => "dismissed",
        _ => throw Unsupported(value)
    };

    public static SalesMeetingActionItemStatus ParseActionItemStatus(string value) => Normalize(value) switch
    {
        "open" => SalesMeetingActionItemStatus.Open,
        "completed" => SalesMeetingActionItemStatus.Completed,
        "dismissed" => SalesMeetingActionItemStatus.Dismissed,
        _ => throw Unsupported(value)
    };

    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A capture enum value is required.", nameof(value))
        : value.Trim().ToLowerInvariant();

    private static ArgumentOutOfRangeException Unsupported<T>(T value) =>
        new(nameof(value), value, "Unsupported sales meeting capture value.");
}
