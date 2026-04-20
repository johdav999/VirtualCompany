namespace VirtualCompany.Application.Briefings;

public sealed record GenerateDashboardBriefingSummaryQuery(
    Guid CompanyId,
    Guid UserId,
    int FocusItemCount = 5,
    int ActionItemCount = 5);

public sealed record DashboardBriefingSummaryDto(
    string Summary,
    DateTime GeneratedUtc,
    bool UsedArtificialIntelligence,
    string? Model);

public interface IDashboardBriefingSummaryService
{
    Task<DashboardBriefingSummaryDto> GenerateAsync(
        GenerateDashboardBriefingSummaryQuery query,
        CancellationToken cancellationToken);
}
