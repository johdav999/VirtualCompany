namespace VirtualCompany.Application.CustomerMemory;

public interface ICustomerMemoryService
{
    Task<CustomerMemoryContext?> GetContextAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<CustomerMemoryContext?> RefreshProfileAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken);
    Task<OfferEligibilityResult> EvaluateOfferEligibilityAsync(Guid companyId, Guid contactId, string offerKey, TimeSpan lookbackWindow, CancellationToken cancellationToken);
}

public sealed record CustomerMemoryContext(
    Guid CompanyId,
    Guid ContactId,
    string ContactName,
    string ContactEmail,
    string? CustomerCompanyName,
    string? Industry,
    string AiSummary,
    string RelationshipMemory,
    string? LastOutreachSummary,
    decimal EngagementScore,
    IReadOnlyList<CustomerConversationMemory> PastConversations,
    IReadOnlyList<CustomerDealMemory> PreviousDeals,
    IReadOnlyList<CustomerMemorySignal> Preferences,
    IReadOnlyList<CustomerMemorySignal> PriceSensitivityIndicators,
    IReadOnlyList<CustomerMemorySignal> IndustrySignals,
    IReadOnlyList<OfferExposureMemory> OfferExposureHistory,
    DateTime RefreshedUtc);

public sealed record CustomerConversationMemory(
    Guid? ConversationId,
    string Summary,
    DateTime OccurredUtc,
    string SourceType);

public sealed record CustomerDealMemory(
    Guid DealId,
    string Title,
    string Status,
    decimal Amount,
    string Currency,
    DateTime? ClosedUtc,
    string Summary);

public sealed record CustomerMemorySignal(
    string Key,
    string Value,
    decimal Confidence,
    DateTime ObservedUtc,
    string SourceSummary);

public sealed record OfferExposureMemory(
    string OfferKey,
    Guid? CampaignId,
    Guid? DealId,
    DateTime OccurredUtc,
    string SourceType,
    string Summary);

public sealed record OfferEligibilityResult(
    bool CanSend,
    string OfferKey,
    DateTime LookbackStartUtc,
    string? BlockReason,
    IReadOnlyList<OfferExposureMemory> MatchingExposures)
{
    public static OfferEligibilityResult Allowed(string offerKey, DateTime lookbackStartUtc) =>
        new(true, offerKey, lookbackStartUtc, null, []);

    public static OfferEligibilityResult Blocked(string offerKey, DateTime lookbackStartUtc, IReadOnlyList<OfferExposureMemory> exposures)
    {
        var reason = exposures.Count == 1
            ? $"This contact already received or discussed this offer on {exposures[0].OccurredUtc:yyyy-MM-dd}."
            : $"This contact already received or discussed this offer {exposures.Count} times in the lookback window.";

        return new(false, offerKey, lookbackStartUtc, reason, exposures);
    }
}

public sealed class CustomerMemoryOptions
{
    public const string SectionName = "CustomerMemory";
    public int DuplicateOfferLookbackDays { get; set; } = 90;
}