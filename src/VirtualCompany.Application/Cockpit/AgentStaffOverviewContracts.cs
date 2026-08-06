namespace VirtualCompany.Application.Cockpit;

public sealed record GetAgentStaffOverviewQuery(
    Guid CompanyId,
    int? Year = null,
    int? Month = null,
    bool IncludeAllTasks = false);

public sealed record AgentStaffOverviewDto(
    Guid CompanyId,
    string CompanyName,
    DateTime GeneratedAtUtc,
    AgentStaffOverviewPeriodDto Period,
    AgentStaffFinancialSummaryDto Finance,
    AgentStaffSalesSummaryDto Sales,
    AgentStaffSupportSummaryDto Support,
    AgentStaffStageCountsDto StageCounts,
    IReadOnlyList<AgentStaffRowDto> Agents,
    IReadOnlyList<AgentStaffAttentionItemDto> AttentionItems);

public sealed record AgentStaffOverviewPeriodDto(
    int Year,
    int Month,
    DateTime StartUtc,
    DateTime EndUtc,
    string Label);

public sealed record AgentStaffFinancialSummaryDto(
    bool CanView,
    bool IsInitialized,
    bool HasData,
    decimal? Revenue,
    decimal? Costs,
    decimal? Result,
    string? Currency,
    decimal? RevenueChangePercentage,
    decimal? CostsChangePercentage,
    decimal? ResultChangePercentage,
    string Explanation,
    string Route);

public sealed record AgentStaffSalesSummaryDto(
    bool HasData,
    decimal PipelineValue,
    decimal ForecastRevenue,
    string Currency,
    int DealsNeedingAttention,
    string Route);

public sealed record AgentStaffSupportSummaryDto(
    int CasesAtSlaRisk,
    int BreachedCases,
    int OpenCases,
    string Route);

public sealed record AgentStaffStageCountsDto(
    int Planned,
    int InProgress,
    int AwaitingHumanApproval,
    int Completed);

public sealed record AgentStaffRowDto(
    Guid AgentId,
    string DisplayName,
    string RoleName,
    string Department,
    string Status,
    string? AvatarUrl,
    string ProfileRoute,
    IReadOnlyList<AgentStaffTaskDto> Planned,
    IReadOnlyList<AgentStaffTaskDto> InProgress,
    IReadOnlyList<AgentStaffTaskDto> AwaitingHumanApproval,
    IReadOnlyList<AgentStaffTaskDto> Completed,
    AgentStaffStageCountsDto StageCounts);

public sealed record AgentStaffTaskDto(
    Guid Id,
    string Title,
    string Context,
    string Priority,
    string Status,
    DateTime? DueUtc,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc,
    string Route,
    Guid? ApprovalId,
    string? ApprovalRoute);

public sealed record AgentStaffAttentionItemDto(
    string Key,
    string Severity,
    string Title,
    string Summary,
    string? ActionLabel,
    string? Route);

public interface IAgentStaffOverviewQueryService
{
    Task<AgentStaffOverviewDto> GetAsync(
        GetAgentStaffOverviewQuery query,
        CancellationToken cancellationToken);
}
