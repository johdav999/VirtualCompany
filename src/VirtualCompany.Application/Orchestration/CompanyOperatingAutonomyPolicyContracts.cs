using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Orchestration;

public enum CompanyOperatingAutonomyPhase
{
    AutomaticCommit = 1,
    Dispatch = 2
}

public sealed record CompanyOperatingAutonomyDecision(
    bool Allowed,
    bool ReviewRequired,
    string ReasonCode,
    string Explanation,
    CompanyAutonomyLevel EffectiveCompanyLevel,
    int ConfigurationVersion,
    IReadOnlyDictionary<string, string?> Evidence);

public interface ICompanyOperatingAutonomyPolicy
{
    Task<CompanyOperatingAutonomyDecision> EvaluateAsync(
        Guid companyId,
        Guid planId,
        CompanyOperatingAutonomyPhase phase,
        CancellationToken cancellationToken);
}
