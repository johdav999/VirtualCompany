

namespace VirtualCompany.Application.CustomerMemory;


public sealed record OfferExposureMemory(
    string OfferKey,
    Guid? CampaignId,
    Guid? DealId,
    DateTime OccurredUtc,
    string SourceType,
    string Summary);


public sealed record CustomerDealMemory(
    Guid DealId,
    string Title,
    string Status,
    decimal Amount,
    string Currency,
    DateTime? ClosedUtc,
    string Summary);


public sealed record CustomerConversationMemory(
    Guid? ConversationId,
    string Summary,
    DateTime OccurredUtc,
    string SourceType);


public sealed record CustomerMemorySignal(
    string Key,
    string Value,
    decimal Confidence,
    DateTime ObservedUtc,
    string SourceSummary);


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
