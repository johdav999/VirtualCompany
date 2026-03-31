using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Companies;

public static class CompanyOutboxTopics
{
    public const string InvitationCreated = "company.invitation.created";
    public const string InvitationDeliveryRequested = "company.invitation.delivery_requested";
    public const string InvitationResent = "company.invitation.resent";
    public const string InvitationRevoked = "company.invitation.revoked";
    public const string InvitationAccepted = "company.invitation.accepted";
    public const string MembershipRoleChanged = "company.membership.role_changed";
}

public sealed record CompanyInvitationDeliveryRequestedMessage(
    Guid InvitationId,
    Guid CompanyId,
    string CompanyName,
    string Email,
    CompanyMembershipRole Role,
    string AcceptanceToken,
    DateTime ExpiresAtUtc,
    Guid InvitedByUserId,
    string? CorrelationId);

public sealed record CompanyInvitationSendResult(string? ProviderMessageId);

public interface ICompanyOutboxEnqueuer
{
    void Enqueue(
        Guid companyId,
        string topic,
        object payload,
        string? correlationId = null,
        DateTime? availableAtUtc = null,
        string? idempotencyKey = null,
        string? messageType = null);
}

public interface ICompanyInvitationDeliveryDispatcher
{
    Task DispatchAsync(CompanyInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}

public interface ICompanyInvitationSender
{
    Task<CompanyInvitationSendResult> SendAsync(CompanyInvitationDeliveryRequestedMessage invitation, CancellationToken cancellationToken);
}