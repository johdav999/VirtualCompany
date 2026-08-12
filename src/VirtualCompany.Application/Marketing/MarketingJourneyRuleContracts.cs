namespace VirtualCompany.Application.Marketing;

public sealed record MarketingJourneyContactFacts(Guid ContactId, string Status, bool HasEmail,
    bool HasCustomerCompany, string? PreferredLanguage, DateTime CreatedUtc,
    IReadOnlySet<string> EventTypes, IReadOnlySet<Guid> StrategicSegmentVersionIds);
public sealed record MarketingJourneyRuleDecision(bool Allowed, bool ShouldExit, bool GoalReached,
    IReadOnlyList<string> ReasonCodes, string EvidenceJson);
public interface IMarketingJourneyRuleEvaluator
{
    MarketingJourneyValidationDto Validate(string audienceEligibilityJson, string entryExitCriteriaJson, string guardrailsJson, int stepCount);
    MarketingJourneyRuleDecision Evaluate(string audienceEligibilityJson, string entryExitCriteriaJson,
        MarketingJourneyContactFacts facts, string phase);
}

public sealed record ProcessMarketingJourneyInboundEventRequest(Guid JourneyId, int JourneyVersion,
    Guid ContactId, string EventType, string EventReference, int OccurrenceVersion,
    DateTime OccurredUtc, string EvidenceJson);
public sealed record MarketingJourneyInboundEventDto(Guid Id, Guid JourneyId, int JourneyVersion,
    Guid ContactId, string EventType, string EventReference, int OccurrenceVersion,
    string Outcome, DateTime OccurredUtc, DateTime ProcessedUtc);
public interface IMarketingJourneyInboundEventService
{
    Task<MarketingJourneyInboundEventDto> ProcessAsync(Guid companyId,
        ProcessMarketingJourneyInboundEventRequest request, CancellationToken ct);
}
