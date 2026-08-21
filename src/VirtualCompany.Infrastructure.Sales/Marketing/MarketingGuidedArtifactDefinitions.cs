using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
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

public sealed class MarketingPlanGuidedArtifactDefinition(
    IMarketingOperationsService marketing,
    IMarketingStrategyService strategyService,
    IAgentCapabilityCatalog capabilities) : IGuidedArtifactDefinition
{
    public string ArtifactType => GuidedArtifactTypes.MarketingPlan;
    public string SchemaVersion => "1.0";
    public string DisplayName => "Marketing plan";
    public GuidedArtifactCapabilities Capabilities { get; } = new(true, [".pdf", ".docx", ".pptx", ".xlsx", ".txt", ".md", ".csv"], ["knowledge", "marketing"], true, true);
    public IReadOnlyList<string> QuestionPriorities =>
    [
        "summary", "strategy_id", "objective_ids", "primary_segment_version_id", "targeting_rationale",
        "expected_contribution", "starts_on", "ends_on", "planned_budget", "evidence_references", "missing_evidence"
    ];
    public IReadOnlyList<GuidedFieldDefinition> Fields { get; } =
    [
        Text("name", "Plan name", true, 200, "Name this bounded Marketing plan."),
        Text("summary", "Plan outcome", true, 4000, "Describe the measurable business outcome, intended customer change, and scope of this plan."),
        Identifier("strategy_id", "Approved strategy", true, "Select the exact approved or active Marketing strategy that governs this plan."),
        Number("strategy_version", "Strategy version", true, 1, "Use the current version of the selected strategy."),
        Date("starts_on", "Starts on", true),
        Date("ends_on", "Ends on", true),
        Number("planned_budget", "Planned budget", false, 0, "Set the maximum planned budget, or leave it unknown if no limit has been approved."),
        Text("budget_currency", "Budget currency", true, 3, "Use the three-letter currency code for the plan budget."),
        List("objective_ids", "Active objectives", true, "Select one or more active Marketing objectives that overlap the plan period."),
        Identifier("primary_segment_version_id", "Primary audience", true, "Select the exact approved or active audience version linked to the strategy."),
        List("secondary_segment_version_ids", "Secondary audiences", false, "Optionally select additional approved or active audience versions linked to the strategy."),
        Text("targeting_rationale", "Audience rationale", true, 4000, "Explain why the selected audiences are prioritized and the relevant trade-offs."),
        Text("expected_contribution", "Expected audience contribution", true, 4000, "Describe how the selected audiences are expected to contribute to the plan outcome."),
        Text("rationale", "Plan rationale", true, 4000, "Explain how the strategy, objectives, audiences, timing, and budget form a coherent plan."),
        List("evidence_references", "Evidence references", true, "List the sources that support this plan."),
        List("missing_evidence", "Missing evidence", true, "Keep unresolved evidence gaps explicit. This list must be empty before the plan draft can be saved.")
    ];

    public Task EnsureEligibleAsync(Guid companyId, Guid agentId, CancellationToken ct) =>
        MarketingStrategyGuidedArtifactDefinition.EnsureCapability(capabilities, companyId, agentId, AgentCapabilityIds.MarketingPlanning, ct);

    public async Task<string> BuildCompanyReferenceContextAsync(Guid companyId, Guid agentId, CancellationToken ct)
    {
        var strategies = (await strategyService.ListStrategiesAsync(companyId, ct))
            .OrderByDescending(x => x.UpdatedUtc)
            .ToArray();
        var segments = (await strategyService.ListSegmentsAsync(companyId, ct))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var plans = await marketing.ListPlanReferencesAsync(companyId, ct);

        var strategyItems = BuildBoundedArray(strategies, 10_000, strategy => new
        {
            artifact_type = "marketing_strategy",
            id = strategy.Id,
            title = strategy.Title,
            current_version = strategy.Version,
            status = strategy.Status,
            valid_from = strategy.ValidFromUtc.ToString("yyyy-MM-dd"),
            valid_to = strategy.ValidToUtc.ToString("yyyy-MM-dd"),
            summary = ReferenceText(strategy.Summary, 900),
            business_context = ReferenceText(strategy.BusinessContext, 900),
            sections_json = ReferenceText(strategy.SectionsJson, 2_500),
            evidence_references_json = ReferenceText(strategy.EvidenceReferencesJson, 800),
            missing_evidence_json = ReferenceText(strategy.MissingEvidenceJson, 800),
            linked_segment_version_ids = strategy.SegmentVersionIds
        });
        var segmentItems = BuildBoundedArray(segments, 10_000, segment => new
        {
            artifact_type = "marketing_segmentation",
            id = segment.Id,
            name = segment.Name,
            archived = segment.IsArchived,
            description = ReferenceText(segment.Description, 700),
            versions = segment.Versions.OrderByDescending(x => x.VersionNumber).Take(5).Select(version => new
            {
                id = version.Id,
                version = version.VersionNumber,
                status = version.Status,
                criteria_json = ReferenceText(version.CriteriaJson, 500),
                needs_json = ReferenceText(version.NeedsJson, 700),
                behaviors_json = ReferenceText(version.BehaviorsJson, 500),
                channels_json = ReferenceText(version.ChannelsJson, 400),
                pricing_json = ReferenceText(version.PricingJson, 400),
                size_low = version.SizeLow,
                size_high = version.SizeHigh,
                size_method = version.SizeMethod,
                confidence = version.Confidence,
                economics_json = ReferenceText(version.EconomicsJson, 500),
                evidence_json = ReferenceText(version.EvidenceJson, 600),
                target_rationale = ReferenceText(version.TargetRationale, 600)
            })
        });
        var planItems = BuildBoundedArray(plans, 7_000, plan => new
        {
            artifact_type = "marketing_plan",
            id = plan.Id,
            name = plan.Name,
            version = plan.Version,
            status = plan.Status,
            strategy_title = plan.StrategyTitle,
            strategy_version = plan.StrategyVersion,
            starts_on = plan.StartsUtc.ToString("yyyy-MM-dd"),
            ends_on = plan.EndsUtc.ToString("yyyy-MM-dd"),
            summary = ReferenceText(plan.Summary, 900),
            rationale = ReferenceText(plan.Rationale, 900),
            planned_budget = plan.PlannedBudget,
            budget_currency = plan.BudgetCurrency,
            objectives = plan.Objectives.Take(8).Select(x => new { x.Id, x.Name, x.Status }),
            audiences = plan.Segments.Take(8).Select(x => new { x.SegmentVersionId, x.SegmentVersionNumber, x.SegmentName, x.Role, x.Status }),
            evidence_references = plan.EvidenceReferences.Take(20),
            missing_evidence = plan.MissingEvidence.Take(20)
        });

        return new JsonObject
        {
            ["purpose"] = "Existing company Marketing material that may inform this plan workshop.",
            ["governance"] = "Only a currently valid approved or active strategy and its linked approved or active audience versions may govern a new plan. Other statuses and previous plans are reference material only.",
            ["strategy_count"] = strategies.Length,
            ["included_strategy_count"] = strategyItems.Count,
            ["strategies"] = strategyItems,
            ["segmentation_artifact_count"] = segments.Length,
            ["included_segmentation_artifact_count"] = segmentItems.Count,
            ["segmentation"] = segmentItems,
            ["previous_plan_count"] = plans.Count,
            ["included_previous_plan_count"] = planItems.Count,
            ["previous_plans"] = planItems
        }.ToJsonString();
    }

    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId, Guid agentId, Guid? targetId, CancellationToken ct)
    {
        if (targetId.HasValue)
            throw new GuidedWorkValidationException(new Dictionary<string, string[]> { [nameof(targetId)] = ["Marketing plan workshops create new reviewable drafts. Start a new workshop without an existing plan."] });

        var today = DateTime.UtcNow.Date;
        var strategies = (await strategyService.ListStrategiesAsync(companyId, ct))
            .Where(x => (x.Status is MarketingStrategicStatuses.Approved or MarketingStrategicStatuses.Active) && x.ValidToUtc.Date > today)
            .OrderByDescending(x => x.UpdatedUtc)
            .ToArray();
        var selectedStrategy = strategies.FirstOrDefault();
        var starts = selectedStrategy is null ? today : MaxDate(today, selectedStrategy.ValidFromUtc.Date);
        var ends = selectedStrategy is null ? today.AddMonths(3) : MinDate(starts.AddMonths(3), selectedStrategy.ValidToUtc.Date);
        if (ends <= starts) ends = starts.AddDays(1);

        var objectives = (await marketing.ListObjectivesAsync(companyId, ct))
            .Where(x => x.Status == MarketingStatuses.Active && x.PeriodStartUtc < ends && x.PeriodEndUtc > starts)
            .OrderBy(x => x.PeriodEndUtc)
            .ToArray();
        var segments = await strategyService.ListSegmentsAsync(companyId, ct);
        var linkedIds = selectedStrategy?.SegmentVersionIds.ToHashSet() ?? [];
        var eligibleSegments = segments.SelectMany(segment => segment.Versions
                .Where(version => linkedIds.Contains(version.Id) && (version.Status is MarketingStrategicStatuses.Approved or MarketingStrategicStatuses.Active))
                .Select(version => new { segment.Name, Version = version }))
            .OrderByDescending(x => x.Version.AttractivenessScore)
            .ToArray();

        var values = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["starts_on"] = starts.ToString("yyyy-MM-dd"),
            ["ends_on"] = ends.ToString("yyyy-MM-dd"),
            ["budget_currency"] = "SEK",
            ["objective_ids"] = new JsonArray(objectives.Take(1).Select(x => JsonValue.Create(x.Id.ToString())).ToArray()),
            ["secondary_segment_version_ids"] = new JsonArray(),
            ["missing_evidence"] = selectedStrategy is null ? new JsonArray() : ParseStringArray(selectedStrategy.MissingEvidenceJson)
        };
        if (selectedStrategy is not null)
        {
            values["name"] = Bound($"{selectedStrategy.Title} plan", 200);
            values["summary"] = selectedStrategy.Summary;
            values["strategy_id"] = selectedStrategy.Id.ToString();
            values["strategy_version"] = selectedStrategy.Version;
            values["rationale"] = $"Execute {selectedStrategy.Title} through a bounded, measurable Marketing plan.";
            values["evidence_references"] = ParseStringArray(selectedStrategy.EvidenceReferencesJson);
        }
        if (eligibleSegments.FirstOrDefault() is { } primary)
        {
            values["primary_segment_version_id"] = primary.Version.Id.ToString();
            values["targeting_rationale"] = $"Use {primary.Name} version {primary.Version.VersionNumber} as the primary audience because it is linked to the selected strategy.";
            values["expected_contribution"] = $"Contribute to the selected Marketing objective through the needs and behaviors documented for {primary.Name}.";
        }

        var statuses = values.Keys.ToDictionary(x => x, _ => "proposed", StringComparer.OrdinalIgnoreCase);
        var context = BuildOpeningContext(strategies, objectives, eligibleSegments.Select(x => (x.Name, x.Version)).ToArray());
        return new(DisplayName, null, null, values, statuses,
            OpeningSummary: $"Build a strategy-grounded Marketing plan as a reviewable draft. {context}",
            OpeningQuestion: "What measurable outcome should this plan deliver, and which active objective should show that it succeeded?");
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(Guid companyId, Guid agentId, Guid? targetId, IReadOnlyDictionary<string, JsonNode?> values, CancellationToken ct)
    {
        var gaps = MarketingStrategyGuidedArtifactDefinition.RequiredGaps(Fields, values);
        if (targetId.HasValue) gaps.Add("Marketing plan workshops cannot replace an existing plan");
        if (!TryGuid(values, "strategy_id", out var strategyId)) gaps.Add("Approved strategy must be selected");
        if (!TryInt(values, "strategy_version", out var strategyVersion) || strategyVersion < 1) gaps.Add("Strategy version must be valid");
        if (!MarketingStrategyGuidedArtifactDefinition.TryDate(values, "starts_on", out var starts)) gaps.Add("Starts on must be valid");
        if (!MarketingStrategyGuidedArtifactDefinition.TryDate(values, "ends_on", out var ends)) gaps.Add("Ends on must be valid");
        if (starts != default && ends != default && ends <= starts) gaps.Add("Ends on must be after starts on");
        if (!TryGuid(values, "primary_segment_version_id", out var primarySegmentId)) gaps.Add("Primary audience must be selected");
        var objectiveEntries = ReadStringList(values, "objective_ids");
        var objectiveIds = ReadGuidList(values, "objective_ids");
        if (objectiveIds.Count == 0) gaps.Add("At least one active objective must be selected");
        if (objectiveIds.Count != objectiveEntries.Count || objectiveIds.Count != objectiveIds.Distinct().Count()) gaps.Add("Every objective must be a unique valid identifier");
        var secondaryEntries = ReadStringList(values, "secondary_segment_version_ids");
        var secondaryIds = ReadGuidList(values, "secondary_segment_version_ids");
        if (secondaryIds.Count != secondaryEntries.Count || secondaryIds.Count != secondaryIds.Distinct().Count() || secondaryIds.Contains(primarySegmentId)) gaps.Add("Each audience version must be a unique valid identifier");
        var evidence = ReadStringList(values, "evidence_references");
        if (evidence.Count == 0) gaps.Add("At least one evidence reference is required");
        var missingEvidence = ReadStringList(values, "missing_evidence");
        if (missingEvidence.Count > 0) gaps.Add("Resolve or remove every missing evidence item before saving the plan draft");
        if (gaps.Count > 0) return gaps;

        var request = BuildRequest(values, strategyId, strategyVersion, starts, ends, objectiveIds, primarySegmentId, secondaryIds, evidence, missingEvidence, agentId, "guided-validation");
        var decision = await marketing.AssessPlanReadinessAsync(companyId, request, ct);
        if (!decision.Allowed) gaps.Add(decision.Explanation);
        return gaps;
    }

    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid companyId, Guid agentId, Guid? targetId, IReadOnlyDictionary<string, JsonNode?> values, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>
        ([
            new("Strategy grounding", $"Version {ReadInt(values, "strategy_version")}", "The exact approved strategy version is rechecked before the draft is created."),
            new("Plan coverage", $"{ReadGuidList(values, "objective_ids").Count} objective(s) and {1 + ReadGuidList(values, "secondary_segment_version_ids").Count} audience(s)", "Objectives, audiences, dates, evidence, and budget are validated by the Marketing plan readiness policy."),
            new("Confirmation effect", "Create plan draft only", "Confirmation does not submit, approve, activate, create campaigns, publish content, or spend budget.")
        ]);

    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context, CancellationToken ct)
    {
        var strategyId = Guid.Parse(Get(context.Values, "strategy_id"));
        var strategyVersion = ReadInt(context.Values, "strategy_version");
        var starts = MarketingStrategyGuidedArtifactDefinition.GetDate(context.Values, "starts_on");
        var ends = MarketingStrategyGuidedArtifactDefinition.GetDate(context.Values, "ends_on");
        var primarySegmentId = Guid.Parse(Get(context.Values, "primary_segment_version_id"));
        var request = BuildRequest(context.Values, strategyId, strategyVersion, starts, ends,
            ReadGuidList(context.Values, "objective_ids"), primarySegmentId, ReadGuidList(context.Values, "secondary_segment_version_ids"),
            ReadStringList(context.Values, "evidence_references"), ReadStringList(context.Values, "missing_evidence"), context.AgentId, $"guided:{context.SessionId:N}");
        var result = await marketing.CreateGroundedPlanAsync(context.CompanyId, context.UserId, request, ct);
        return new(result.Summary.Id, result.Summary.Version.ToString(), $"Saved marketing plan {result.Summary.Name} as a reviewable draft.");
    }

    private static CreateGroundedMarketingPlanRequest BuildRequest(IReadOnlyDictionary<string, JsonNode?> values, Guid strategyId, int strategyVersion,
        DateTime starts, DateTime ends, IReadOnlyList<Guid> objectiveIds, Guid primarySegmentId, IReadOnlyList<Guid> secondarySegmentIds,
        IReadOnlyList<string> evidence, IReadOnlyList<string> missingEvidence, Guid agentId, string idempotencyKey)
    {
        var rationale = Get(values, "targeting_rationale");
        var contribution = Get(values, "expected_contribution");
        var segments = new List<MarketingPlanSegmentSelection> { new(primarySegmentId, "primary", 1, rationale, contribution) };
        segments.AddRange(secondarySegmentIds.Select((id, index) => new MarketingPlanSegmentSelection(id, "secondary", index + 2, rationale, contribution)));
        return new(Get(values, "name"), Get(values, "summary"), strategyId, strategyVersion,
            DateTime.SpecifyKind(starts.Date, DateTimeKind.Utc), DateTime.SpecifyKind(ends.Date, DateTimeKind.Utc),
            ReadDecimal(values, "planned_budget"), Get(values, "budget_currency").ToUpperInvariant(), objectiveIds, segments,
            Get(values, "rationale"), evidence, [], [], missingEvidence, idempotencyKey, agentId);
    }

    private static string BuildOpeningContext(IReadOnlyList<MarketingStrategyDto> strategies, IReadOnlyList<MarketingObjectiveDto> objectives,
        IReadOnlyList<(string Name, MarketingSegmentVersionDto Version)> segments)
    {
        var strategyText = strategies.Count == 0 ? "No currently valid approved strategy is available."
            : "Eligible strategies: " + string.Join("; ", strategies.Take(5).Select(x => $"{x.Title} v{x.Version} ({x.Id:D})")) + ".";
        var objectiveText = objectives.Count == 0 ? " No overlapping active objective is available."
            : " Active objectives: " + string.Join("; ", objectives.Take(8).Select(x => $"{x.Name} ({x.Id:D})")) + ".";
        var segmentText = segments.Count == 0 ? " No eligible audience is linked to the selected strategy."
            : " Eligible audiences: " + string.Join("; ", segments.Take(8).Select(x => $"{x.Name} v{x.Version.VersionNumber} ({x.Version.Id:D})")) + ".";
        return Bound(strategyText + objectiveText + segmentText, 1800);
    }

    private static GuidedFieldDefinition Text(string path, string label, bool required, int max, string description) =>
        new(path, label, description, GuidedFieldValueTypes.Text, required, MaxLength: max, AllowsEvidence: true);
    private static GuidedFieldDefinition Date(string path, string label, bool required) =>
        new(path, label, $"Set {label.ToLowerInvariant()} within the governing strategy period.", GuidedFieldValueTypes.Date, required);
    private static GuidedFieldDefinition Number(string path, string label, bool required, decimal minimum, string description) =>
        new(path, label, description, GuidedFieldValueTypes.Number, required, Minimum: minimum);
    private static GuidedFieldDefinition Identifier(string path, string label, bool required, string description) =>
        new(path, label, description, GuidedFieldValueTypes.Identifier, required);
    private static GuidedFieldDefinition List(string path, string label, bool required, string description) =>
        new(path, label, description, GuidedFieldValueTypes.TextList, required, AllowsEvidence: true);
    private static string Get(IReadOnlyDictionary<string, JsonNode?> values, string key) => MarketingStrategyGuidedArtifactDefinition.GetString(values, key);
    private static bool TryGuid(IReadOnlyDictionary<string, JsonNode?> values, string key, out Guid id) => Guid.TryParse(Get(values, key), out id);
    private static bool TryInt(IReadOnlyDictionary<string, JsonNode?> values, string key, out int number) => int.TryParse(values.GetValueOrDefault(key)?.ToString(), out number);
    private static int ReadInt(IReadOnlyDictionary<string, JsonNode?> values, string key) => TryInt(values, key, out var number) ? number : 0;
    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonNode?> values, string key) => decimal.TryParse(values.GetValueOrDefault(key)?.ToString(), out var value) ? value : null;
    private static IReadOnlyList<Guid> ReadGuidList(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        ReadStringList(values, key).Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToArray();
    private static IReadOnlyList<string> ReadStringList(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) && node is JsonArray array
            ? array.Select(x => x?.GetValue<string>()?.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray()
            : [];
    private static JsonArray ParseStringArray(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray array
                ? new JsonArray(array.Take(100).Select(x => x?.DeepClone()).ToArray())
                : new JsonArray();
        }
        catch (JsonException) { return new JsonArray(); }
    }
    private static DateTime MinDate(DateTime left, DateTime right) => left <= right ? left : right;
    private static DateTime MaxDate(DateTime left, DateTime right) => left >= right ? left : right;
    private static JsonArray BuildBoundedArray<T>(IEnumerable<T> source, int characterBudget, Func<T, object> project)
    {
        var result = new JsonArray();
        var used = 2;
        foreach (var item in source)
        {
            var node = JsonSerializer.SerializeToNode(project(item))!;
            var length = node.ToJsonString().Length + (result.Count == 0 ? 0 : 1);
            if (used + length > characterBudget) break;
            result.Add(node);
            used += length;
        }
        return result;
    }
    private static string ReferenceText(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : normalized[..max] + "…";
    }
    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max];
}
