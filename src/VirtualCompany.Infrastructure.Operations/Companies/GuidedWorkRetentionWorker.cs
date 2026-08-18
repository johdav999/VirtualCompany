using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class GuidedWorkRetentionWorker(IServiceScopeFactory scopes,IOptions<GuidedDialogueOptions> options,ILogger<GuidedWorkRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{await RunOnceAsync(stoppingToken);}catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}catch(Exception ex){logger.LogError(ex,"Guided work retention cleanup failed safely and will retry.");}
            await Task.Delay(TimeSpan.FromHours(24),stoppingToken);
        }
    }
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope=scopes.CreateScope();var db=scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();var cutoff=DateTime.UtcNow.AddDays(-Math.Clamp(options.Value.RetentionDays,7,3650));
        var sessions=await db.GuidedWorkSessions.IgnoreQueryFilters().Include(x=>x.Operations).Include(x=>x.VoiceBindings).Where(x=>(x.Status==GuidedWorkSessionStatuses.Completed||x.Status==GuidedWorkSessionStatuses.Cancelled)&&x.UpdatedUtc<cutoff).OrderBy(x=>x.UpdatedUtc).Take(100).ToListAsync(ct);
        foreach(var session in sessions)
        {
            var messages=await db.Messages.IgnoreQueryFilters().Include(x=>x.TaskLinks).Where(x=>x.CompanyId==session.CompanyId&&x.ConversationId==session.ConversationId&&x.CreatedUtc<cutoff).ToListAsync(ct);
            db.Messages.RemoveRange(messages.Where(x=>x.TaskLinks.Count==0&&IsSessionMessage(x,session.Id)));
            db.GuidedWorkSessions.Remove(session);
        }
        var expiredBindings=await db.GuidedVoiceBindings.IgnoreQueryFilters().Where(x=>x.ExpiresUtc<cutoff&&x.EndedUtc!=null).Take(500).ToListAsync(ct);db.GuidedVoiceBindings.RemoveRange(expiredBindings);
        if(sessions.Count>0||expiredBindings.Count>0)await db.SaveChangesAsync(ct);
        if(sessions.Count>0||expiredBindings.Count>0)logger.LogInformation("Removed {SessionCount} retained guided sessions and {BindingCount} expired voice bindings.",sessions.Count,expiredBindings.Count);
    }
    private static bool IsSessionMessage(Message message,Guid sessionId)
    {
        try{return message.StructuredPayload.TryGetValue("guided_session_id",out var value)&&value is System.Text.Json.Nodes.JsonValue json&&json.TryGetValue<string>(out var text)&&Guid.TryParse(text,out var id)&&id==sessionId;}catch{return false;}
    }
}
