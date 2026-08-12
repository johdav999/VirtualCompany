using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingEventScanner(VirtualCompanyDbContext db,IMarketingEventPublisher publisher)
{
    public async Task<int> ScanAsync(DateTime nowUtc,CancellationToken ct)
    {
        var count=0;
        var overdue=await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking().Where(x=>x.DueUtc.HasValue&&x.DueUtc<nowUtc&&x.Status!="approved"&&x.Status!="retired").Take(100).Select(x=>new{x.CompanyId,x.Id,x.Version,x.Title,x.DueUtc,x.Status}).ToListAsync(ct);
        foreach(var x in overdue){await publisher.PublishAsync(x.CompanyId,new(MarketingEventTypes.ContentOverdue,"marketing_content_brief",x.Id.ToString("N"),x.Version,JsonSerializer.Serialize(x),$"content:{x.Id:N}",nowUtc,x.DueUtc!.Value.ToString("yyyyMMdd")),ct);count++;}
        var failed=await db.MarketingChannelActions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.Status=="failed"||x.Status=="ambiguous").Take(100).Select(x=>new{x.CompanyId,x.Id,x.Version,x.Status,x.FailureCode,x.ProviderReference}).ToListAsync(ct);
        foreach(var x in failed){await publisher.PublishAsync(x.CompanyId,new(MarketingEventTypes.ProviderFailure,"marketing_channel_action",x.Id.ToString("N"),x.Version,JsonSerializer.Serialize(x),$"channel-action:{x.Id:N}",nowUtc),ct);count++;}
        var staleCutoff=nowUtc.AddHours(-72);var stale=await db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking().Where(x=>!x.IsSuperseded&&x.RetrievedUtc<staleCutoff).OrderBy(x=>x.RetrievedUtc).Take(100).Select(x=>new{x.CompanyId,x.Id,x.MetricCode,x.Provider,x.RetrievedUtc,x.SourceReference}).ToListAsync(ct);
        foreach(var x in stale){await publisher.PublishAsync(x.CompanyId,new(MarketingEventTypes.StaleObservation,"marketing_observation",x.Id.ToString("N"),1,JsonSerializer.Serialize(x),$"observation:{x.Id:N}",nowUtc,nowUtc.ToString("yyyyMMdd")),ct);count++;}
        await db.SaveChangesAsync(ct);return count;
    }
}
public sealed class MarketingEventScannerBackgroundService(IServiceScopeFactory scopes,ILogger<MarketingEventScannerBackgroundService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken){while(!stoppingToken.IsCancellationRequested){try{using var scope=scopes.CreateScope();await scope.ServiceProvider.GetRequiredService<MarketingEventScanner>().ScanAsync(DateTime.UtcNow,stoppingToken);await Task.Delay(TimeSpan.FromMinutes(5),stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}catch(Exception e){logger.LogError(e,"Automatic Marketing event scan failed.");}}}
}
