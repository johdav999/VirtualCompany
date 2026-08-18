using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Application.Support;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportSlaGuidedArtifactDefinition(ISupportSlaPolicyService policies, IAgentCapabilityCatalog capabilities) : IGuidedArtifactDefinition
{
    public string ArtifactType=>GuidedArtifactTypes.SupportSlaPolicy; public string SchemaVersion=>"1.0"; public string DisplayName=>"Support service-level policy";
    public IReadOnlyList<string> QuestionPriorities=>["category","priority","customer_tier","first_response_minutes","resolution_minutes","risk_threshold_minutes","time_basis","escalation_recipient_role","is_active"];
    public IReadOnlyList<GuidedFieldDefinition> Fields {get;}=
    [
        T("name","Policy name",true,200),T("category","Case category",true,64),T("priority","Priority",true,32),T("customer_tier","Customer tier",false,64),
        N("first_response_minutes","First-response minutes",true,1,525600),N("resolution_minutes","Resolution minutes",true,1,525600),
        new("is_active","Active policy","Choose whether this policy is active.",GuidedFieldValueTypes.Boolean,true),
        new("time_basis","Time basis","Choose elapsed or business time.",GuidedFieldValueTypes.Text,true,["elapsed","business"],64),
        N("risk_threshold_minutes","Risk threshold minutes",true,1,525600),T("escalation_recipient_role","Escalation recipient role",true,128)
    ];
    public async Task EnsureEligibleAsync(Guid c,Guid a,CancellationToken ct){var catalog=await capabilities.GetEffectiveCatalogAsync(c,a,ct);var cap=catalog.Capabilities.SingleOrDefault(x=>x.Id==AgentCapabilityIds.SupportRiskEscalation);if(cap is null||cap.State is AgentCapabilityStates.PermissionDenied or AgentCapabilityStates.NotImplemented)throw new GuidedArtifactNotEligibleException("The selected agent does not have the tools and data permissions required for Support policy workshops.");}
    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId,Guid agentId,Guid? targetId,CancellationToken ct)
    {
        var values=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase){{"is_active",true},{"time_basis","elapsed"},{"risk_threshold_minutes",240},{"escalation_recipient_role","support_supervisor"}};string? version=null;
        if(targetId is Guid id){var p=(await policies.ListAsync(companyId,ct)).SingleOrDefault(x=>x.Id==id)??throw new KeyNotFoundException("Support service-level policy not found.");values["name"]=p.Name;values["category"]=p.Category;values["priority"]=p.Priority;values["customer_tier"]=p.CustomerTier;values["first_response_minutes"]=p.FirstResponseMinutes;values["resolution_minutes"]=p.ResolutionMinutes;values["is_active"]=p.IsActive;values["time_basis"]=p.TimeBasis;values["risk_threshold_minutes"]=p.RiskThresholdMinutes;values["escalation_recipient_role"]=p.EscalationRecipientRole;version=p.UpdatedUtc.ToString("O");}
        return new(DisplayName,targetId,version,values,OpeningSummary:"Define one bounded service-level policy using the current Support SLA semantics.",OpeningQuestion:"Which case category, priority, and customer tier should this policy cover?");
    }
    public Task<IReadOnlyList<string>> ValidateAsync(Guid c,Guid a,Guid? t,IReadOnlyDictionary<string,JsonNode?>v,CancellationToken ct)
    {
        var gaps=Fields.Where(x=>x.IsRequired&&(!v.TryGetValue(x.Path,out var n)||n is null||string.IsNullOrWhiteSpace(n.ToString()))).Select(x=>x.Label).ToList();
        var first=Int(v,"first_response_minutes");var resolution=Int(v,"resolution_minutes");var risk=Int(v,"risk_threshold_minutes");if(resolution<first)gaps.Add("Resolution time must be at least the first-response time");if(risk>resolution)gaps.Add("Risk threshold should not exceed resolution time");return Task.FromResult<IReadOnlyList<string>>(gaps);
    }
    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid c,Guid a,Guid? t,IReadOnlyDictionary<string,JsonNode?>v,CancellationToken ct)=>Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>([new("Policy scope",$"{Text(v,"category")} · {Text(v,"priority")}{(string.IsNullOrWhiteSpace(Text(v,"customer_tier"))?"":$" · {Text(v,"customer_tier")}")}","The current deterministic SLA resolution rules choose the applicable policy."),new("Timing",$"Respond in {Int(v,"first_response_minutes")} minutes; resolve in {Int(v,"resolution_minutes")} minutes",$"Risk escalation begins at {Int(v,"risk_threshold_minutes")} minutes using {Text(v,"time_basis")} time."),new("Confirmation effect","Save one SLA policy only","Confirmation does not reply to cases, issue refunds, change mailbox routing, or deactivate unrelated policies.")]);
    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext c,CancellationToken ct)
    {
        if(c.TargetArtifactId is Guid id&&!string.IsNullOrWhiteSpace(c.TargetArtifactVersion)){var current=(await policies.ListAsync(c.CompanyId,ct)).SingleOrDefault(x=>x.Id==id);if(current is null||current.UpdatedUtc.ToString("O")!=c.TargetArtifactVersion)throw new GuidedWorkConflictException("The service-level policy changed after this workshop started.");}
        var request=new UpsertSupportSlaPolicyRequest(c.TargetArtifactId,Text(c.Values,"name"),Text(c.Values,"category"),Text(c.Values,"priority"),Int(c.Values,"first_response_minutes"),Int(c.Values,"resolution_minutes"),NullText(c.Values,"customer_tier"),Bool(c.Values,"is_active"),Text(c.Values,"time_basis"),Int(c.Values,"risk_threshold_minutes"),Text(c.Values,"escalation_recipient_role"));
        var result=await policies.UpsertAsync(c.CompanyId,c.UserId,request,ct);return new(result.Id,result.UpdatedUtc.ToString("O"),$"Saved service-level policy {result.Name}.");
    }
    private static GuidedFieldDefinition T(string p,string l,bool r=false,int max=8000)=>new(p,l,$"Set {l.ToLowerInvariant()}.",GuidedFieldValueTypes.Text,r,MaxLength:max,AllowsEvidence:true);private static GuidedFieldDefinition N(string p,string l,bool r,int min,int max)=>new(p,l,$"Set {l.ToLowerInvariant()}.",GuidedFieldValueTypes.Number,r,Minimum:min,Maximum:max);private static string Text(IReadOnlyDictionary<string,JsonNode?>v,string k)=>v.TryGetValue(k,out var n)?n?.ToString().Trim('"')??"":"";private static string? NullText(IReadOnlyDictionary<string,JsonNode?>v,string k)=>string.IsNullOrWhiteSpace(Text(v,k))?null:Text(v,k);private static int Int(IReadOnlyDictionary<string,JsonNode?>v,string k)=>v.TryGetValue(k,out var n)&&int.TryParse(n?.ToString(),out var i)?i:0;private static bool Bool(IReadOnlyDictionary<string,JsonNode?>v,string k)=>v.TryGetValue(k,out var n)&&bool.TryParse(n?.ToString(),out var b)&&b;
}
