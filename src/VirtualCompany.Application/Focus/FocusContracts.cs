using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Focus;

public sealed record GetDashboardFocusQuery(
    Guid CompanyId,
    Guid UserId);

public sealed record FocusItemDto(
    string Id,
    string Title,
    string Description,
    string ActionType,
    int PriorityScore,
    string NavigationTarget,
    string? SourceType = null);

public sealed record FocusCandidate(
    string Id,
    string Title,
    string Description,
    string ActionType,
    string NavigationTarget,
    FocusSourceType SourceType,
    double RawScore,
    DateTime? SortUtc,
    string StableSortKey);

public interface IFocusEngine
{
    Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken);
}

public interface IFocusCandidateSource
{
    Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken);
}
