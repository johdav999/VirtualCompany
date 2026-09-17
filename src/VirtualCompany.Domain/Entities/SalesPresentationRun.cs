using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

/// <summary>One company-scoped, situation-specific use of an immutable preset version.</summary>
public sealed class SalesPresentationRun : ICompanyOwnedEntity
{
    private SalesPresentationRun() { }
    public SalesPresentationRun(Guid id, Guid companyId, Guid presetVersionId, Guid meetingSessionId,
        Guid presenterAgentId, string goal, string audience, int durationMinutes, string? demoScenario,
        string language, string controlMode, bool goalOverridden, bool audienceOverridden,
        bool durationOverridden, bool demoOverridden, bool presenterOverridden, Guid actorUserId, DateTime nowUtc)
    {
        Require(companyId, nameof(companyId)); Require(presetVersionId, nameof(presetVersionId)); Require(meetingSessionId, nameof(meetingSessionId));
        Require(presenterAgentId, nameof(presenterAgentId)); Require(actorUserId, nameof(actorUserId));
        if (durationMinutes is < 5 or > 480) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        Id=id==Guid.Empty?Guid.NewGuid():id; CompanyId=companyId; PresetVersionId=presetVersionId;
        ContextType=SalesPresentationPresetContextType.SalesMeeting; ContextReference=meetingSessionId.ToString("D"); MeetingSessionId=meetingSessionId;
        RuntimeStrategy="meeting_session";
        PresenterAgentId=presenterAgentId; Goal=Text(goal,2000); Audience=Text(audience,1000); DurationMinutes=durationMinutes;
        DemoScenario=Optional(demoScenario,2000); Language=Text(language,20).ToLowerInvariant(); ControlMode=Mode(controlMode);
        GoalOverridden=goalOverridden; AudienceOverridden=audienceOverridden; DurationOverridden=durationOverridden;
        DemoScenarioOverridden=demoOverridden; PresenterOverridden=presenterOverridden;
        PreparationStatus=SalesPresentationRunPreparationStatus.Pending; PreparationVersion=1; IsActive=true;
        CreatedByUserId=UpdatedByUserId=actorUserId; CreatedUtc=UpdatedUtc=Utc(nowUtc); ConcurrencyVersion=1;
    }
    public SalesPresentationRun(Guid id, Guid companyId, Guid presetVersionId, string contextReference,
        Guid? meetingSessionId, Guid presenterAgentId, string goal, string audience, int durationMinutes,
        string? demoScenario, string language, string controlMode, Guid actorUserId, DateTime nowUtc)
    {
        Require(companyId,nameof(companyId));Require(presetVersionId,nameof(presetVersionId));Require(presenterAgentId,nameof(presenterAgentId));Require(actorUserId,nameof(actorUserId));
        if(durationMinutes is < 5 or > 480)throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        Id=id==Guid.Empty?Guid.NewGuid():id;CompanyId=companyId;PresetVersionId=presetVersionId;ContextType=SalesPresentationPresetContextType.CampaignActivity;
        ContextReference=Text(contextReference,100);MeetingSessionId=meetingSessionId;PresenterAgentId=presenterAgentId;Goal=Text(goal,2000);Audience=Text(audience,1000);
        RuntimeStrategy=meetingSessionId.HasValue?"meeting_session":"preparation_only";
        DurationMinutes=durationMinutes;DemoScenario=Optional(demoScenario,2000);Language=Text(language,20).ToLowerInvariant();ControlMode=Mode(controlMode);
        PreparationStatus=SalesPresentationRunPreparationStatus.Pending;PreparationVersion=1;IsActive=true;CreatedByUserId=UpdatedByUserId=actorUserId;
        CreatedUtc=UpdatedUtc=Utc(nowUtc);ConcurrencyVersion=1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid PresetVersionId { get; private set; }
    public SalesPresentationPresetContextType ContextType { get; private set; } public string ContextReference { get; private set; }=null!;
    public Guid? MeetingSessionId { get; private set; } public Guid PresenterAgentId { get; private set; }
    public Guid? ClientRequestId { get; private set; } public Guid? ContextCustomerCompanyId { get; private set; }
    public Guid? ContextContactId { get; private set; } public Guid? ContextLeadId { get; private set; } public Guid? ContextDealId { get; private set; }
    public string RuntimeStrategy { get; private set; }="preparation_only";
    public string Goal { get; private set; }=null!; public string Audience { get; private set; }=null!; public int DurationMinutes { get; private set; }
    public string? DemoScenario { get; private set; } public string Language { get; private set; }=null!; public string ControlMode { get; private set; }=null!;
    public bool GoalOverridden { get; private set; } public bool AudienceOverridden { get; private set; } public bool DurationOverridden { get; private set; }
    public bool DemoScenarioOverridden { get; private set; } public bool PresenterOverridden { get; private set; }
    public SalesPresentationRunPreparationStatus PreparationStatus { get; private set; } public string? ReadinessBlockersJson { get; private set; }
    public int PreparationVersion { get; private set; } public int PreparationAttemptCount { get; private set; } public bool CanRetry { get; private set; }
    public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; } public DateTime? PreparationStartedUtc { get; private set; }
    public DateTime? PreparedUtc { get; private set; } public DateTime? FailedUtc { get; private set; } public Guid? CompatibilityDeckId { get; private set; }
    public bool IsActive { get; private set; } public Guid CreatedByUserId { get; private set; } public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; } public long ConcurrencyVersion { get; private set; }
    public SalesPresentationPresetVersion PresetVersion { get; private set; }=null!; public SalesMeetingSession? MeetingSession { get; private set; }
    public Agent PresenterAgent { get; private set; }=null!;
    public ICollection<SalesPresentationRunArtifact> Artifacts { get; }=new List<SalesPresentationRunArtifact>();
    public static SalesPresentationRun CreateAdHoc(Guid id,Guid companyId,Guid presetVersionId,Guid clientRequestId,
        Guid presenterAgentId,string goal,string audience,int durationMinutes,string? demoScenario,string language,
        string controlMode,bool goalOverridden,bool audienceOverridden,bool durationOverridden,bool demoOverridden,bool presenterOverridden,
        Guid? customerCompanyId,Guid? contactId,Guid? leadId,Guid? dealId,Guid actorUserId,DateTime nowUtc)
    {
        Require(companyId,nameof(companyId));Require(presetVersionId,nameof(presetVersionId));Require(clientRequestId,nameof(clientRequestId));
        Require(presenterAgentId,nameof(presenterAgentId));Require(actorUserId,nameof(actorUserId));
        if(durationMinutes is < 5 or > 480)throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if(customerCompanyId==Guid.Empty||contactId==Guid.Empty||leadId==Guid.Empty||dealId==Guid.Empty)throw new ArgumentException("Optional context identifiers cannot be empty.");
        var run=new SalesPresentationRun{Id=id==Guid.Empty?Guid.NewGuid():id,CompanyId=companyId,PresetVersionId=presetVersionId,
            ContextType=SalesPresentationPresetContextType.AdHoc,ContextReference=$"ad-hoc:{clientRequestId:N}",ClientRequestId=clientRequestId,
            PresenterAgentId=presenterAgentId,Goal=Text(goal,2000),Audience=Text(audience,1000),DurationMinutes=durationMinutes,
            DemoScenario=Optional(demoScenario,2000),Language=Text(language,20).ToLowerInvariant(),ControlMode=Mode(controlMode),
            GoalOverridden=goalOverridden,AudienceOverridden=audienceOverridden,DurationOverridden=durationOverridden,
            DemoScenarioOverridden=demoOverridden,PresenterOverridden=presenterOverridden,
            ContextCustomerCompanyId=customerCompanyId,ContextContactId=contactId,ContextLeadId=leadId,ContextDealId=dealId,
            RuntimeStrategy="preparation_only",PreparationStatus=SalesPresentationRunPreparationStatus.Pending,PreparationVersion=1,
            IsActive=true,CreatedByUserId=actorUserId,UpdatedByUserId=actorUserId,CreatedUtc=Utc(nowUtc),UpdatedUtc=Utc(nowUtc),ConcurrencyVersion=1};
        return run;
    }
    public void BeginPreparation(Guid actor,DateTime now){Require(actor,nameof(actor));PreparationStatus=SalesPresentationRunPreparationStatus.Preparing;PreparationAttemptCount++;PreparationStartedUtc=Utc(now);CanRetry=false;FailureCode=FailureSummary=null;Touch(actor,now);}
    public void CompletePreparation(Guid deckId,bool review,string? blockers,Guid actor,DateTime now){Require(deckId,nameof(deckId));if(PreparationStatus!=SalesPresentationRunPreparationStatus.Preparing)throw new InvalidOperationException("The run is not being prepared.");CompatibilityDeckId=deckId;PreparationStatus=review?SalesPresentationRunPreparationStatus.NeedsReview:SalesPresentationRunPreparationStatus.Ready;ReadinessBlockersJson=Optional(blockers,8000);PreparedUtc=Utc(now);Touch(actor,now);}
    public void CompletePreparationWithoutRuntime(bool review,string? blockers,Guid actor,DateTime now){if(ContextType!=SalesPresentationPresetContextType.AdHoc)throw new InvalidOperationException("Only an ad-hoc run can complete without a meeting runtime.");if(PreparationStatus!=SalesPresentationRunPreparationStatus.Preparing)throw new InvalidOperationException("The run is not being prepared.");PreparationStatus=review?SalesPresentationRunPreparationStatus.NeedsReview:SalesPresentationRunPreparationStatus.Ready;ReadinessBlockersJson=Optional(blockers,8000);PreparedUtc=Utc(now);Touch(actor,now);}
    public void FailPreparation(string code,string summary,bool canRetry,Guid actor,DateTime now){PreparationStatus=SalesPresentationRunPreparationStatus.Failed;FailureCode=Text(code,100);FailureSummary=Text(summary,1000);CanRetry=canRetry;FailedUtc=Utc(now);Touch(actor,now);}
    public void QueueRetry(Guid actor,DateTime now){if(PreparationStatus!=SalesPresentationRunPreparationStatus.Failed||!CanRetry)throw new InvalidOperationException("This run cannot be retried.");PreparationStatus=SalesPresentationRunPreparationStatus.Pending;PreparationVersion++;CanRetry=false;FailureCode=FailureSummary=null;FailedUtc=null;Touch(actor,now);}
    public void Deactivate(Guid actor,DateTime now){if(!IsActive)return;IsActive=false;Touch(actor,now);}
    private void Touch(Guid actor,DateTime now){Require(actor,nameof(actor));UpdatedByUserId=actor;UpdatedUtc=Utc(now);ConcurrencyVersion++;}
    private static void Require(Guid value,string name){if(value==Guid.Empty)throw new ArgumentException($"{name} is required.",name);} private static string Text(string? value,int max){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("A value is required.");var v=value.Trim();if(v.Length>max)throw new ArgumentOutOfRangeException(nameof(value));return v;}
    private static string? Optional(string? value,int max)=>string.IsNullOrWhiteSpace(value)?null:Text(value,max); private static string Mode(string value){var v=Text(value,20).ToLowerInvariant();return v is "manual" or "assisted" or "autonomous"?v:throw new ArgumentOutOfRangeException(nameof(value));} private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.ToUniversalTime();
}
