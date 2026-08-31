using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ComplianceObligationTelemetry
{
    private readonly Counter<long> _generated; private readonly Counter<long> _transitions; private readonly Counter<long> _reminders;
    public ComplianceObligationTelemetry(IMeterFactory factory) { var meter=factory.Create("VirtualCompany.Finance.Compliance"); _generated=meter.CreateCounter<long>("compliance.obligations.generated"); _transitions=meter.CreateCounter<long>("compliance.obligations.transitions"); _reminders=meter.CreateCounter<long>("compliance.reminders.generated"); }
    public void Generated(int count) => _generated.Add(count); public void Transition(string action) => _transitions.Add(1, new KeyValuePair<string, object?>("action", action)); public void Reminders(int count) => _reminders.Add(count);
}

public sealed class ComplianceObligationService : IComplianceObligationService
{
    private readonly VirtualCompanyDbContext _db; private readonly ICompanyMembershipContextResolver _memberships;
    private readonly ICurrentUserAccessor _currentUser; private readonly IAccountingPolicyPackResolver _packs;
    private readonly IAuditEventWriter _audit; private readonly TimeProvider _time; private readonly ComplianceObligationTelemetry? _telemetry;
    public ComplianceObligationService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        ICurrentUserAccessor currentUser, IAccountingPolicyPackResolver packs, IAuditEventWriter audit,
        TimeProvider time, ComplianceObligationTelemetry? telemetry = null)
    { _db=db; _memberships=memberships; _currentUser=currentUser; _packs=packs; _audit=audit; _time=time; _telemetry=telemetry; }

    public async Task<ComplianceCalendarDto> GetCalendarAsync(GetComplianceCalendarQuery query, CancellationToken ct)
    {
        await RequireViewAsync(query.CompanyId, ct);
        var items=await Query(query.CompanyId).Where(x=>x.DueDate>=query.From && x.DueDate<=query.To).OrderBy(x=>x.DueDate).Take(500).ToListAsync(ct);
        var mapped=items.Select(Map).ToArray(); var today=DateOnly.FromDateTime(Now());
        return new(query.CompanyId,query.From,query.To,mapped.Count(x=>!Terminal(x.Status)),mapped.Count(x=>!Terminal(x.Status)&&x.DueDate>=today&&x.DueDate<=today.AddDays(14)),mapped.Count(x=>!Terminal(x.Status)&&x.DueDate<today),mapped.Count(x=>x.Status is ComplianceObligationStatuses.ManualSubmissionRecorded or ComplianceObligationStatuses.AuthorityReceived),mapped);
    }

    public async Task<ComplianceObligationDto> GetAsync(Guid companyId, Guid instanceId, CancellationToken ct)
    { await RequireViewAsync(companyId,ct); return Map(await LoadAsync(companyId,instanceId,false,ct)); }

    public async Task<IReadOnlyList<ComplianceObligationDto>> GenerateAsync(GenerateComplianceObligationsCommand command, CancellationToken ct)
    {
        await RequireManageAsync(command.CompanyId,command.ActorUserId,ct); var key=Required(command.IdempotencyKey,"idempotencyKey",200);var generatePayload=$"generate|{command.OwnerUserId:N}";
        var replay=await _db.ComplianceCommandReceipts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==command.CompanyId&&x.IdempotencyKey==key,ct);
        if(replay is not null){if(replay.PayloadHash!=Hash(generatePayload))throw Error("idempotency_payload_mismatch","The idempotency key was already used with a different command payload.");return (await Query(command.CompanyId).OrderBy(x=>x.DueDate).ToListAsync(ct)).Select(Map).ToArray();}
        var profile=await _db.CompanyStatutoryProfiles.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==command.CompanyId,ct)
            ?? throw Error("statutory_profile_required","Complete the company statutory profile before generating obligations.");
        if(!string.Equals(profile.CountryCode,"SE",StringComparison.OrdinalIgnoreCase)||profile.VatRegistrationStatus!=StatutoryVatRegistrationStatusValues.Registered)
            throw Error("swedish_registered_vat_profile_required","This obligation definition applies only to a Swedish company with a registered VAT status.");
        var config=await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==command.CompanyId,ct)
            ?? throw Error("accounting_configuration_required","Select an accounting policy pack before generating obligations.");
        var pack=_packs.Resolve(config.PolicyPackKey,config.PolicyPackVersion);
        if(!await _db.ComplianceObligationDefinitions.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==command.CompanyId&&x.Key=="se_vat_return"&&x.PolicyPackKey==config.PolicyPackKey&&x.PolicyPackVersion==config.PolicyPackVersion,ct))
            _db.ComplianceObligationDefinitions.Add(new(Guid.NewGuid(),command.CompanyId,"se_vat_return","Swedish VAT return","SE",config.PolicyPackKey,config.PolicyPackVersion,pack.DefinitionHash,"vat_filing_period.explicit_due_date","Finalized Swedish VAT return package","Manual submission evidence; independently reviewed authority receipt/decision evidence",ComplianceSubmissionModes.ExportAndManualEvidence,Now()));
        var periods=await _db.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==command.CompanyId&&x.DueDate!=null).OrderBy(x=>x.DueDate).ToListAsync(ct);
        if(periods.Count==0) throw Error("explicit_due_date_required","No VAT filing period has an explicit due date. Configure the authority-sourced deadline before generating an obligation.");
        var existing=await _db.ComplianceObligationInstances.IgnoreQueryFilters().Where(x=>x.CompanyId==command.CompanyId&&x.CorrectionOfInstanceId==null).ToDictionaryAsync(x=>x.VatFilingPeriodId,ct);
        var created=0; ComplianceObligationInstance? first=null; var now=Now();
        foreach(var period in periods)
        {
            var vat=await _db.VatReturns.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==command.CompanyId&&x.FilingPeriodId==period.Id).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);
            var closeTask=period.FiscalPeriodId.HasValue?await _db.AccountingCloseTasks.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==command.CompanyId&&x.CloseInstance.FiscalPeriodId==period.FiscalPeriodId&& (x.Key.Contains("vat")||x.Title.Contains("VAT"))).OrderByDescending(x=>x.UpdatedUtc).FirstOrDefaultAsync(ct):null;
            var source=Hash($"profile:{profile.Id:N}:{profile.Version}|pack:{config.PolicyPackKey}:{config.PolicyPackVersion}:{pack.DefinitionHash}|period:{period.Id:N}:{period.StartDate:yyyy-MM-dd}:{period.EndDate:yyyy-MM-dd}:{period.DueDate:yyyy-MM-dd}|vat:{vat?.Id:N}:{vat?.InputHash}:{vat?.PackageChecksum}");
            if(existing.TryGetValue(period.Id,out var current)){current.RefreshSource(vat?.Id,closeTask?.Id,period.DueDate!.Value,source,now);continue;}
            var item=new ComplianceObligationInstance(Guid.NewGuid(),command.CompanyId,"se_vat_return","Swedish VAT return · "+period.PeriodCode,"SE",config.PolicyPackKey,config.PolicyPackVersion,pack.DefinitionHash,"vat_filing_period.explicit_due_date",period.DueDate!.Value,command.OwnerUserId,period.Id,vat?.Id,closeTask?.Id,source,command.ActorUserId,now);
            _db.ComplianceObligationInstances.Add(item); AddHistory(item,"generated","",item.Status,command.ActorUserId,"Generated from an explicit filing-period deadline.",now); first??=item; created++;
        }
        first??=existing.Values.FirstOrDefault(); if(first is not null)_db.ComplianceCommandReceipts.Add(new(Guid.NewGuid(),command.CompanyId,key,Hash(generatePayload),first.Id,now));
        await SaveAsync(ct); _telemetry?.Generated(created); await AuditAsync(command.CompanyId,command.ActorUserId,"compliance_obligations_generated",first?.Id??Guid.Empty,$"Generated {created} obligation(s) from explicit VAT filing deadlines.",ct);
        return (await Query(command.CompanyId).OrderBy(x=>x.DueDate).ToListAsync(ct)).Select(Map).ToArray();
    }

    public async Task<ComplianceObligationDto> TransitionAsync(TransitionComplianceObligationCommand command, CancellationToken ct)
    {
        if(command.Action is ComplianceObligationActions.Approve or ComplianceObligationActions.Reject) await RequireApproveAsync(command.CompanyId,command.ActorUserId,ct); else await RequireManageAsync(command.CompanyId,command.ActorUserId,ct);var payload=$"{command.Action}|{command.InstanceId:N}|{command.ExpectedVersion}"; var replay=await ReplayAsync(command.CompanyId,command.IdempotencyKey,payload,ct); if(replay.HasValue)return Map(await LoadAsync(command.CompanyId,replay.Value,false,ct));
        var item=await LoadAsync(command.CompanyId,command.InstanceId,true,ct); EnsureVersion(item,command.ExpectedVersion); var from=item.Status; var now=Now();
        switch(command.Action){case ComplianceObligationActions.Prepare: EnsureVatPrepared(item); item.Prepare(command.ActorUserId,now); break; case ComplianceObligationActions.SubmitReview:item.SubmitForReview(command.ActorUserId,now);break;case ComplianceObligationActions.Approve:item.Decide(command.ActorUserId,true,now);break;case ComplianceObligationActions.Reject:item.Decide(command.ActorUserId,false,now);break;case ComplianceObligationActions.Export:var vat=await RequireLockedPackageAsync(item,ct);item.RecordExport($"vat-return:{vat.Id:D}/package",vat.PackageChecksum!,now);break;default:throw Error("unsupported_action","The compliance workflow action is not supported.");}
        AddHistory(item,command.Action,from,item.Status,command.ActorUserId,command.Reason,now); AddReceipt(command.CompanyId,command.IdempotencyKey,$"{command.Action}|{command.InstanceId:N}|{command.ExpectedVersion}",item.Id,now); await SaveAsync(ct); _telemetry?.Transition(command.Action); await AuditAsync(command.CompanyId,command.ActorUserId,"compliance_obligation_"+command.Action,item.Id,$"Moved obligation from {from} to {item.Status}.",ct); return Map(item);
    }

    public async Task<ComplianceObligationDto> RecordManualSubmissionAsync(RecordComplianceSubmissionCommand command, CancellationToken ct)
    {
        await RequireManageAsync(command.CompanyId,command.ActorUserId,ct);var payload=$"submit|{command.InstanceId:N}|{command.EvidenceHash}"; var replay=await ReplayAsync(command.CompanyId,command.IdempotencyKey,payload,ct);if(replay.HasValue)return Map(await LoadAsync(command.CompanyId,replay.Value,false,ct));
        var item=await LoadAsync(command.CompanyId,command.InstanceId,true,ct);EnsureVersion(item,command.ExpectedVersion);var from=item.Status;var now=Now();
        var evidence=new ComplianceSubmissionEvidence(Guid.NewGuid(),command.CompanyId,item.Id,Required(command.EvidenceReference,"evidenceReference",500),Required(command.EvidenceHash,"evidenceHash",128),command.ActorUserId,now); item.SubmissionEvidence.Add(evidence);item.RecordManualSubmission(now);AddHistory(item,ComplianceObligationActions.MarkManualSubmitted,from,item.Status,command.ActorUserId,"Manual submission evidence recorded; authority receipt is not yet established.",now);AddReceipt(command.CompanyId,command.IdempotencyKey,$"submit|{item.Id:N}|{command.EvidenceHash}",item.Id,now);await SaveAsync(ct);_telemetry?.Transition(ComplianceObligationActions.MarkManualSubmitted);return Map(item);
    }

    public async Task<ComplianceObligationDto> RecordAcknowledgementAsync(RecordComplianceAcknowledgementCommand command, CancellationToken ct)
    {
        await RequireManageAsync(command.CompanyId,command.ActorUserId,ct);var kind=Required(command.Kind,"kind",24).ToLowerInvariant();var payload=$"ack|{command.InstanceId:N}|{kind}|{command.EvidenceHash}";var replay=await ReplayAsync(command.CompanyId,command.IdempotencyKey,payload,ct);if(replay.HasValue)return Map(await LoadAsync(command.CompanyId,replay.Value,false,ct));
        var item=await LoadAsync(command.CompanyId,command.InstanceId,true,ct);EnsureVersion(item,command.ExpectedVersion);if(kind is not("received" or "approved" or "rejected"))throw Error("invalid_acknowledgement_kind","Acknowledgement kind must be received, approved, or rejected.");var from=item.Status;var now=Now();
        if(item.SubmissionEvidence.All(x=>x.ReviewStatus!="accepted"))throw Error("submission_evidence_review_required","An independent reviewer must accept the manual submission evidence before authority evidence can advance the state.");item.Acknowledgements.Add(new(Guid.NewGuid(),command.CompanyId,item.Id,kind,Required(command.Reference,"reference",500),Required(command.EvidenceHash,"evidenceHash",128),command.ActorUserId,now));item.RecordAcknowledgement(kind,now);AddHistory(item,"authority_"+kind,from,item.Status,command.ActorUserId,"Authority state recorded from retained acknowledgement evidence.",now);AddReceipt(command.CompanyId,command.IdempotencyKey,$"ack|{item.Id:N}|{kind}|{command.EvidenceHash}",item.Id,now);await SaveAsync(ct);_telemetry?.Transition("authority_"+kind);return Map(item);
    }

    public async Task<ComplianceObligationDto> ReviewEvidenceAsync(ReviewComplianceEvidenceCommand command, CancellationToken ct)
    {
        await RequireApproveAsync(command.CompanyId,command.ActorUserId,ct);var payload=$"evidence-review|{command.EvidenceId:N}|{command.Accepted}";var replay=await ReplayAsync(command.CompanyId,command.IdempotencyKey,payload,ct);if(replay.HasValue)return Map(await LoadAsync(command.CompanyId,replay.Value,false,ct));
        var item=await LoadAsync(command.CompanyId,command.InstanceId,true,ct);EnsureVersion(item,command.ExpectedVersion);var evidence=item.SubmissionEvidence.SingleOrDefault(x=>x.Id==command.EvidenceId)??throw new KeyNotFoundException("The submission evidence was not found for this obligation.");var now=Now();evidence.Review(command.ActorUserId,command.Accepted,now);var action=command.Accepted?"submission_evidence_accepted":"submission_evidence_rejected";AddHistory(item,action,item.Status,item.Status,command.ActorUserId,"Independent review of retained manual submission evidence.",now);AddReceipt(command.CompanyId,command.IdempotencyKey,$"evidence-review|{evidence.Id:N}|{command.Accepted}",item.Id,now);await SaveAsync(ct);_telemetry?.Transition(action);return Map(item);
    }

    public async Task<ComplianceObligationDto> CorrectAsync(CorrectComplianceObligationCommand command,CancellationToken ct)
    {
        await RequireManageAsync(command.CompanyId,command.ActorUserId,ct);var reason=Required(command.Reason,"reason",1000);var payload=$"correct|{command.InstanceId:N}|{reason}";var replay=await ReplayAsync(command.CompanyId,command.IdempotencyKey,payload,ct);if(replay.HasValue)return Map(await LoadAsync(command.CompanyId,replay.Value,false,ct));
        var original=await LoadAsync(command.CompanyId,command.InstanceId,true,ct);EnsureVersion(original,command.ExpectedVersion);var now=Now();
        var originalStatus=original.Status;var correction=new ComplianceObligationInstance(Guid.NewGuid(),original.CompanyId,original.DefinitionKey,original.Title+" · correction",original.Jurisdiction,original.PolicyPackKey,original.PolicyPackVersion,original.PolicyPackDefinitionHash,original.DueDateRule,original.DueDate,original.OwnerUserId,original.VatFilingPeriodId,original.VatReturnId,original.AccountingCloseTaskId,original.SourceHash,command.ActorUserId,now);correction.SetCorrectionOf(original.Id,now);original.LinkCorrection(correction.Id,now);_db.ComplianceObligationInstances.Add(correction);AddHistory(original,ComplianceObligationActions.Correct,originalStatus,ComplianceObligationStatuses.Corrected,command.ActorUserId,reason,now);AddHistory(correction,"correction_created","",correction.Status,command.ActorUserId,reason,now);AddReceipt(command.CompanyId,command.IdempotencyKey,$"correct|{original.Id:N}|{reason}",correction.Id,now);await SaveAsync(ct);_telemetry?.Transition(ComplianceObligationActions.Correct);return Map(correction);
    }

    public async Task<int> GenerateRemindersAsync(Guid companyId,Guid actorUserId,CancellationToken ct)
    {
        await RequireManageAsync(companyId,actorUserId,ct);var today=DateOnly.FromDateTime(Now());var items=await Query(companyId).Where(x=>x.DueDate<=today.AddDays(14)).ToListAsync(ct);var count=0;
        foreach(var item in items.Where(x=>!Terminal(x.Status))){var days=item.DueDate.DayNumber-today.DayNumber;var kind=days<0?"overdue":days<=3?"due_soon":"upcoming";var level=days < -7?2:days<0?1:0;if(item.Reminders.All(x=>x.Kind!=kind||x.EscalationLevel!=level)){item.Reminders.Add(new(Guid.NewGuid(),companyId,item.Id,kind,level,Now()));count++;}}
        await SaveAsync(ct);_telemetry?.Reminders(count);return count;
    }

    private IQueryable<ComplianceObligationInstance> Query(Guid companyId)=>_db.ComplianceObligationInstances.IgnoreQueryFilters().AsNoTracking().Include(x=>x.History).Include(x=>x.SubmissionEvidence).Include(x=>x.Acknowledgements).Include(x=>x.Reminders).Where(x=>x.CompanyId==companyId);
    private async Task<ComplianceObligationInstance> LoadAsync(Guid companyId,Guid id,bool tracked,CancellationToken ct){IQueryable<ComplianceObligationInstance> q=_db.ComplianceObligationInstances.IgnoreQueryFilters().Include(x=>x.History).Include(x=>x.SubmissionEvidence).Include(x=>x.Acknowledgements).Include(x=>x.Reminders);if(!tracked)q=q.AsNoTracking();return await q.SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==id,ct)??throw new KeyNotFoundException("The compliance obligation was not found in the requested company.");}
    private async Task<VatReturn> RequireLockedPackageAsync(ComplianceObligationInstance item,CancellationToken ct)=>item.VatReturnId.HasValue?await _db.VatReturns.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==item.CompanyId&&x.Id==item.VatReturnId&&x.Status==VatReturnStatuses.Locked&&x.PackageChecksum!=null,ct)??throw Error("validated_export_required","Finalize and technically validate the linked VAT package before recording an export."):throw Error("vat_return_required","Prepare a linked VAT return before export.");
    private void EnsureVatPrepared(ComplianceObligationInstance item){if(!item.VatReturnId.HasValue)throw Error("vat_return_required","Calculate the linked VAT return before preparing the obligation.");}
    private static bool Terminal(string status)=>status is ComplianceObligationStatuses.AuthorityApproved or ComplianceObligationStatuses.Corrected;
    private static void EnsureVersion(ComplianceObligationInstance x,long expected){if(x.Version!=expected)throw Error("compliance_state_changed","The obligation changed. Refresh its source evidence and try again.");}
    private void AddHistory(ComplianceObligationInstance item,string action,string from,string to,Guid actor,string? reason,DateTime now)=>item.History.Add(new(Guid.NewGuid(),item.CompanyId,item.Id,action,from,to,actor,item.SourceHash,reason,now));
    private void AddReceipt(Guid company,string key,string payload,Guid instance,DateTime now)=>_db.ComplianceCommandReceipts.Add(new(Guid.NewGuid(),company,Required(key,"idempotencyKey",200),Hash(payload),instance,now));
    private async Task<Guid?> ReplayAsync(Guid company,string key,string payload,CancellationToken ct){var receipt=await _db.ComplianceCommandReceipts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.IdempotencyKey==Required(key,"idempotencyKey",200),ct);if(receipt is null)return null;if(receipt.PayloadHash!=Hash(payload))throw Error("idempotency_payload_mismatch","The idempotency key was already used with a different command payload.");return receipt.InstanceId;}
    private async Task RequireViewAsync(Guid company,CancellationToken ct){var membership=await _memberships.ResolveAsync(company,ct)??throw new UnauthorizedAccessException();if(!FinanceAccess.CanViewAccounting(membership.MembershipRole.ToStorageValue()))throw new UnauthorizedAccessException();}
    private async Task RequireManageAsync(Guid company,Guid actor,CancellationToken ct){if(_currentUser.UserId!=actor)throw new UnauthorizedAccessException();var membership=await _memberships.ResolveAsync(company,ct)??throw new UnauthorizedAccessException();if(!FinanceAccess.CanManageAccounting(membership.MembershipRole.ToStorageValue()))throw new UnauthorizedAccessException();}
    private async Task RequireApproveAsync(Guid company,Guid actor,CancellationToken ct){if(_currentUser.UserId!=actor)throw new UnauthorizedAccessException();var membership=await _memberships.ResolveAsync(company,ct)??throw new UnauthorizedAccessException();if(!FinanceAccess.CanApproveInvoices(membership.MembershipRole.ToStorageValue()))throw new UnauthorizedAccessException();}
    private async Task SaveAsync(CancellationToken ct){try{await _db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){throw Error("compliance_state_changed","The obligation changed. Refresh and try again.");}catch(DbUpdateException ex)when(ex.InnerException is not null){throw Error("compliance_conflict","The obligation operation conflicts with an existing source or idempotency receipt.");}}
    private async Task AuditAsync(Guid company,Guid actor,string action,Guid target,string rationale,CancellationToken ct)=>await _audit.WriteAsync(new AuditEventWriteRequest(company,AuditActorTypes.User,actor,action,"compliance_obligation",target.ToString("D"),AuditEventOutcomes.Succeeded,rationale,DataSources:["company_statutory_profile","accounting_policy_pack","vat_filing_period","vat_return","compliance_evidence"],OccurredUtc:Now()),ct);
    private DateTime Now()=>_time.GetUtcNow().UtcDateTime;
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Required(string? value,string name,int max){var v=value?.Trim();if(string.IsNullOrWhiteSpace(v)||v.Length>max)throw Error("invalid_"+name,$"{name} is required and limited to {max} characters.");return v;}
    private static ComplianceObligationException Error(string code,string message)=>new(code,message);
    private static ComplianceObligationDto Map(ComplianceObligationInstance x)
    {
        var requirements=new[]{new ComplianceRequirementDto("return","Finalized VAT return package",x.ExportChecksum is not null,x.ExportReference),new ComplianceRequirementDto("submission_evidence","Manual submission evidence",x.SubmissionEvidence.Any(),x.SubmissionEvidence.OrderByDescending(e=>e.SubmittedUtc).FirstOrDefault()?.Reference),new ComplianceRequirementDto("authority_acknowledgement","Authority acknowledgement",x.Acknowledgements.Any(),x.Acknowledgements.OrderByDescending(e=>e.OccurredUtc).FirstOrDefault()?.Reference)};
        var actions=x.Status switch{ComplianceObligationStatuses.Generated=>new[]{ComplianceObligationActions.Prepare},ComplianceObligationStatuses.Prepared=>new[]{ComplianceObligationActions.SubmitReview},ComplianceObligationStatuses.UnderReview=>new[]{ComplianceObligationActions.Approve,ComplianceObligationActions.Reject},ComplianceObligationStatuses.Approved=>new[]{ComplianceObligationActions.Export},ComplianceObligationStatuses.Exported=>new[]{ComplianceObligationActions.MarkManualSubmitted},ComplianceObligationStatuses.ManualSubmissionRecorded or ComplianceObligationStatuses.AuthorityReceived=>new[]{ComplianceObligationActions.RecordAcknowledgement},ComplianceObligationStatuses.Rejected=>new[]{ComplianceObligationActions.Correct},_=>Array.Empty<string>()};
        return new(x.Id,x.CompanyId,x.DefinitionKey,x.Title,x.Jurisdiction,x.PolicyPackKey,x.PolicyPackVersion,x.PolicyPackDefinitionHash,x.DueDateRule,x.DueDate,x.OwnerUserId,x.Status,x.SubmissionMode,x.VatFilingPeriodId,x.VatReturnId,x.AccountingCloseTaskId,x.CorrectionOfInstanceId,x.CorrectedByInstanceId,x.SourceHash,x.ExportReference,x.ExportChecksum,x.Version,x.CreatedUtc,x.UpdatedUtc,requirements,x.History.OrderBy(h=>h.OccurredUtc).Select(h=>new ComplianceHistoryDto(h.Id,h.Action,h.FromStatus,h.ToStatus,h.ActorUserId,h.SourceHash,h.Reason,h.OccurredUtc)).ToArray(),x.SubmissionEvidence.OrderByDescending(e=>e.SubmittedUtc).Select(e=>new ComplianceEvidenceDto(e.Id,e.Reference,e.ContentHash,e.ActorUserId,e.SubmittedUtc,e.ReviewStatus,e.ReviewedByUserId,e.ReviewedUtc)).ToArray(),x.Acknowledgements.OrderByDescending(e=>e.OccurredUtc).Select(e=>new ComplianceAcknowledgementDto(e.Id,e.Kind,e.Reference,e.ContentHash,e.ActorUserId,e.OccurredUtc)).ToArray(),x.Reminders.OrderByDescending(r=>r.CreatedUtc).Select(r=>new ComplianceReminderDto(r.Id,r.Kind,r.EscalationLevel,r.Status,r.CreatedUtc)).ToArray(),actions);
    }
}
