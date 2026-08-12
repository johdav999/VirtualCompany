namespace VirtualCompany.Domain.Entities;

public sealed class MarketingAttributionTouch : ICompanyOwnedEntity
{
    private MarketingAttributionTouch() { }
    public MarketingAttributionTouch(Guid id, Guid companyId, string subjectType, Guid subjectId, string touchType,
        string channel, string sourceReference, int sourceVersion, DateTime occurredUtc, decimal? cost,
        string? currency, string evidenceJson, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId); if (subjectId == Guid.Empty || sourceVersion < 1 || cost < 0) throw new ArgumentException("Attribution touch is invalid.");
        System.Text.Json.JsonDocument.Parse(evidenceJson); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        SubjectType = T(subjectType, 80); SubjectId = subjectId; TouchType = T(touchType, 60); Channel = T(channel, 60);
        SourceReference = T(sourceReference, 500); SourceVersion = sourceVersion; OccurredUtc = occurredUtc.ToUniversalTime();
        Cost = cost; Currency = currency; EvidenceJson = evidenceJson; IdempotencyKey = T(idempotencyKey, 220); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string SubjectType { get; private set; } = null!;
    public Guid SubjectId { get; private set; } public string TouchType { get; private set; } = null!; public string Channel { get; private set; } = null!;
    public string SourceReference { get; private set; } = null!; public int SourceVersion { get; private set; } public DateTime OccurredUtc { get; private set; }
    public decimal? Cost { get; private set; } public string? Currency { get; private set; } public string EvidenceJson { get; private set; } = "{}";
    public string IdempotencyKey { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
    private static string T(string value, int max) => SalesEntityText.NormalizeRequired(value, nameof(value), max).ToLowerInvariant();
}

public sealed class MarketingAttributionModelDefinition : ICompanyOwnedEntity
{
    private static readonly HashSet<string> Models = new(StringComparer.OrdinalIgnoreCase) { "first_touch", "last_touch", "even", "configured_weighted" };
    private MarketingAttributionModelDefinition() { }
    public MarketingAttributionModelDefinition(Guid id, Guid companyId, string name, string modelType, int version,
        string rulesJson, string limitations, int lookbackDays, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId); if (!Models.Contains(modelType) || version < 1 || lookbackDays is < 1 or > 730) throw new ArgumentException("Attribution model is invalid.");
        System.Text.Json.JsonDocument.Parse(rulesJson); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160); ModelType = modelType.ToLowerInvariant(); Version = version;
        RulesJson = rulesJson; Limitations = SalesEntityText.NormalizeRequired(limitations, nameof(limitations), 2000); LookbackDays = lookbackDays;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 220); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public string Name { get; private set; } = null!;
    public string ModelType { get; private set; } = null!; public int Version { get; private set; } public string RulesJson { get; private set; } = "{}";
    public string Limitations { get; private set; } = null!; public int LookbackDays { get; private set; } public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingAttributionAllocation : ICompanyOwnedEntity
{
    private MarketingAttributionAllocation() { }
    public MarketingAttributionAllocation(Guid id, Guid companyId, Guid resultId, Guid touchId, decimal weight,
        decimal value, string evidenceVersion)
    { SalesEntityText.EnsureCompany(companyId); if (resultId == Guid.Empty || touchId == Guid.Empty || weight is < 0 or > 1) throw new ArgumentException("Attribution allocation is invalid."); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingAttributionResultId = resultId; MarketingAttributionTouchId = touchId; Weight = weight; AttributedValue = value; EvidenceVersion = SalesEntityText.NormalizeRequired(evidenceVersion, nameof(evidenceVersion), 200); CreatedUtc = DateTime.UtcNow; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingAttributionResultId { get; private set; }
    public Guid MarketingAttributionTouchId { get; private set; } public decimal Weight { get; private set; } public decimal AttributedValue { get; private set; }
    public string EvidenceVersion { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingExperimentExposure : ICompanyOwnedEntity
{
    private MarketingExperimentExposure() { }
    public MarketingExperimentExposure(Guid id, Guid companyId, Guid experimentId, string subjectReference,
        string variant, string assignmentKey, DateTime exposedUtc, string evidenceJson)
    { SalesEntityText.EnsureCompany(companyId); if (experimentId == Guid.Empty) throw new ArgumentException("Experiment is required."); System.Text.Json.JsonDocument.Parse(evidenceJson); Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingExperimentId = experimentId; SubjectReference = T(subjectReference, 200); Variant = T(variant, 80); AssignmentKey = T(assignmentKey, 220); ExposedUtc = exposedUtc.ToUniversalTime(); EvidenceJson = evidenceJson; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingExperimentId { get; private set; }
    public string SubjectReference { get; private set; } = null!; public string Variant { get; private set; } = null!; public string AssignmentKey { get; private set; } = null!;
    public DateTime ExposedUtc { get; private set; } public string EvidenceJson { get; private set; } = "{}"; private static string T(string v,int m)=>SalesEntityText.NormalizeRequired(v,nameof(v),m);
}

public sealed class MarketingExperimentDecisionRecord : ICompanyOwnedEntity
{
    private MarketingExperimentDecisionRecord() { }
    public MarketingExperimentDecisionRecord(Guid id, Guid companyId, Guid experimentId, string decision,
        int sampleSize, decimal contaminationRate, bool guardrailBreached, bool causalEligible,
        string evidenceJson, string limitations)
    { SalesEntityText.EnsureCompany(companyId); if (experimentId == Guid.Empty || sampleSize < 0 || contaminationRate is < 0 or > 1) throw new ArgumentException("Experiment decision is invalid."); System.Text.Json.JsonDocument.Parse(evidenceJson); Id=id==Guid.Empty?Guid.NewGuid():id;CompanyId=companyId;MarketingExperimentId=experimentId;Decision=SalesEntityText.NormalizeRequired(decision,nameof(decision),60);SampleSize=sampleSize;ContaminationRate=contaminationRate;GuardrailBreached=guardrailBreached;CausalEligible=causalEligible;EvidenceJson=evidenceJson;Limitations=SalesEntityText.NormalizeRequired(limitations,nameof(limitations),2000);CreatedUtc=DateTime.UtcNow; }
    public Guid Id{get;private set;}public Guid CompanyId{get;private set;}public Guid MarketingExperimentId{get;private set;}public string Decision{get;private set;}=null!;public int SampleSize{get;private set;}public decimal ContaminationRate{get;private set;}public bool GuardrailBreached{get;private set;}public bool CausalEligible{get;private set;}public string EvidenceJson{get;private set;}="{}";public string Limitations{get;private set;}=null!;public DateTime CreatedUtc{get;private set;}
}

public sealed class MarketingSegmentLearningProposal : ICompanyOwnedEntity
{
    private MarketingSegmentLearningProposal() { }
    public MarketingSegmentLearningProposal(Guid id,Guid companyId,Guid segmentVersionId,string metricsJson,string proposedChangesJson,string evidenceJson,decimal confidence,string idempotencyKey)
    { SalesEntityText.EnsureCompany(companyId);if(segmentVersionId==Guid.Empty||confidence is<0 or>1)throw new ArgumentException("Segment learning proposal is invalid.");System.Text.Json.JsonDocument.Parse(metricsJson);System.Text.Json.JsonDocument.Parse(proposedChangesJson);System.Text.Json.JsonDocument.Parse(evidenceJson);Id=id==Guid.Empty?Guid.NewGuid():id;CompanyId=companyId;MarketingCustomerSegmentVersionId=segmentVersionId;MetricsJson=metricsJson;ProposedChangesJson=proposedChangesJson;EvidenceJson=evidenceJson;Confidence=confidence;IdempotencyKey=SalesEntityText.NormalizeRequired(idempotencyKey,nameof(idempotencyKey),220);Status="review_proposed";CreatedUtc=DateTime.UtcNow;}
    public Guid Id{get;private set;}public Guid CompanyId{get;private set;}public Guid MarketingCustomerSegmentVersionId{get;private set;}public string MetricsJson{get;private set;}="{}";public string ProposedChangesJson{get;private set;}="{}";public string EvidenceJson{get;private set;}="{}";public decimal Confidence{get;private set;}public string IdempotencyKey{get;private set;}=null!;public string Status{get;private set;}=null!;public DateTime CreatedUtc{get;private set;}
}

public static class MarketingExperimentDecisionPolicy
{
    public static (string Decision,bool CausalEligible,string Limitation) Evaluate(int minimumSample,int sampleSize,decimal contaminationRate,bool dataQualityValid,bool guardrailBreached)
    { if(sampleSize<minimumSample)return("insufficient_evidence",false,"The minimum sample has not been reached.");if(!dataQualityValid)return("insufficient_evidence",false,"Data quality checks did not pass.");if(contaminationRate>.05m)return("insufficient_evidence",false,"Measured assignment contamination exceeds five percent.");if(guardrailBreached)return("stop_guardrail_breach",false,"A configured guardrail was breached.");return("ready_for_decision",true,"Causal language remains limited to this randomized experiment and its measured population."); }
}
