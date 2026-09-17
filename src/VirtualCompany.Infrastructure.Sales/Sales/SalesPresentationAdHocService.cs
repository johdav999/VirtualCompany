using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationAdHocService(VirtualCompanyDbContext db,ISalesPresentationRunContextResolver contexts,TimeProvider timeProvider,ILogger<SalesPresentationAdHocService> logger):ISalesPresentationAdHocService
{
    private static readonly Meter Meter=new("VirtualCompany.Sales.PresentationAdHoc","1.0.0");
    private static readonly Counter<long> Creations=Meter.CreateCounter<long>("sales.presentation.ad_hoc.creations");
    private static readonly Counter<long> Rejections=Meter.CreateCounter<long>("sales.presentation.ad_hoc.rejections");
    private static readonly Histogram<double> Latency=Meter.CreateHistogram<double>("sales.presentation.ad_hoc.preparation.latency","ms");

    public async Task<SalesPresentationAdHocOptionsDto> GetOptionsAsync(Guid companyId,Guid actorUserId,CancellationToken ct)
    {
        await Member(companyId,actorUserId,ct);
        var accounts=await db.CustomerCompanies.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new SalesPresentationContextOptionDto(x.Id,x.Name,null,null,null)).ToListAsync(ct);
        var contacts=await db.Contacts.AsNoTracking().OrderBy(x=>x.FullName).Select(x=>new SalesPresentationContextOptionDto(x.Id,x.FullName,x.CustomerCompanyId,null,null)).ToListAsync(ct);
        var leads=await db.Leads.AsNoTracking().OrderBy(x=>x.Title).Select(x=>new SalesPresentationContextOptionDto(x.Id,x.Title,x.CustomerCompanyId,x.PrimaryContactId,null)).ToListAsync(ct);
        var deals=await db.Deals.AsNoTracking().OrderBy(x=>x.Title).Select(x=>new SalesPresentationContextOptionDto(x.Id,x.Title,x.CustomerCompanyId,x.PrimaryContactId,x.SourceLeadId)).ToListAsync(ct);
        var presenters=await db.Agents.AsNoTracking().Where(x=>x.Status==AgentStatus.Active&&x.Department=="Sales").OrderBy(x=>x.DisplayName).Select(x=>new SalesPresentationContextOptionDto(x.Id,x.DisplayName,null,null,null)).ToListAsync(ct);
        return new(accounts,contacts,leads,deals,presenters);
    }

    public async Task<SalesPresentationAdHocRunDto> CreateAsync(Guid companyId,Guid actorUserId,CreateAdHocSalesPresentationCommand command,string? correlationId,CancellationToken ct)
    {
        var started=Stopwatch.GetTimestamp();await Member(companyId,actorUserId,ct);
        var existing=await db.SalesPresentationRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.ClientRequestId==command.ClientRequestId,ct);
        if(existing is not null){if(existing.PresetVersionId!=command.PresetVersionId)throw Conflict(SalesPresentationAdHocProblemCodes.Conflict,"This preparation request was already used for another preset version.");return (await GetAsync(companyId,actorUserId,existing.Id,ct))!;}
        try
        {
            if(!string.Equals(command.RuntimeStrategy,"preparation_only",StringComparison.Ordinal))throw Conflict(SalesPresentationAdHocProblemCodes.RuntimeUnavailable,"Ad-hoc preparation does not fabricate a meeting. Schedule or select an authorized meeting before opening a live presenter.");
            var version=await db.SalesPresentationPresetVersions.Include(x=>x.Preset).Include(x=>x.Asset).ThenInclude(x=>x!.Slides).SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==command.PresetVersionId,ct);
            if(version is null||version.Lifecycle!=SalesPresentationPresetVersionLifecycle.Published||version.Preset.Lifecycle==SalesPresentationPresetLifecycle.Archived||!version.AllowAdHoc||version.Asset?.Status!=SalesPresentationPresetAssetStatus.Processed||version.Asset.Slides.Count==0)
                throw Conflict(SalesPresentationAdHocProblemCodes.PresetUnavailable,"Choose a published, processed preset version that allows ad-hoc use.");
            var presenterId=command.PresenterAgentId??version.DefaultPresenterAgentId??throw Conflict(SalesPresentationAdHocProblemCodes.PresenterInvalid,"Choose an active Sales presenter.");
            if(!await db.Agents.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.Id==presenterId&&x.Status==AgentStatus.Active&&x.Department=="Sales",ct))throw Conflict(SalesPresentationAdHocProblemCodes.PresenterInvalid,"The selected presenter is unavailable or belongs to another company.");
            var resolved=await contexts.ResolveAdHocAsync(companyId,command,ct);var goal=Value(command.Goal,version.Goal);var audience=Value(command.Audience,version.Audience);var duration=command.DurationMinutes??version.DurationMinutes;var demo=command.DemoScenario??version.DemoScenario;var language=Value(command.Language,version.Language);var mode=Value(command.ControlMode,version.ControlMode);
            if(duration is < 5 or > 480)throw Conflict(SalesPresentationAdHocProblemCodes.Conflict,"Choose a duration from 5 to 480 minutes for this ad-hoc run.");
            var blockers=resolved.Blockers.ToList();var evidence=resolved.Evidence.ToList();
            if(!string.IsNullOrWhiteSpace(version.RequiredKnowledgeScope)&&!await db.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.IngestionStatus==CompanyKnowledgeDocumentIngestionStatus.Processed&&x.ActiveChunkCount>0&&(x.SourceRef==version.RequiredKnowledgeScope||x.Title.Contains(version.RequiredKnowledgeScope)),ct))
            {blockers.Add("required_knowledge_unavailable");evidence.Add(new("missing_evidence",$"Required knowledge scope '{version.RequiredKnowledgeScope}' is unavailable. Review before presenting.","needs_review",null,evidence.Count));}
            var now=Now();var run=SalesPresentationRun.CreateAdHoc(Guid.NewGuid(),companyId,version.Id,command.ClientRequestId,presenterId,goal,audience,duration,demo,language,mode,!Same(goal,version.Goal),!Same(audience,version.Audience),duration!=version.DurationMinutes,!Same(demo,version.DemoScenario),presenterId!=version.DefaultPresenterAgentId,resolved.CustomerCompanyId,resolved.ContactId,resolved.LeadId,resolved.DealId,actorUserId,now);run.BeginPreparation(actorUserId,now);
            db.Add(run);foreach(var draft in evidence)db.Add(new SalesPresentationRunArtifact(Guid.NewGuid(),companyId,run.Id,run.PreparationVersion,SalesPresentationPresetEnumValues.ParseRunArtifactType(draft.Type),draft.Content,draft.Classification,draft.SourceReference,null,draft.Order,null,now));
            run.CompletePreparationWithoutRuntime(blockers.Count>0,blockers.Count==0?null:JsonSerializer.Serialize(blockers),actorUserId,now);
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(),companyId,AuditActorTypes.User,actorUserId,"sales.presentation_run.ad_hoc_prepared","sales_presentation_run",run.Id.ToString("D"),AuditEventOutcomes.Succeeded,"An isolated ad-hoc run was prepared from an immutable preset version without starting a meeting or live presenter.",["presentation run","ad hoc"],new Dictionary<string,string?>{{"presetVersionId",version.Id.ToString("D")},{"customerCompanyId",resolved.CustomerCompanyId?.ToString("D")},{"contactId",resolved.ContactId?.ToString("D")},{"leadId",resolved.LeadId?.ToString("D")},{"dealId",resolved.DealId?.ToString("D")},{"runtimeStrategy","preparation_only"}},correlationId,now));
            await db.SaveChangesAsync(ct);Creations.Add(1);logger.LogInformation("Ad-hoc presentation prepared. CompanyId={CompanyId} RunId={RunId} PresetVersionId={PresetVersionId} PresenterId={PresenterId} ContextSelected={ContextSelected} Status={Status}",companyId,run.Id,version.Id,presenterId,resolved.CustomerCompanyId.HasValue||resolved.ContactId.HasValue||resolved.LeadId.HasValue||resolved.DealId.HasValue,run.PreparationStatus);return (await GetAsync(companyId,actorUserId,run.Id,ct))!;
        }
        catch(SalesPresentationPresetConflictException ex){Rejections.Add(1);logger.LogWarning("Ad-hoc presentation rejected. CompanyId={CompanyId} PresetVersionId={PresetVersionId} Code={Code}",companyId,command.PresetVersionId,ex.Code);throw;}
        catch(DbUpdateException){db.ChangeTracker.Clear();var raced=await db.SalesPresentationRuns.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.ClientRequestId==command.ClientRequestId,ct);if(raced is not null)return(await GetAsync(companyId,actorUserId,raced.Id,ct))!;throw Conflict(SalesPresentationAdHocProblemCodes.Conflict,"The ad-hoc presentation changed concurrently. Try again.");}
        finally{Latency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);}
    }

    public async Task<SalesPresentationAdHocRunDto?> GetAsync(Guid companyId,Guid actorUserId,Guid runId,CancellationToken ct)
    {
        await Member(companyId,actorUserId,ct);var run=await db.SalesPresentationRuns.AsNoTracking().Include(x=>x.PresetVersion).ThenInclude(x=>x.Preset).Include(x=>x.PresenterAgent).Include(x=>x.Artifacts).SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==runId&&x.ContextType==SalesPresentationPresetContextType.AdHoc,ct);if(run is null)return null;
        return new(run.Id,run.PresetVersion.PresetId,run.PresetVersion.Preset.Name,run.PresetVersionId,run.PresetVersion.VersionNumber,run.PresenterAgentId,run.PresenterAgent.DisplayName,run.Goal,run.Audience,run.DurationMinutes,run.DemoScenario,run.Language,run.ControlMode,run.RuntimeStrategy,run.ContextCustomerCompanyId,run.ContextContactId,run.ContextLeadId,run.ContextDealId,run.PreparationStatus.ToStorageValue(),Parse(run.ReadinessBlockersJson),false,run.MeetingSessionId,run.ConcurrencyVersion,run.Artifacts.OrderBy(x=>x.Order).Select(x=>new SalesPresentationRunArtifactDto(x.Id,x.Type.ToStorageValue(),x.Content,x.Classification,x.SourceReference,x.PresetSlideId,x.Order,x.AiRunId)).ToArray());
    }
    private async Task Member(Guid companyId,Guid actor,CancellationToken ct){if(companyId==Guid.Empty||actor==Guid.Empty||!await db.CompanyMemberships.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.UserId==actor&&x.Status==CompanyMembershipStatus.Active,ct))throw new UnauthorizedAccessException("An active company membership is required.");}
    private static string Value(string? value,string fallback)=>string.IsNullOrWhiteSpace(value)?fallback:value.Trim();private static bool Same(string? a,string? b)=>string.Equals(a?.Trim(),b?.Trim(),StringComparison.Ordinal);private static SalesPresentationPresetConflictException Conflict(string code,string message)=>new(code,message);private static IReadOnlyList<string> Parse(string? json)=>string.IsNullOrWhiteSpace(json)?[]:JsonSerializer.Deserialize<string[]>(json)??[];private DateTime Now()=>timeProvider.GetUtcNow().UtcDateTime;
}
