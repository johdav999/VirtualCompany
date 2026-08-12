namespace VirtualCompany.Application.Marketing;

public sealed record RecordMarketingAttributionTouchRequest(string SubjectType, Guid SubjectId, string TouchType,
    string Channel, string SourceReference, int SourceVersion, DateTime OccurredUtc, decimal? Cost,
    string? Currency, string EvidenceJson, string IdempotencyKey);
public sealed record MarketingAttributionTouchDto(Guid Id,string SubjectType,Guid SubjectId,string TouchType,string Channel,string SourceReference,int SourceVersion,DateTime OccurredUtc,decimal? Cost,string? Currency,string EvidenceJson);
public sealed record CreateMarketingAttributionModelRequest(string Name,string ModelType,string RulesJson,string Limitations,int LookbackDays,string IdempotencyKey);
public sealed record MarketingAttributionModelDto(Guid Id,string Name,string ModelType,int Version,string RulesJson,string Limitations,int LookbackDays);
public sealed record RunMarketingAttributionRequest(Guid ModelId,string SubjectType,Guid SubjectId,decimal OutcomeValue,string Unit,DateTime PeriodStartUtc,DateTime PeriodEndUtc,string IdempotencyKey);
public sealed record MarketingAttributionRunDto(MarketingAttributionDto Result,IReadOnlyList<MarketingAttributionAllocationDto> Allocations,string Limitations);
public sealed record MarketingAttributionAllocationDto(Guid TouchId,decimal Weight,decimal AttributedValue,string EvidenceVersion);
public sealed record RecordMarketingExperimentExposureRequest(Guid ExperimentId,string SubjectReference,string Variant,string AssignmentKey,DateTime ExposedUtc,string EvidenceJson);
public sealed record EvaluateMarketingExperimentRequest(Guid ExperimentId,int MinimumSampleSize,int SampleSize,decimal ContaminationRate,bool DataQualityValid,bool GuardrailBreached,string EvidenceJson);
public sealed record MarketingExperimentDecisionDto(Guid Id,Guid ExperimentId,string Decision,int SampleSize,decimal ContaminationRate,bool GuardrailBreached,bool CausalEligible,string EvidenceJson,string Limitations);
public sealed record CreateMarketingSegmentLearningProposalRequest(Guid SegmentVersionId,string MetricsJson,string ProposedChangesJson,string EvidenceJson,decimal Confidence,string IdempotencyKey);
public sealed record MarketingSegmentLearningProposalDto(Guid Id,Guid SegmentVersionId,string MetricsJson,string ProposedChangesJson,string EvidenceJson,decimal Confidence,string Status,DateTime CreatedUtc);
public interface IMarketingMeasurementService
{
    Task<MarketingAttributionTouchDto> RecordTouchAsync(Guid companyId,RecordMarketingAttributionTouchRequest request,CancellationToken ct);
    Task<MarketingAttributionModelDto> CreateModelAsync(Guid companyId,CreateMarketingAttributionModelRequest request,CancellationToken ct);
    Task<MarketingAttributionRunDto> RunAttributionAsync(Guid companyId,RunMarketingAttributionRequest request,CancellationToken ct);
    Task RecordExposureAsync(Guid companyId,RecordMarketingExperimentExposureRequest request,CancellationToken ct);
    Task<MarketingExperimentDecisionDto> EvaluateExperimentAsync(Guid companyId,EvaluateMarketingExperimentRequest request,CancellationToken ct);
    Task<MarketingSegmentLearningProposalDto> ProposeSegmentLearningAsync(Guid companyId,CreateMarketingSegmentLearningProposalRequest request,CancellationToken ct);
}
