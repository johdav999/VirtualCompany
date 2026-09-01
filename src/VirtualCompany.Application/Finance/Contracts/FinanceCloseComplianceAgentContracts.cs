using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Finance;

public static class FinanceCloseComplianceAgentToolIds
{
    public const string ReadTemplates = "finance.close.read_templates";
    public const string ReadInstance = "finance.close.read_instance";
    public const string ReadReadiness = "finance.close.read_readiness";
    public const string ReadPeriodLockHistory = "finance.close.read_period_lock_history";
    public const string ReadComplianceObligations = "finance.compliance.read_obligations";
    public const string ReadAuditPackages = "finance.audit.read_packages";
    public const string ReadAccountantAccessActivity = "finance.accountant.read_access_activity";
    public const string ReadYearEnd = "finance.year_end.read_readiness_history";

    public const string PrioritizeCloseBlockers = "finance.close.recommend_blocker_priority";
    public const string ExplainCompliancePreparation = "finance.compliance.recommend_preparation";
    public const string ExplainAuditPackageCompleteness = "finance.audit.recommend_package_completeness";
    public const string ExplainYearEndPrerequisites = "finance.year_end.recommend_prerequisites";

    public static IReadOnlyList<string> ReadTools { get; } =
    [
        ReadTemplates, ReadInstance, ReadReadiness, ReadPeriodLockHistory,
        ReadComplianceObligations, ReadAuditPackages, ReadAccountantAccessActivity, ReadYearEnd
    ];

    public static IReadOnlyList<string> RecommendationTools { get; } =
    [
        PrioritizeCloseBlockers, ExplainCompliancePreparation,
        ExplainAuditPackageCompleteness, ExplainYearEndPrerequisites
    ];

    public static IReadOnlyList<string> All { get; } = [.. ReadTools, .. RecommendationTools];

    public static bool Contains(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && All.Contains(toolName.Trim(), StringComparer.OrdinalIgnoreCase);

    public static ToolActionType ActionFor(string toolName) =>
        RecommendationTools.Contains(toolName, StringComparer.OrdinalIgnoreCase)
            ? ToolActionType.Recommend
            : ToolActionType.Read;
}

public static class FinanceCloseComplianceAgentContract
{
    public const string Version = "finance-close-compliance-agent-v1";
    public const int MaximumPageSize = 100;
    public const int MaximumCalendarRangeDays = 366;
    public const int MaximumSourceIds = 2_000;
    public const string AuthorityNotice =
        "Technical readiness and recorded evidence are not filing, provider acknowledgement, human approval, professional approval, or statutory sign-off.";
}

public interface IFinanceCloseComplianceAgentService
{
    Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken);
}
