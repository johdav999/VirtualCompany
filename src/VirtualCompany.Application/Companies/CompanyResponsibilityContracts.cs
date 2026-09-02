using System.Collections.ObjectModel;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Companies;

public sealed record ResponsibilityMemberDto(Guid MembershipId, Guid? UserId, string DisplayName, string Email,
    CompanyMembershipRole Role, CompanyMembershipStatus Status = CompanyMembershipStatus.Active);
public sealed record ResponsibilityAgentDto(Guid AgentId, string DisplayName, string RoleName, string Department,
    AgentStatus Status, IReadOnlyList<ResponsibilityArea>? CompatibleAreas = null);

public sealed record CompanyResponsibilityAssignmentDto(
    Guid Id, Guid CompanyId, ResponsibilityArea ResponsibilityArea, ResponsibilityAssignmentKind AssignmentKind,
    ResponsibilityMemberDto AssignedMember, ResponsibilityAgentDto? PrimaryAgent, string AuthorityLevel,
    Guid? ApprovalPolicyId, ResponsibilityMemberDto? EscalationMember, long Version, DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record ResponsibilityPresetMetadataDto(CompanySizeBand CompanySize, string Name, string Description,
    IReadOnlyList<ResponsibilityArea> ResponsibilityAreas, bool SupportsManagerSelections, bool AddsExecutiveOversight);

public sealed record CompanyResponsibilitiesDto(Guid CompanyId, CompanySizeBand CompanySize,
    IReadOnlyList<CompanyResponsibilityAssignmentDto> Assignments,
    IReadOnlyList<ResponsibilityPresetMetadataDto> AvailablePresets,
    bool CanManage = false,
    IReadOnlyList<ResponsibilityMemberDto>? Members = null,
    IReadOnlyList<ResponsibilityAgentDto>? Agents = null);

public sealed record ResponsibilityPresetRequest(
    CompanySizeBand CompanySize, Guid OwnerMembershipId,
    IReadOnlyDictionary<ResponsibilityArea, Guid>? ManagerMembershipIds = null,
    ResponsibilityPresetMode Mode = ResponsibilityPresetMode.FillMissing,
    string? Reason = null);

public enum ResponsibilityPresetChangeKind { Add = 1, Retain = 2, Replace = 3 }
public sealed record ResponsibilityPresetChangeDto(ResponsibilityArea ResponsibilityArea,
    ResponsibilityAssignmentKind AssignmentKind, ResponsibilityPresetChangeKind ChangeKind,
    Guid? PreviousMembershipId, Guid AssignedMembershipId, Guid? PreviousAgentId, Guid? PrimaryAgentId);
public sealed record ResponsibilityPresetPreviewDto(Guid CompanyId, CompanySizeBand CompanySize,
    ResponsibilityPresetMode Mode, IReadOnlyList<ResponsibilityPresetChangeDto> Changes);
public sealed record ResponsibilityPresetApplyResultDto(ResponsibilityPresetPreviewDto Preview,
    IReadOnlyList<CompanyResponsibilityAssignmentDto> Assignments);

public sealed record UpsertCompanyResponsibilityAssignmentCommand(
    Guid? AssignmentId, ResponsibilityArea ResponsibilityArea, ResponsibilityAssignmentKind AssignmentKind,
    Guid AssignedMembershipId, Guid? PrimaryAgentId, string AuthorityLevel,
    Guid? ApprovalPolicyId, Guid? EscalationMembershipId, long? ExpectedVersion, string? Reason);

public interface ICompanyResponsibilityService
{
    Task<CompanyResponsibilitiesDto> GetAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ResponsibilityPresetPreviewDto> PreviewPresetAsync(Guid companyId, ResponsibilityPresetRequest request, CancellationToken cancellationToken);
    Task<ResponsibilityPresetApplyResultDto> ApplyPresetAsync(Guid companyId, ResponsibilityPresetRequest request, CancellationToken cancellationToken);
    Task<CompanyResponsibilityAssignmentDto> UpsertAsync(Guid companyId, UpsertCompanyResponsibilityAssignmentCommand command, CancellationToken cancellationToken);
    Task RemoveAsync(Guid companyId, Guid assignmentId, long? expectedVersion, string? reason, CancellationToken cancellationToken);
}

public sealed class CompanyResponsibilityValidationException : Exception
{
    public CompanyResponsibilityValidationException(IDictionary<string, string[]> errors) : base("Responsibility assignment validation failed.")
        => Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class CompanyResponsibilityConflictException : Exception
{
    public CompanyResponsibilityConflictException(string message) : base(message) { }
}
