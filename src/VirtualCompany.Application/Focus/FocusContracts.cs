using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Focus;

public sealed record GetDashboardFocusQuery(
    Guid CompanyId,
    Guid UserId,
    bool ForPriorityWorkspace = false);

public sealed record FocusItemDto(
    string Id,
    string Title,
    string Description,
    string ActionType,
    int PriorityScore,
    string NavigationTarget,
    string? SourceType = null,
    DateTime? ObservedAtUtc = null,
    DateTime? DueUtc = null,
    string? SourceState = null,
    Guid? RelatedTaskId = null,
    Guid? RelatedApprovalId = null,
    string? ResponsiblePerson = null,
    string? WorkingAgent = null, string? PriorityEvidenceKey = null);

public sealed record FocusCandidate(
    string Id,
    string Title,
    string Description,
    string ActionType,
    string NavigationTarget,
    FocusSourceType SourceType,
    double RawScore,
    DateTime? SortUtc,
    string StableSortKey,
    DateTime? ObservedAtUtc = null,
    DateTime? DueUtc = null,
    string? SourceState = null,
    Guid? RelatedTaskId = null,
    Guid? RelatedApprovalId = null,
    string? ResponsiblePerson = null,
    string? WorkingAgent = null, string? PriorityEvidenceKey = null);

public interface IFocusEngine
{
    Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken);
}

public interface IFocusCandidateSource
{
    Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken);
}
