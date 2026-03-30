namespace VirtualCompany.Application.Auditing;

public sealed record AuditEventWriteRequest(
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string? RationaleSummary = null,
    IReadOnlyCollection<string>? DataSources = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    string? CorrelationId = null,
    DateTime? OccurredUtc = null);

// Business audit history is an explicit application concern.
// Technical diagnostics belong on ILogger and must not be inferred from audit records.
public interface IAuditEventWriter
{
    Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken);
}

public static class AuditActorTypes
{
    public const string User = "user";
}

public static class AuditTargetTypes
{
    public const string CompanyInvitation = "company_invitation";
    public const string CompanyMembership = "company_membership";
}

public static class AuditEventOutcomes
{
    public const string Succeeded = "succeeded";
}

public static class AuditEventActions
{
    public const string CompanyInvitationCreated = "company.invitation.created";
    public const string CompanyInvitationResent = "company.invitation.resent";
    public const string CompanyInvitationRevoked = "company.invitation.revoked";
    public const string CompanyInvitationAccepted = "company.invitation.accepted";
    public const string CompanyMembershipRoleChanged = "company.membership.role_changed";
}
