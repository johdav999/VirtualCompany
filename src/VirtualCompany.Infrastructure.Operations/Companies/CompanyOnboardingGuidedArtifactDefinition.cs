using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.GuidedWork;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOnboardingGuidedArtifactDefinition(
    VirtualCompanyDbContext db,
    ICompanyOnboardingService onboarding,
    ICompanyOutboxEnqueuer outbox) : IGuidedArtifactDefinition
{
    public string ArtifactType => GuidedArtifactTypes.CompanyOnboarding;
    public string SchemaVersion => "1.0";
    public string DisplayName => "Company onboarding";
    public GuidedArtifactCapabilities Capabilities { get; } = new(true,
        [".pdf",".docx",".pptx",".xlsx",".csv",".txt",".md"],
        ["knowledge","operations","sales","marketing","support"], true, true);
    public IReadOnlyList<string> QuestionPriorities =>
        ["company_summary","customer_problem","target_customers","value_proposition","products_and_services","initial_priorities","mission","differentiators","operating_principles","success_measures"];
    public IReadOnlyList<GuidedFieldDefinition> Fields { get; } =
    [
        Required("company_name","Company name",200), Required("industry","Industry",100), Required("business_type","Business type",100),
        Required("timezone","Timezone",100), Required("currency","Currency",16), Required("language","Language",16), Required("compliance_region","Compliance region",50),
        Optional("selected_template_id","Starting template",100),
        Optional("branding","Branding",4000,GuidedFieldValueTypes.Object),
        Required("company_summary","Company summary",8000), Optional("mission","Mission"), Optional("vision","Vision"),
        Required("customer_problem","Customer problem"), Required("target_customers","Target customers"), Required("value_proposition","Value proposition"),
        Optional("markets_and_geography","Markets and geography"), Optional("differentiators","Differentiators"),
        Required("products_and_services","Products and services",12000), Optional("pricing_principles","Pricing principles"),
        Optional("delivery_model","Delivery model"), Optional("product_limitations","Product limitations"), Optional("customer_promises","Customer promises"),
        Optional("operating_principles","Operating principles"), Optional("key_processes","Key processes"), Optional("policies_and_constraints","Policies and constraints"),
        Required("initial_priorities","Initial priorities"), Optional("success_measures","Success measures"),
        Optional("evidence","Evidence"), Optional("assumptions","Assumptions"), Optional("missing_evidence","Missing evidence"), Optional("risks","Risks")
    ];

    public async Task EnsureEligibleAsync(Guid companyId,Guid agentId,CancellationToken ct)
    {
        var eligible=await db.Agents.AsNoTracking().AnyAsync(x=>x.CompanyId==companyId&&x.Id==agentId&&x.TemplateId==CompanyOnboardingWorkshopService.FacilitatorTemplateId,ct);
        if(!eligible)throw new GuidedArtifactNotEligibleException("Company onboarding must be facilitated by the company setup advisor.");
        _=await db.Companies.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==companyId,ct)??throw new KeyNotFoundException("Company not found.");
    }

    public async Task<GuidedArtifactInitialization> InitializeAsync(Guid companyId,Guid agentId,Guid? targetId,CancellationToken ct)
    {
        if(targetId.HasValue&&targetId!=companyId)throw new GuidedWorkConflictException("The onboarding workshop cannot target another company.");
        var company=await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==companyId,ct);
        var values=new Dictionary<string,JsonNode?>(StringComparer.OrdinalIgnoreCase);
        AddInitialValue(values,"company_name",company.Name);
        AddInitialValue(values,"industry",company.Industry);
        AddInitialValue(values,"business_type",company.BusinessType);
        AddInitialValue(values,"timezone",company.Timezone);
        AddInitialValue(values,"currency",company.Currency);
        AddInitialValue(values,"language",company.Language);
        AddInitialValue(values,"compliance_region",company.ComplianceRegion);
        AddInitialValue(values,"selected_template_id",company.OnboardingTemplateId);
        return new(DisplayName,companyId,company.UpdatedUtc.Ticks.ToString(),values,
            OpeningSummary:"Build a reviewed company foundation with Eva. Uploaded material and public research remain evidence, not confirmed company decisions.",
            OpeningQuestion:"In a few sentences, what does the company do, who is it for, and what problem does it solve?");
    }

    public Task<IReadOnlyList<string>> ValidateAsync(Guid companyId,Guid agentId,Guid? targetId,IReadOnlyDictionary<string,JsonNode?> values,CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Fields.Where(x=>x.IsRequired&&string.IsNullOrWhiteSpace(Text(values,x.Path))).Select(x=>x.Label).ToArray());

    public Task<IReadOnlyList<GuidedReviewInsightDto>> BuildReviewInsightsAsync(Guid companyId,Guid agentId,Guid? targetId,IReadOnlyDictionary<string,JsonNode?> values,CancellationToken ct)=>
        Task.FromResult<IReadOnlyList<GuidedReviewInsightDto>>([
            new("Company foundation","Profile, story, products, and operating context","Confirmation saves the reviewed company foundation and keeps the standard department agents available."),
            new("Initial documents","Company overview and product catalog","Reviewed workshop content is retained for deterministic document generation."),
            new("Confirmation effect","Complete this company setup only","No integrations, messages, publishing, spending, or autonomy changes are performed.")]);

    public async Task<GuidedArtifactCommitResult> CommitAsync(GuidedArtifactCommitContext context,CancellationToken ct)
    {
        var company=await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==context.CompanyId,ct);
        if(!string.IsNullOrWhiteSpace(context.TargetArtifactVersion)&&company.UpdatedUtc.Ticks.ToString()!=context.TargetArtifactVersion)
            throw new GuidedWorkConflictException("Company setup changed after this workshop started. Review the latest values before confirming.");
        var result=await onboarding.CompleteOnboardingAsync(new CompleteCompanyOnboardingRequest(context.CompanyId,
            Text(context.Values,"company_name"),Text(context.Values,"industry"),Text(context.Values,"business_type"),null,null,
            Text(context.Values,"timezone"),Text(context.Values,"currency"),Text(context.Values,"language"),Text(context.Values,"compliance_region"),OptionalText(context.Values,"selected_template_id")),ct);
        QueueDocument(context,"company-overview","Company overview","company-overview.md",RenderOverview(context.Values));
        QueueDocument(context,"product-catalog","Product catalog","product-catalog.md",RenderProducts(context.Values));
        var operating=RenderOperating(context.Values);
        if(!string.IsNullOrWhiteSpace(operating))QueueDocument(context,"company-operating-context","Company operating context","company-operating-context.md",operating);
        return new(result.CompanyId,DateTime.UtcNow.Ticks.ToString(),$"Saved the reviewed company foundation for {result.CompanyName}.");
    }

    private void QueueDocument(GuidedArtifactCommitContext context,string key,string title,string fileName,string markdown)
    {
        var contentHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown))).ToLowerInvariant();
        var idempotencyKey=$"onboarding-document:{context.CompanyId:N}:{context.SessionId:N}:{key}:{SchemaVersion}:{contentHash[..16]}";
        outbox.Enqueue(context.CompanyId,CompanyOutboxTopics.OnboardingDocumentGenerationRequested,
            new OnboardingDocumentGenerationRequestedMessage(context.CompanyId,context.SessionId,context.UserId,key,title,fileName,markdown,contentHash,SchemaVersion,context.CorrelationId),
            context.CorrelationId,idempotencyKey:idempotencyKey);
    }

    private static string RenderOverview(IReadOnlyDictionary<string,JsonNode?> v)=>string.Concat(
        "# ",Text(v,"company_name")," — Company overview\n\n",
        Section("Summary",Text(v,"company_summary")),Section("Mission",Text(v,"mission")),Section("Vision",Text(v,"vision")),
        Section("Customers and problem",$"{Text(v,"target_customers")}\n\n{Text(v,"customer_problem")}"),
        Section("Value proposition",Text(v,"value_proposition")),
        Section("Markets and differentiation",$"{Text(v,"markets_and_geography")}\n\n{Text(v,"differentiators")}"),
        Section("Initial priorities",Text(v,"initial_priorities")));
    private static string RenderProducts(IReadOnlyDictionary<string,JsonNode?> v)=>string.Concat(
        "# Product and service catalog\n\n",Section("Products and services",Text(v,"products_and_services")),
        Section("Intended customers and value",$"{Text(v,"target_customers")}\n\n{Text(v,"value_proposition")}"),
        Section("Pricing principles",Text(v,"pricing_principles")),Section("Delivery model",Text(v,"delivery_model")),
        Section("Customer promises",Text(v,"customer_promises")),Section("Known limitations",Text(v,"product_limitations")));
    private static string RenderOperating(IReadOnlyDictionary<string,JsonNode?> v)
    {
        var parts=new[]{Text(v,"operating_principles"),Text(v,"key_processes"),Text(v,"policies_and_constraints"),Text(v,"success_measures")};
        if(parts.All(string.IsNullOrWhiteSpace))return string.Empty;
        return string.Concat("# Company operating context\n\n",Section("Operating principles",parts[0]),Section("Key processes",parts[1]),
            Section("Policies and constraints",parts[2]),Section("Initial priorities",Text(v,"initial_priorities")),Section("Success measures",parts[3]));
    }
    private static string Section(string title,string value)=>$"## {title}\n{value}\n\n";

    private static GuidedFieldDefinition Required(string path,string label,int max=8000)=>new(path,label,$"Define {label.ToLowerInvariant()} in plain business language.",GuidedFieldValueTypes.Text,true,MaxLength:max,AllowsEvidence:true);
    private static GuidedFieldDefinition Optional(string path,string label,int max=8000,string type=GuidedFieldValueTypes.Text)=>new(path,label,$"Describe {label.ToLowerInvariant()} when it is useful.",type,false,MaxLength:max,AllowsEvidence:true);
    private static void AddInitialValue(IDictionary<string,JsonNode?> values,string path,string? value)
    {
        if(!string.IsNullOrWhiteSpace(value))values[path]=value;
    }
    private static string Text(IReadOnlyDictionary<string,JsonNode?> values,string path)=>values.TryGetValue(path,out var value)&&value is JsonValue scalar&&scalar.TryGetValue<string>(out var text)?text.Trim():string.Empty;
    private static string? OptionalText(IReadOnlyDictionary<string,JsonNode?> values,string path){var value=Text(values,path);return string.IsNullOrWhiteSpace(value)?null:value;}
}
