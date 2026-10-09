

namespace VirtualCompany.Application.Marketing;

public sealed record MarketingAttributionModelDto(Guid Id,string Name,string ModelType,int Version,string RulesJson,string Limitations,int LookbackDays);

public sealed record MarketingExperimentDecisionDto(Guid Id,Guid ExperimentId,string Decision,int SampleSize,decimal ContaminationRate,bool GuardrailBreached,bool CausalEligible,string EvidenceJson,string Limitations);
