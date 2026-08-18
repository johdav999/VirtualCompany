using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Infrastructure.Marketing;

public sealed class MarketingStrategyGuidedArtifactDefinition(IMarketingStrategyService marketing, IAgentCapabilityCatalog capabilities) : IGuidedArtifactDefinition
{
    public string ArtifactType => GuidedArtifactTypes.MarketingStrategy; public string SchemaVersion => "1.1"; public string DisplayName => "Marketing strategy";
    public GuidedArtifactCapabilities Capabilities { get; } = new(true,[".pdf",".docx",".pptx",".xlsx",".txt",".md",".csv"],["knowledge","marketing"],true,true);
    public IReadOnlyList<string> QuestionPriorities => ["objectives","market_situation","segmentation","segment_version_ids","targeting_rationale","positioning","value_proposition","product","price","place","promotion","implementation_roadmap","success_metrics","risks","evidence","missing_evidence"];
    public IReadOnlyList<GuidedFieldDefinition> Fields { get; } =
    [
        Text("title","Strategy title",true,200), Text("summary","Executive summary",true,4000), Text("business_context","Business context",true,8000),
        Date("valid_from","Valid from",true), Date("valid_to","Valid to",true), List("segment_version_ids","Target segment versions",true),
        Text("objectives","Strategic objectives",true,8000,"Define measurable commercial and customer outcomes, the time horizon, and how they align with company goals."),
        Text("market_situation","Market situation",true,8000,"Summarize category dynamics, customer demand, competitors, trends, constraints, and the evidence supporting the diagnosis."),
        Text("segmentation","Segmentation framework",true,8000,"Describe how the market is divided using observable needs, firmographic, behavioral, geographic, or value-based criteria."),
        Text("targeting_rationale","Targeting rationale",true,8000,"Explain which segments are prioritized or excluded, their attractiveness, company fit, reachability, economics, and trade-offs."),
        Text("positioning","Positioning",true,8000,"State the intended market position for the target audience, frame of reference, primary benefit, and reasons to believe."),
        Text("value_proposition","Value proposition",true,8000,"Define the customer problem, promised outcomes, proof, and why the offer is preferable to alternatives."),
        Text("differentiation","Differentiation",true,8000,"Describe defensible differentiators, competitive alternatives, and claims that must not be made without evidence."),
        Text("product","Product strategy",true,8000,"Define the offer, capabilities, packaging, service experience, onboarding, and product decisions for the target segments."),
        Text("price","Pricing strategy",true,8000,"Define pricing logic, willingness-to-pay assumptions, tiers, commercial model, discounts, and value communication."),
        Text("place","Place and distribution",true,8000,"Define routes to market, sales motion, partners, availability, and how customers buy and receive the offer."),
        Text("promotion","Promotion strategy",true,8000,"Define messages, content, campaigns, channels, calls to action, cadence, and the role of paid, owned, earned, and partner media."),
        Text("brand_messaging","Brand and messaging"), Text("customer_journey","Customer journey"), Text("sales_alignment","Sales alignment"),
        Text("competitive_analysis","Competitive analysis"), Text("swot_and_five_forces","SWOT and Five Forces"),
        Text("implementation_roadmap","Implementation roadmap",true,8000,"Define priorities, owners, dependencies, phases, decision gates, and the first actions required to execute the strategy."),
        Text("budget_resources","Budget and resources"), Text("governance","Governance and review cadence"),
        Text("success_metrics","Success metrics",true,8000,"Define leading and lagging indicators, baselines, targets, attribution limits, review cadence, and stop or change criteria."),
        Text("evidence","Evidence references"), Text("missing_evidence","Missing evidence"), Text("risks","Risks and assumptions",true)
    ];
    public Task EnsureEligibleAsync(Guid companyId, Guid agentId, CancellationToken ct) => EnsureCapability(capabilities, companyId, agentId, AgentCapabilityIds.MarketingPlanning, ct);
    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId, Guid agentId, Guid? targetId, CancellationToken ct)
    {
        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase); string? version = null;
        if (targetId is Guid id)
        {
            var current = await marketing.GetStrategyAsync(companyId, id, ct) ?? throw new KeyNotFoundException("Marketing strategy not found.");
            values["title"] = current.Title; values["summary"] = current.Summary; values["business_context"] = current.BusinessContext;
            values["valid_from"] = current.ValidFromUtc.ToString("yyyy-MM-dd"); values["valid_to"] = current.ValidToUtc.ToString("yyyy-MM-dd");
            values["segment_version_ids"] = new JsonArray(current.SegmentVersionIds.Select(x => JsonValue.Create(x.ToString())).ToArray());
            if (JsonNode.Parse(current.SectionsJson) is JsonObject sections) foreach (var key in SectionKeys) if (sections[key] is { } node) values[key] = node.DeepClone();
            values["evidence"] = current.EvidenceReferencesJson; values["missing_evidence"] = current.MissingEvidenceJson; version = current.Version.ToString();
        }
        return new(DisplayName, targetId, version, values, OpeningSummary: "Build an evidence-aware STP and 4P strategy covering objectives, market diagnosis, differentiation, execution, measurement, and governance.", OpeningQuestion: "What business outcome should this strategy achieve, and which customer segment should it prioritize to achieve it?");
    }
    public async Task<IReadOnlyList<string>> ValidateAsync(Guid companyId, Guid agentId, Guid? targetId, IReadOnlyDictionary<string, JsonNode?> values, CancellationToken ct)
    {
        var gaps = RequiredGaps(Fields, values);
        if (TryDate(values,"valid_from",out var from) && TryDate(values,"valid_to",out var to) && to <= from) gaps.Add("Valid to must be after valid from");
        var eligible = (await marketing.ListSegmentsAsync(companyId, ct)).SelectMany(x => x.Versions)
            .Where(x => string.Equals(x.Status, "approved", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase))
            .Select(x=>x.Id).ToHashSet();
        foreach(var id in ReadGuids(values,"segment_version_ids")) if(!eligible.Contains(id)) gaps.Add($"Target segment version {id} is not eligible");
        return gaps;
    }
    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid companyId,Guid agentId,Guid? targetId,IReadOnlyDictionary<string,JsonNode?> values,CancellationToken ct)=>
        Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>
        ([
            new("Target segments",$"{ReadGuids(values,"segment_version_ids").Count} approved or active version(s)","Segment references are validated in this company before the draft can be saved."),
            new("Evidence gaps",string.IsNullOrWhiteSpace(GetString(values,"missing_evidence"))?"None recorded":"Remain visible", "Missing or stale evidence is not promoted to fact."),
            new("Confirmation effect","Save strategy draft only","Confirmation does not submit, approve, activate, decompose, schedule, publish, or spend budget.")
        ]);
    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context, CancellationToken ct)
    {
        var sections = new JsonObject(); foreach(var key in SectionKeys) sections[key]=GetString(context.Values,key);
        var request = new SaveMarketingStrategyRequest(GetString(context.Values,"title"),GetString(context.Values,"summary"),GetString(context.Values,"business_context"),
            GetDate(context.Values,"valid_from"),GetDate(context.Values,"valid_to"),sections.ToJsonString(),JsonText(context.Values,"evidence"),JsonText(context.Values,"missing_evidence"),
            $"guided:{context.SessionId:N}",ReadGuids(context.Values,"segment_version_ids"),int.TryParse(context.TargetArtifactVersion,out var expected)?expected:null);
        MarketingStrategyDto result;
        if(context.TargetArtifactId is Guid id) result=await marketing.UpdateStrategyAsync(context.CompanyId,context.UserId,id,request,ct) ?? throw new GuidedWorkConflictException("The strategy changed or no longer exists.");
        else result=await marketing.CreateStrategyAsync(context.CompanyId,context.UserId,request,ct);
        return new(result.Id,result.Version.ToString(),$"Saved marketing strategy {result.Title} as a reviewable draft.");
    }
    private static readonly string[] SectionKeys=["objectives","market_situation","segmentation","targeting_rationale","positioning","value_proposition","differentiation","product","price","place","promotion","brand_messaging","customer_journey","sales_alignment","competitive_analysis","swot_and_five_forces","implementation_roadmap","budget_resources","governance","success_metrics","risks"];
    private static GuidedFieldDefinition Text(string p,string l,bool r=false,int max=8000,string? description=null)=>new(p,l,description??$"Define {l.ToLowerInvariant()} in plain business language.",GuidedFieldValueTypes.Text,r,MaxLength:max,AllowsEvidence:true);
    private static GuidedFieldDefinition Date(string p,string l,bool r)=>new(p,l,$"Set {l.ToLowerInvariant()}.",GuidedFieldValueTypes.Date,r);
    private static GuidedFieldDefinition List(string p,string l,bool r)=>new(p,l,"Select current eligible segment version identifiers.",GuidedFieldValueTypes.TextList,r);
    internal static async Task EnsureCapability(IAgentCapabilityCatalog catalog,Guid companyId,Guid agentId,string capabilityId,CancellationToken ct){var result=await catalog.GetEffectiveCatalogAsync(companyId,agentId,ct);var capability=result.Capabilities.SingleOrDefault(x=>x.Id==capabilityId);if(capability is null||capability.State is AgentCapabilityStates.PermissionDenied or AgentCapabilityStates.NotImplemented)throw new GuidedArtifactNotEligibleException("The selected agent does not have the tools and data permissions required for this workshop.");}
    internal static List<string> RequiredGaps(IReadOnlyList<GuidedFieldDefinition> fields,IReadOnlyDictionary<string,JsonNode?> values)=>fields.Where(x=>x.IsRequired&&(!values.TryGetValue(x.Path,out var v)||v is null||string.IsNullOrWhiteSpace(v is JsonValue j&&j.TryGetValue<string>(out var s)?s:v.ToJsonString()))).Select(x=>x.Label).ToList();
    internal static string GetString(IReadOnlyDictionary<string,JsonNode?> v,string k)=>v.TryGetValue(k,out var n)&&n is JsonValue j&&j.TryGetValue<string>(out var s)?s.Trim():string.Empty;
    internal static DateTime GetDate(IReadOnlyDictionary<string,JsonNode?> v,string k)=>DateTime.SpecifyKind(DateTime.Parse(GetString(v,k)),DateTimeKind.Utc);
    internal static bool TryDate(IReadOnlyDictionary<string,JsonNode?> v,string k,out DateTime d)=>DateTime.TryParse(GetString(v,k),out d);
    internal static IReadOnlyList<Guid> ReadGuids(IReadOnlyDictionary<string,JsonNode?> v,string k)=>v.TryGetValue(k,out var n)&&n is JsonArray a?a.Select(x=>Guid.TryParse(x?.GetValue<string>(),out var id)?id:Guid.Empty).Where(x=>x!=Guid.Empty).ToArray():[];
    internal static string JsonText(IReadOnlyDictionary<string,JsonNode?> v,string k){var s=GetString(v,k);if(string.IsNullOrWhiteSpace(s))return "[]";try{JsonNode.Parse(s);return s;}catch{return new JsonArray(JsonValue.Create(s)).ToJsonString();}}
}

public sealed class MarketingSegmentGuidedArtifactDefinition(IMarketingStrategyService marketing, IAgentCapabilityCatalog capabilities) : IGuidedArtifactDefinition
{
    public string ArtifactType=>GuidedArtifactTypes.MarketingSegment; public string SchemaVersion=>"1.1"; public string DisplayName=>"Marketing segment discovery";
    public GuidedArtifactCapabilities Capabilities { get; } = new(true,[".pdf",".docx",".pptx",".xlsx",".txt",".md",".csv"],["knowledge","marketing"],true,true);
    public IReadOnlyList<string> QuestionPriorities=>["criteria","needs","behaviors","channels","pricing","size_method","economics","evidence","score_dimensions","target_rationale"];
    public IReadOnlyList<GuidedFieldDefinition> Fields {get;}=
    [
        T("name","Segment name",true,200),T("description","Description",true,2000),T("criteria","Qualification criteria",true),T("needs","Needs and jobs",true),T("behaviors","Behaviors",true),
        T("channels","Channel presence",true),T("pricing","Price sensitivity",true),N("size_low","Estimated size low"),N("size_high","Estimated size high"),T("size_method","Size method",true),
        N("confidence","Confidence",true,0,1),T("economics","Economics",true),T("evidence","Evidence",true),Scorecard(),T("risks","Risks"),T("target_rationale","Target rationale",true)
    ];
    public Task EnsureEligibleAsync(Guid c,Guid a,CancellationToken ct)=>MarketingStrategyGuidedArtifactDefinition.EnsureCapability(capabilities,c,a,AgentCapabilityIds.MarketingAudienceIntelligence,ct);
    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId,Guid agentId,Guid? targetId,CancellationToken ct)
    {
        var values=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase);
        string? targetVersion=null;if(targetId is Guid id){var segment=(await marketing.ListSegmentsAsync(companyId,ct)).SingleOrDefault(x=>x.Id==id)??throw new KeyNotFoundException("Marketing segment not found.");values["name"]=segment.Name;values["description"]=segment.Description;var v=segment.Versions.OrderByDescending(x=>x.VersionNumber).FirstOrDefault();if(v is not null){values["criteria"]=v.CriteriaJson;values["needs"]=v.NeedsJson;values["behaviors"]=v.BehaviorsJson;values["channels"]=v.ChannelsJson;values["pricing"]=v.PricingJson;values["size_low"]=v.SizeLow;values["size_high"]=v.SizeHigh;values["size_method"]=v.SizeMethod;values["confidence"]=v.Confidence;values["economics"]=v.EconomicsJson;values["evidence"]=v.EvidenceJson;values["score_dimensions"]=ParseObject(v.ScorecardJson);values["target_rationale"]=v.TargetRationale??"";targetVersion=$"{v.Id:N}:{v.ConcurrencyVersion}";}}
        return new(DisplayName,targetId,targetVersion,values,OpeningSummary:"Define one evidence-aware audience segment as a separate reusable Marketing artifact.",OpeningQuestion:"Who belongs in this segment, and what observable criteria distinguish them?");
    }
    public Task<IReadOnlyList<string>> ValidateAsync(Guid c,Guid a,Guid? t,IReadOnlyDictionary<string,JsonNode?> v,CancellationToken ct){var gaps=MarketingStrategyGuidedArtifactDefinition.RequiredGaps(Fields,v);var low=Dec(v,"size_low");var high=Dec(v,"size_high");if(low.HasValue&&high.HasValue&&high<low)gaps.Add("Estimated size high must be at least size low");try{_ = SegmentAttractivenessPolicy.Calculate(ReadScores(v));}catch(ArgumentException){gaps.Add("nine segment score dimensions with values from 0 to 100");}return Task.FromResult<IReadOnlyList<string>>(gaps);}
    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid c,Guid a,Guid? t,IReadOnlyDictionary<string,JsonNode?> v,CancellationToken ct){var score=SegmentAttractivenessPolicy.Calculate(ReadScores(v));return Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>([new("Backend attractiveness score",score.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture),"Calculated deterministically from the complete nine-dimension scorecard; the model cannot override it."),new("Evidence quality",string.IsNullOrWhiteSpace(MarketingStrategyGuidedArtifactDefinition.GetString(v,"evidence"))?"Missing":"Recorded","Size and economics without authoritative evidence remain explicit estimates."),new("Confirmation effect","Create a new segment version","Confirmation does not approve, activate, or select the segment as a target.")]);}
    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext c,CancellationToken ct)
    {
        if(c.TargetArtifactId is Guid existing&&!string.IsNullOrWhiteSpace(c.TargetArtifactVersion)){var current=(await marketing.ListSegmentsAsync(c.CompanyId,ct)).SingleOrDefault(x=>x.Id==existing)?.Versions.OrderByDescending(x=>x.VersionNumber).FirstOrDefault();if(current is null||$"{current.Id:N}:{current.ConcurrencyVersion}"!=c.TargetArtifactVersion)throw new GuidedWorkConflictException("The segment changed after this workshop started.");}
        var segmentId=c.TargetArtifactId??(await marketing.CreateSegmentAsync(c.CompanyId,c.UserId,new(Get(c,"name"),Get(c,"description")),ct)).Id;
        var scoreJson=ScoreJson(c.Values);Dictionary<string,decimal> scores=[];try{if(JsonNode.Parse(scoreJson) is JsonObject o)foreach(var p in o)if(decimal.TryParse(p.Value?.ToString(),out var d))scores[p.Key]=d;}catch{}
        var request=new CreateMarketingSegmentVersionRequest(AsJson(Get(c,"criteria")),AsJson(Get(c,"needs")),AsJson(Get(c,"behaviors")),AsJson(Get(c,"channels")),AsJson(Get(c,"pricing")),Long(c,"size_low"),Long(c,"size_high"),Get(c,"size_method"),Dec(c.Values,"confidence")??0,AsJson(Get(c,"economics")),AsJson(scoreJson),scores,AsJson(Get(c,"evidence")),DateTime.UtcNow,$"guided:{c.SessionId:N}");
        var version=await marketing.CreateSegmentVersionAsync(c.CompanyId,c.UserId,segmentId,request,ct)??throw new GuidedWorkConflictException("The segment could not be updated.");
        if(!string.IsNullOrWhiteSpace(Get(c,"target_rationale")))await marketing.RecommendTargetAsync(c.CompanyId,c.UserId,version.Id,new("primary",Get(c,"target_rationale"),"{}",version.Confidence,AsJson(Get(c,"risks")),DateTime.UtcNow.AddMonths(3),$"guided:{c.SessionId:N}:target"),ct);
        return new(version.Id,version.ConcurrencyVersion.ToString(),"Saved a new evidence-aware marketing segment version for review.");
    }
    private static GuidedFieldDefinition T(string p,string l,bool r=false,int max=8000)=>new(p,l,$"Describe {l.ToLowerInvariant()}.",GuidedFieldValueTypes.Text,r,MaxLength:max,AllowsEvidence:true);
    private static GuidedFieldDefinition Scorecard()=>new("score_dimensions","Score dimensions","Provide all nine numeric scores from 0 to 100: sizeGrowth, needIntensity, productFit, differentiation, reachability, priceValueFit, economics, evidenceQuality, and risk. Keep the supporting narrative in evidence or target rationale.",GuidedFieldValueTypes.Object,true,AllowsEvidence:true);
    private static GuidedFieldDefinition N(string p,string l,bool r=false,decimal? min=null,decimal? max=null)=>new(p,l,$"Set {l.ToLowerInvariant()}.",GuidedFieldValueTypes.Number,r,Minimum:min,Maximum:max);
    private static string Get(GuidedArtifactCommitContext c,string k)=>MarketingStrategyGuidedArtifactDefinition.GetString(c.Values,k); private static decimal? Dec(IReadOnlyDictionary<string,JsonNode?>v,string k)=>v.TryGetValue(k,out var n)&&decimal.TryParse(n?.ToString(),out var d)?d:null; private static long? Long(GuidedArtifactCommitContext c,string k)=>Dec(c.Values,k) is decimal d?(long)d:null;
    private static IReadOnlyDictionary<string,decimal> ReadScores(IReadOnlyDictionary<string,JsonNode?> values){try{if(JsonNode.Parse(ScoreJson(values)) is not JsonObject o)return new Dictionary<string,decimal>();return o.Where(x=>decimal.TryParse(x.Value?.ToString(),out _)).ToDictionary(x=>x.Key,x=>decimal.Parse(x.Value!.ToString()),StringComparer.OrdinalIgnoreCase);}catch(JsonException){return new Dictionary<string,decimal>();}}
    private static string ScoreJson(IReadOnlyDictionary<string,JsonNode?> values)=>values.TryGetValue("score_dimensions",out var node)&&node is not null
        ?node is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text:node.ToJsonString()
        :string.Empty;
    private static JsonObject ParseObject(string json){try{return JsonNode.Parse(json) as JsonObject??new JsonObject();}catch(JsonException){return new JsonObject();}}
    private static string AsJson(string s){if(string.IsNullOrWhiteSpace(s))return "{}";try{JsonNode.Parse(s);return s;}catch{return new JsonObject{{"summary",s}}.ToJsonString();}}
}
