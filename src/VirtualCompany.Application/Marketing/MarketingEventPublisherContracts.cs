namespace VirtualCompany.Application.Marketing;

public sealed record PublishMarketingEventCommand(string EventType,string SourceType,string SourceId,int SourceVersion,
    string EvidenceJson,string CorrelationId,DateTime OccurredUtc,string? OccurrenceWindow=null);
public interface IMarketingEventPublisher
{
    Task<Guid> PublishAsync(Guid companyId,PublishMarketingEventCommand command,CancellationToken ct);
}
public sealed record MarketingBriefingItemDto(Guid EventId,string EventType,string Severity,string Summary,
    string EvidenceJson,Guid? RelatedTaskId,Guid? OperatingRunId,string CorrelationId,DateTime CreatedUtc);
public sealed record MarketingBriefingDto(string Cadence,DateTime FromUtc,DateTime ToUtc,
    IReadOnlyList<MarketingBriefingItemDto> Priorities,int SuppressedDuplicateCount);
public interface IMarketingBriefingService
{
    Task<MarketingBriefingDto> BuildAsync(Guid companyId,string cadence,DateTime nowUtc,CancellationToken ct);
}
