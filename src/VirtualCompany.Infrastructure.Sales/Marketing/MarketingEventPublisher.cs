using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingEventPublisher(VirtualCompanyDbContext db):IMarketingEventPublisher
{
    public async Task<Guid> PublishAsync(Guid companyId,PublishMarketingEventCommand c,CancellationToken ct)
    {
        var type=c.EventType.Trim().ToLowerInvariant();if(!MarketingEventTypes.All.Contains(type))throw new ArgumentException("Unsupported Marketing event type.");
        if(c.SourceVersion<1)throw new ArgumentException("Source version is required.");System.Text.Json.JsonDocument.Parse(c.EvidenceJson);
        var window=c.OccurrenceWindow??c.OccurredUtc.ToUniversalTime().ToString("yyyyMMdd");var key=$"event:{type}:{c.SourceType.ToLowerInvariant()}:{c.SourceId.ToLowerInvariant()}:v{c.SourceVersion}:{window}";
        var old=await db.MarketingEventTriggers.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.IdempotencyKey==key,ct);if(old is not null)return old.Id;
        var e=new MarketingEventTrigger(Guid.NewGuid(),companyId,type,c.SourceType,c.SourceId,c.SourceVersion,MarketingEventPolicy.Severity(type),c.EvidenceJson,key,c.CorrelationId);db.Add(e);return e.Id;
    }
}
public static class MarketingEventPolicy
{
    public static string Severity(string type)=>type switch{MarketingEventTypes.ConsentIncident or MarketingEventTypes.BrandIncident=>"critical",MarketingEventTypes.ProviderFailure or MarketingEventTypes.ContentOverdue or MarketingEventTypes.AudienceFatigue=>"warning",_=>"info"};
}
public sealed class MarketingBriefingService(VirtualCompanyDbContext db):IMarketingBriefingService
{
    public async Task<MarketingBriefingDto> BuildAsync(Guid companyId,string cadence,DateTime nowUtc,CancellationToken ct){var span=cadence.ToLowerInvariant() switch{"daily"=>TimeSpan.FromDays(1),"weekly"=>TimeSpan.FromDays(7),"monthly"=>TimeSpan.FromDays(31),_=>throw new ArgumentException("Unsupported briefing cadence.")};var from=nowUtc.ToUniversalTime()-span;var events=await db.MarketingEventTriggers.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==companyId&&x.Status!="resolved"&&x.CreatedUtc>=from).OrderByDescending(x=>x.Severity=="critical").ThenByDescending(x=>x.CreatedUtc).Take(100).ToListAsync(ct);var distinct=events.DistinctBy(x=>new{x.EventType,x.SourceType,x.SourceId,x.SourceVersion}).ToArray();return new(cadence,from,nowUtc,distinct.Select(x=>new MarketingBriefingItemDto(x.Id,x.EventType,x.Severity,$"{x.EventType.Replace('_',' ')} from {x.SourceType}",x.EvidenceJson,x.RelatedTaskId,x.OperatingRunId,x.CorrelationId,x.CreatedUtc)).ToArray(),events.Count-distinct.Length);}
}
