namespace VirtualCompany.Application.Companies;

public sealed record AccountantGrantDto(Guid Id, Guid CompanyId, string CompanyName, Guid MembershipId,
    Guid AccountantUserId, string AccountantName, string ScopeKey, bool CanViewDocuments,
    bool CanRequestEvidence, bool CanSignOff, string Status, DateTime EffectiveFromUtc,
    DateTime? EffectiveUntilUtc, Guid InvitedByUserId, Guid? ApprovedByUserId, Guid? RevokedByUserId,
    DateTime? LastAccessUtc, DateTime CreatedUtc, DateTime UpdatedUtc, long Version);

public sealed record AccountantPortfolioCompanyDto(Guid CompanyId, string CompanyName, Guid GrantId,
    string GrantStatus, DateTime EffectiveFromUtc, DateTime? EffectiveUntilUtc, DateTime? LastAccessUtc,
    int OpenEngagements, DateTime? NextDueUtc, string CloseStatus, int VatOrComplianceIssues,
    int UnreconciledItems, int FailedIntegrations, int PendingApprovals, int OpenEvidenceRequests,
    int OverdueEvidenceRequests);

public sealed record AccountantPortfolioDto(int ActiveCompanyCount, int ClosingSoonCount, int HighRiskCount,
    int OpenEvidenceRequestCount, IReadOnlyList<AccountantPortfolioCompanyDto> Companies);

public sealed record AccountantReviewItemDto(Guid Id, bool IsFinding, string Severity, string Content,
    string TargetType, Guid? TargetId, string Status, Guid CreatedByUserId, DateTime CreatedUtc,
    Guid? ResolvedByUserId, DateTime? ResolvedUtc, string? ResolutionSummary);

public sealed record AccountantEvidenceResponseDto(Guid Id, string ResponseText, Guid RespondedByUserId,
    Guid? DocumentId, bool DocumentAccessible, DateTime CreatedUtc);

public sealed record AccountantEvidenceRequestDto(Guid Id, string RequestText, string TargetType,
    Guid? TargetId, Guid RequestedByUserId, Guid? AssignedToUserId, DateTime DueUtc, string Status,
    DateTime CreatedUtc, DateTime UpdatedUtc, string? ResolutionSummary,
    IReadOnlyList<AccountantEvidenceResponseDto> Responses);

public sealed record AccountantSignOffDto(Guid Id, Guid SignedByUserId, string Conclusion,
    string ScopeSnapshot, DateTime SignedUtc);

public sealed record AccountantReviewHistoryDto(Guid Id, string Action, string TargetType, Guid? TargetId,
    Guid ActorUserId, string SafeSummary, DateTime OccurredUtc);

public sealed record AccountantEngagementDto(Guid Id, Guid CompanyId, string CompanyName, Guid GrantId,
    Guid? FiscalPeriodId, string? FiscalPeriodName, string Title, string EngagementType,
    Guid AssignedAccountantUserId, Guid PreparedByUserId, string Status, DateTime DueUtc,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc, long Version,
    IReadOnlyList<AccountantReviewItemDto> ReviewItems,
    IReadOnlyList<AccountantEvidenceRequestDto> EvidenceRequests,
    IReadOnlyList<AccountantSignOffDto> SignOffs,
    IReadOnlyList<AccountantReviewHistoryDto> History);

public sealed record CreateAccountantGrantCommand(Guid CompanyId, Guid MembershipId, string ScopeKey,
    bool CanViewDocuments, bool CanRequestEvidence, bool CanSignOff, DateTime EffectiveFromUtc,
    DateTime? EffectiveUntilUtc, Guid ActorUserId);
public sealed record ApproveAccountantGrantCommand(Guid CompanyId, Guid GrantId, Guid ActorUserId, long ExpectedVersion);
public sealed record RevokeAccountantGrantCommand(Guid CompanyId, Guid GrantId, Guid ActorUserId, string Reason, long ExpectedVersion);
public sealed record CreateAccountantEngagementCommand(Guid CompanyId, Guid GrantId, Guid? FiscalPeriodId,
    string Title, string EngagementType, Guid PreparedByUserId, DateTime DueUtc);
public sealed record AddAccountantReviewItemCommand(Guid CompanyId, Guid EngagementId, bool IsFinding,
    string Severity, string Content, string TargetType, Guid? TargetId, Guid ActorUserId);
public sealed record ResolveAccountantReviewItemCommand(Guid CompanyId, Guid EngagementId, Guid ItemId,
    string ResolutionSummary, Guid ActorUserId);
public sealed record CreateAccountantEvidenceRequestCommand(Guid CompanyId, Guid EngagementId,
    string RequestText, string TargetType, Guid? TargetId, Guid? AssignedToUserId, DateTime DueUtc, Guid ActorUserId);
public sealed record RespondToAccountantEvidenceRequestCommand(Guid CompanyId, Guid EngagementId,
    Guid RequestId, string ResponseText, Guid? DocumentId, Guid ActorUserId);
public sealed record ResolveAccountantEvidenceRequestCommand(Guid CompanyId, Guid EngagementId,
    Guid RequestId, string ResolutionSummary, Guid ActorUserId);
public sealed record SignOffAccountantEngagementCommand(Guid CompanyId, Guid EngagementId,
    string Conclusion, Guid ActorUserId, long ExpectedVersion);

public interface IAccountantCollaborationService
{
    Task<AccountantPortfolioDto> GetPortfolioAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountantGrantDto>> ListGrantsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AccountantGrantDto> CreateGrantAsync(CreateAccountantGrantCommand command, CancellationToken cancellationToken);
    Task<AccountantGrantDto> ApproveGrantAsync(ApproveAccountantGrantCommand command, CancellationToken cancellationToken);
    Task<AccountantGrantDto> RevokeGrantAsync(RevokeAccountantGrantCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountantEngagementDto>> ListEngagementsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> GetEngagementAsync(Guid companyId, Guid engagementId, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> CreateEngagementAsync(CreateAccountantEngagementCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> AddReviewItemAsync(AddAccountantReviewItemCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> ResolveReviewItemAsync(ResolveAccountantReviewItemCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> CreateEvidenceRequestAsync(CreateAccountantEvidenceRequestCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> RespondToEvidenceRequestAsync(RespondToAccountantEvidenceRequestCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> ResolveEvidenceRequestAsync(ResolveAccountantEvidenceRequestCommand command, CancellationToken cancellationToken);
    Task<AccountantEngagementDto> SignOffAsync(SignOffAccountantEngagementCommand command, CancellationToken cancellationToken);
}

public sealed class AccountantCollaborationException(string reasonCode, string message, bool conflict = false) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
    public bool IsConflict { get; } = conflict;
}
