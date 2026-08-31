namespace VirtualCompany.Domain.Entities;

public static class AccountantGrantStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}

public static class AccountantGrantScopes
{
    public const string AccountingReview = "accounting_review";
}

public static class AccountantEngagementStatuses
{
    public const string Open = "open";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public static class AccountantReviewItemStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
}

public static class AccountantEvidenceRequestStatuses
{
    public const string Open = "open";
    public const string Responded = "responded";
    public const string Resolved = "resolved";
}

public sealed class AccountantCompanyGrant : ICompanyOwnedEntity
{
    private AccountantCompanyGrant() { }

    public AccountantCompanyGrant(Guid id, Guid companyId, Guid membershipId, Guid accountantUserId,
        string scopeKey, bool canViewDocuments, bool canRequestEvidence, bool canSignOff,
        DateTime effectiveFromUtc, DateTime? effectiveUntilUtc, Guid invitedByUserId, DateTime utcNow)
    {
        if (companyId == Guid.Empty || membershipId == Guid.Empty || accountantUserId == Guid.Empty || invitedByUserId == Guid.Empty)
            throw new ArgumentException("Grant identities are required.");
        if (effectiveUntilUtc.HasValue && Utc(effectiveUntilUtc.Value) <= Utc(effectiveFromUtc))
            throw new ArgumentException("Grant expiry must be after its effective date.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MembershipId = membershipId;
        AccountantUserId = accountantUserId;
        ScopeKey = Required(scopeKey, 100).ToLowerInvariant();
        CanViewDocuments = canViewDocuments;
        CanRequestEvidence = canRequestEvidence;
        CanSignOff = canSignOff;
        EffectiveFromUtc = Utc(effectiveFromUtc);
        EffectiveUntilUtc = effectiveUntilUtc.HasValue ? Utc(effectiveUntilUtc.Value) : null;
        InvitedByUserId = invitedByUserId;
        Status = AccountantGrantStatuses.PendingApproval;
        CreatedUtc = UpdatedUtc = Utc(utcNow);
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MembershipId { get; private set; }
    public Guid AccountantUserId { get; private set; }
    public string ScopeKey { get; private set; } = string.Empty;
    public bool CanViewDocuments { get; private set; }
    public bool CanRequestEvidence { get; private set; }
    public bool CanSignOff { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveUntilUtc { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public DateTime? LastAccessUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public CompanyMembership Membership { get; private set; } = null!;
    public ICollection<AccountantReviewEngagement> Engagements { get; private set; } = new List<AccountantReviewEngagement>();

    public bool IsEffectiveAt(DateTime utcNow) => Status == AccountantGrantStatuses.Active &&
        EffectiveFromUtc <= Utc(utcNow) && (!EffectiveUntilUtc.HasValue || EffectiveUntilUtc > Utc(utcNow));

    public void Approve(Guid actorUserId, DateTime utcNow)
    {
        if (Status != AccountantGrantStatuses.PendingApproval) throw new InvalidOperationException("Only pending grants can be approved.");
        if (actorUserId == Guid.Empty || actorUserId == InvitedByUserId) throw new InvalidOperationException("A different authorized user must approve the grant.");
        ApprovedByUserId = actorUserId;
        ApprovedUtc = Utc(utcNow);
        Status = AccountantGrantStatuses.Active;
        Touch(utcNow);
    }

    public void Revoke(Guid actorUserId, string reason, DateTime utcNow)
    {
        if (Status is AccountantGrantStatuses.Revoked or AccountantGrantStatuses.Expired) return;
        RevokedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("Revoker is required.") : actorUserId;
        RevokedUtc = Utc(utcNow);
        RevocationReason = Required(reason, 1000);
        Status = AccountantGrantStatuses.Revoked;
        Touch(utcNow);
    }

    public void RecordAccess(DateTime utcNow)
    {
        if (!IsEffectiveAt(utcNow)) throw new UnauthorizedAccessException("The accountant grant is not active.");
        LastAccessUtc = Utc(utcNow);
        Touch(utcNow);
    }

    private void Touch(DateTime utcNow) { UpdatedUtc = Utc(utcNow); Version++; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
        ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantReviewEngagement : ICompanyOwnedEntity
{
    private AccountantReviewEngagement() { }
    public AccountantReviewEngagement(Guid id, Guid companyId, Guid grantId, Guid? fiscalPeriodId,
        string title, string engagementType, Guid assignedAccountantUserId, Guid preparedByUserId,
        DateTime dueUtc, DateTime utcNow)
    {
        if (companyId == Guid.Empty || grantId == Guid.Empty || assignedAccountantUserId == Guid.Empty || preparedByUserId == Guid.Empty)
            throw new ArgumentException("Engagement identities are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId; GrantId = grantId; FiscalPeriodId = fiscalPeriodId;
        Title = Required(title, 200); EngagementType = Required(engagementType, 64).ToLowerInvariant();
        AssignedAccountantUserId = assignedAccountantUserId; PreparedByUserId = preparedByUserId;
        DueUtc = Utc(dueUtc); Status = AccountantEngagementStatuses.Open;
        CreatedUtc = UpdatedUtc = Utc(utcNow); Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid GrantId { get; private set; }
    public Guid? FiscalPeriodId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string EngagementType { get; private set; } = string.Empty;
    public Guid AssignedAccountantUserId { get; private set; }
    public Guid PreparedByUserId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime DueUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public AccountantCompanyGrant Grant { get; private set; } = null!;
    public FiscalPeriod? FiscalPeriod { get; private set; }
    public ICollection<AccountantReviewItem> ReviewItems { get; private set; } = new List<AccountantReviewItem>();
    public ICollection<AccountantEvidenceRequest> EvidenceRequests { get; private set; } = new List<AccountantEvidenceRequest>();
    public ICollection<AccountantEngagementSignOff> SignOffs { get; private set; } = new List<AccountantEngagementSignOff>();
    public ICollection<AccountantReviewHistory> History { get; private set; } = new List<AccountantReviewHistory>();
    public void Complete(DateTime utcNow) { if (Status != AccountantEngagementStatuses.Open) throw new InvalidOperationException("Only open engagements can be completed."); Status = AccountantEngagementStatuses.Completed; CompletedUtc = Utc(utcNow); Touch(utcNow); }
    private void Touch(DateTime utcNow) { UpdatedUtc = Utc(utcNow); Version++; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantReviewItem : ICompanyOwnedEntity
{
    private AccountantReviewItem() { }
    public AccountantReviewItem(Guid id, Guid companyId, Guid engagementId, bool isFinding, string severity,
        string content, string targetType, Guid? targetId, Guid createdByUserId, DateTime utcNow)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EngagementId = engagementId;
        IsFinding = isFinding; Severity = Required(severity, 32).ToLowerInvariant(); Content = Required(content, 4000);
        TargetType = Required(targetType, 64).ToLowerInvariant(); TargetId = targetId;
        CreatedByUserId = createdByUserId; CreatedUtc = Utc(utcNow); Status = AccountantReviewItemStatuses.Open;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EngagementId { get; private set; }
    public bool IsFinding { get; private set; }
    public string Severity { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid? TargetId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public string? ResolutionSummary { get; private set; }
    public AccountantReviewEngagement Engagement { get; private set; } = null!;
    public void Resolve(Guid actorUserId, string summary, DateTime utcNow) { if (Status != AccountantReviewItemStatuses.Open) throw new InvalidOperationException("Review item is already resolved."); ResolvedByUserId = actorUserId; ResolutionSummary = Required(summary, 2000); ResolvedUtc = Utc(utcNow); Status = AccountantReviewItemStatuses.Resolved; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantEvidenceRequest : ICompanyOwnedEntity
{
    private AccountantEvidenceRequest() { }
    public AccountantEvidenceRequest(Guid id, Guid companyId, Guid engagementId, string requestText,
        string targetType, Guid? targetId, Guid requestedByUserId, Guid? assignedToUserId, DateTime dueUtc, DateTime utcNow)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EngagementId = engagementId;
        RequestText = Required(requestText, 4000); TargetType = Required(targetType, 64).ToLowerInvariant(); TargetId = targetId;
        RequestedByUserId = requestedByUserId; AssignedToUserId = assignedToUserId; DueUtc = Utc(dueUtc);
        Status = AccountantEvidenceRequestStatuses.Open; CreatedUtc = UpdatedUtc = Utc(utcNow);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EngagementId { get; private set; }
    public string RequestText { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid? TargetId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public DateTime DueUtc { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public string? ResolutionSummary { get; private set; }
    public AccountantReviewEngagement Engagement { get; private set; } = null!;
    public ICollection<AccountantEvidenceResponse> Responses { get; private set; } = new List<AccountantEvidenceResponse>();
    public void RecordResponse(DateTime utcNow) { if (Status == AccountantEvidenceRequestStatuses.Resolved) throw new InvalidOperationException("Resolved requests cannot accept responses."); Status = AccountantEvidenceRequestStatuses.Responded; UpdatedUtc = Utc(utcNow); }
    public void Resolve(Guid actorUserId, string summary, DateTime utcNow) { if (Status == AccountantEvidenceRequestStatuses.Resolved) throw new InvalidOperationException("Request is already resolved."); ResolvedByUserId = actorUserId; ResolutionSummary = Required(summary, 2000); ResolvedUtc = UpdatedUtc = Utc(utcNow); Status = AccountantEvidenceRequestStatuses.Resolved; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantEvidenceResponse : ICompanyOwnedEntity
{
    private AccountantEvidenceResponse() { }
    public AccountantEvidenceResponse(Guid id, Guid companyId, Guid requestId, string responseText,
        Guid respondedByUserId, Guid? documentId, DateTime utcNow)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; RequestId = requestId; ResponseText = Required(responseText, 4000); RespondedByUserId = respondedByUserId; DocumentId = documentId; CreatedUtc = Utc(utcNow); }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RequestId { get; private set; }
    public string ResponseText { get; private set; } = string.Empty;
    public Guid RespondedByUserId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public AccountantEvidenceRequest Request { get; private set; } = null!;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantEngagementSignOff : ICompanyOwnedEntity
{
    private AccountantEngagementSignOff() { }
    public AccountantEngagementSignOff(Guid id, Guid companyId, Guid engagementId, Guid signedByUserId,
        string conclusion, string scopeSnapshot, DateTime utcNow)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EngagementId = engagementId; SignedByUserId = signedByUserId; Conclusion = Required(conclusion, 2000); ScopeSnapshot = Required(scopeSnapshot, 2000); SignedUtc = Utc(utcNow); }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EngagementId { get; private set; }
    public Guid SignedByUserId { get; private set; }
    public string Conclusion { get; private set; } = string.Empty;
    public string ScopeSnapshot { get; private set; } = string.Empty;
    public DateTime SignedUtc { get; private set; }
    public AccountantReviewEngagement Engagement { get; private set; } = null!;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}

public sealed class AccountantReviewHistory : ICompanyOwnedEntity
{
    private AccountantReviewHistory() { }
    public AccountantReviewHistory(Guid id, Guid companyId, Guid engagementId, string action,
        string targetType, Guid? targetId, Guid actorUserId, string safeSummary, DateTime utcNow)
    { Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EngagementId = engagementId; Action = Required(action, 100).ToLowerInvariant(); TargetType = Required(targetType, 64).ToLowerInvariant(); TargetId = targetId; ActorUserId = actorUserId; SafeSummary = Required(safeSummary, 2000); OccurredUtc = Utc(utcNow); }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EngagementId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid? TargetId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string SafeSummary { get; private set; } = string.Empty;
    public DateTime OccurredUtc { get; private set; }
    public AccountantReviewEngagement Engagement { get; private set; } = null!;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max ? value.Trim() : throw new ArgumentException($"Value is required and limited to {max} characters.");
}
