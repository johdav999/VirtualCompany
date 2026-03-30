using System.Diagnostics;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyMembershipAdministrationService : ICompanyMembershipAdministrationService
{
    private const int InvitationLifetimeDays = 14;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly IAuditEventWriter _auditEventWriter;

    public CompanyMembershipAdministrationService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        ICorrelationContextAccessor correlationContextAccessor,
        IAuditEventWriter auditEventWriter)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _outboxEnqueuer = outboxEnqueuer;
        _correlationContextAccessor = correlationContextAccessor;
        _auditEventWriter = auditEventWriter;
    }

    public async Task<IReadOnlyList<CompanyMemberDirectoryEntryDto>> GetMembershipsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await EnsureAdministrativeAccessAsync(companyId, cancellationToken);

        return await _dbContext.CompanyMemberships
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Status)
            .ThenBy(x => x.User != null ? x.User.DisplayName : x.InvitedEmail)
            .ThenBy(x => x.User != null ? x.User.Email : x.InvitedEmail)
            .Select(x => new CompanyMemberDirectoryEntryDto(
                x.Id,
                x.CompanyId,
                x.Company.Name,
                x.UserId,
                x.User != null ? x.User.Email : x.InvitedEmail!,
                x.User != null ? x.User.DisplayName : null,
                x.Role,
                x.Status,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CompanyInvitationDto>> GetInvitationsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await EnsureAdministrativeAccessAsync(companyId, cancellationToken);

        if (await SynchronizeExpiredInvitationsAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return await _dbContext.CompanyInvitations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .Select(ToInvitationDtoExpression())
            .ToListAsync(cancellationToken);
    }

    public async Task<CompanyInvitationDeliveryDto> InviteUserAsync(Guid companyId, InviteUserToCompanyRequest request, CancellationToken cancellationToken)
    {
        var companyContext = await EnsureAdministrativeAccessAsync(companyId, cancellationToken);
        var requestedRole = ValidateRequestedRole(request.MembershipRole);
        var email = NormalizeEmail(request.Email);

        if (await SynchronizeExpiredInvitationsAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var existingPendingInvitations = await _dbContext.CompanyInvitations
            .Where(
                x => x.CompanyId == companyId &&
                     x.Email == email &&
                     x.Status == CompanyInvitationStatus.Pending &&
                     x.ExpiresAtUtc > DateTime.UtcNow)
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var existingPendingInvitation = existingPendingInvitations.FirstOrDefault();
        var membership = await EnsurePendingMembershipAsync(companyId, email, requestedRole, cancellationToken);
        var token = GenerateInvitationToken();
        var expiresAtUtc = DateTime.UtcNow.AddDays(InvitationLifetimeDays);
        var correlationId = CreateCorrelationId();

        foreach (var duplicateInvitation in existingPendingInvitations.Skip(1))
        {
            duplicateInvitation.Cancel();
        }

        if (existingPendingInvitation is null)
        {
            var invitation = new CompanyInvitation(
                Guid.NewGuid(),
                companyId,
                email,
                requestedRole,
                companyContext.UserId,
                CompanyInvitationTokenHasher.ComputeHash(token),
                expiresAtUtc);

            _dbContext.CompanyInvitations.Add(invitation);

            EnqueueInvitationDeliveryRequested(invitation, companyContext.CompanyName, token, correlationId);
            _outboxEnqueuer.Enqueue(companyId, CompanyOutboxTopics.InvitationCreated, new
            {
                invitationId = invitation.Id,
                companyId,
                correlationId,
                membershipId = membership.Id,
                email,
                role = invitation.Role,
                invitedByUserId = companyContext.UserId
            });

            await WriteAuditEventAsync(
                companyId,
                companyContext.UserId,
                AuditEventActions.CompanyInvitationCreated,
                AuditTargetTypes.CompanyInvitation,
                invitation.Id,
                correlationId,
                cancellationToken,
                dataSources: ["company_membership_administration", "http_request"],
                metadata: CreateAuditMetadata(("email", email), ("membershipRole", invitation.Role.ToStorageValue())));

            await _dbContext.SaveChangesAsync(cancellationToken);
            return new CompanyInvitationDeliveryDto(ToInvitationDto(invitation), token, false);
        }

        existingPendingInvitation.Resend(CompanyInvitationTokenHasher.ComputeHash(token), expiresAtUtc, requestedRole, companyContext.UserId);
        EnqueueInvitationDeliveryRequested(existingPendingInvitation, companyContext.CompanyName, token, correlationId);

        _outboxEnqueuer.Enqueue(companyId, CompanyOutboxTopics.InvitationResent, new
        {
            invitationId = existingPendingInvitation.Id,
            companyId,
            correlationId,
            email = existingPendingInvitation.Email,
            role = existingPendingInvitation.Role,
            resentByUserId = companyContext.UserId
        });

        await WriteAuditEventAsync(
            companyId,
            companyContext.UserId,
            AuditEventActions.CompanyInvitationResent,
            AuditTargetTypes.CompanyInvitation,
            existingPendingInvitation.Id,
            correlationId,
            cancellationToken,
            dataSources: ["company_membership_administration", "http_request"],
            metadata: CreateAuditMetadata(("email", existingPendingInvitation.Email), ("membershipRole", existingPendingInvitation.Role.ToStorageValue())));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CompanyInvitationDeliveryDto(ToInvitationDto(existingPendingInvitation), token, true);
    }

    public async Task<CompanyInvitationDeliveryDto> ReinviteUserAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken)
    {
        var companyContext = await EnsureAdministrativeAccessAsync(companyId, cancellationToken);
        if (await SynchronizeExpiredInvitationsAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var invitation = await _dbContext.CompanyInvitations
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invitationId, cancellationToken);

        if (invitation is null)
        {
            throw new KeyNotFoundException("Invitation not found.");
        }

        if (invitation.Status != CompanyInvitationStatus.Pending)
        {
            var message = invitation.Status switch
            {
                CompanyInvitationStatus.Accepted => "Accepted invitations cannot be resent.",
                CompanyInvitationStatus.Revoked => "Revoked invitations cannot be resent.",
                CompanyInvitationStatus.Expired => "Expired invitations cannot be resent.",
                CompanyInvitationStatus.Cancelled => "Cancelled invitations cannot be resent.",
                _ => "Only outstanding invitations can be resent."
            };

            throw BuildValidationException("InvitationId", message);
        }

        await EnsurePendingMembershipAsync(companyId, invitation.Email, invitation.Role, cancellationToken);

        var token = GenerateInvitationToken();
        invitation.Resend(CompanyInvitationTokenHasher.ComputeHash(token), DateTime.UtcNow.AddDays(InvitationLifetimeDays), invitation.Role, companyContext.UserId);
        var correlationId = CreateCorrelationId();
        EnqueueInvitationDeliveryRequested(invitation, companyContext.CompanyName, token, correlationId);
        EnqueueInvitationResent(invitation, companyId, companyContext.UserId, correlationId);

        await WriteAuditEventAsync(
            companyId,
            companyContext.UserId,
            AuditEventActions.CompanyInvitationResent,
            AuditTargetTypes.CompanyInvitation,
            invitation.Id,
            correlationId,
            cancellationToken,
            dataSources: ["company_membership_administration", "http_request"],
            metadata: CreateAuditMetadata(("email", invitation.Email), ("membershipRole", invitation.Role.ToStorageValue())));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new CompanyInvitationDeliveryDto(ToInvitationDto(invitation), token, true);
    }

    public async Task<CompanyInvitationDto> RevokeInvitationAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken)
    {
        var companyContext = await EnsureAdministrativeAccessAsync(companyId, cancellationToken);
        if (await SynchronizeExpiredInvitationsAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var correlationId = CreateCorrelationId();

        var invitation = await _dbContext.CompanyInvitations
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invitationId, cancellationToken);

        if (invitation is null)
        {
            throw new KeyNotFoundException("Invitation not found.");
        }

        if (invitation.Status == CompanyInvitationStatus.Revoked)
        {
            return ToInvitationDto(invitation);
        }

        if (invitation.Status != CompanyInvitationStatus.Pending)
        {
            var message = invitation.Status switch
            {
                CompanyInvitationStatus.Accepted => "Accepted invitations cannot be revoked.",
                CompanyInvitationStatus.Expired => "Expired invitations cannot be revoked.",
                CompanyInvitationStatus.Cancelled => "Cancelled invitations cannot be revoked.",
                _ => "Only outstanding invitations can be revoked."
            };

            throw BuildValidationException("InvitationId", message);
        }

        invitation.Revoke();
        var membership = await FindMembershipByInvitationTargetAsync(companyId, invitation.Email, cancellationToken);
        if (membership is not null && membership.Status == CompanyMembershipStatus.Pending)
        {
            membership.UpdateStatus(CompanyMembershipStatus.Revoked);
        }

        _outboxEnqueuer.Enqueue(companyId, CompanyOutboxTopics.InvitationRevoked, new
        {
            invitationId = invitation.Id,
            companyId,
            correlationId,
            email = invitation.Email,
            revokedByUserId = companyContext.UserId
        });

        await WriteAuditEventAsync(
            companyId,
            companyContext.UserId,
            AuditEventActions.CompanyInvitationRevoked,
            AuditTargetTypes.CompanyInvitation,
            invitation.Id,
            correlationId,
            cancellationToken,
            dataSources: ["company_membership_administration", "http_request"],
            metadata: CreateAuditMetadata(("email", invitation.Email), ("membershipRole", invitation.Role.ToStorageValue())));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToInvitationDto(invitation);
    }

    public async Task<CompanyMemberDirectoryEntryDto> ChangeMembershipRoleAsync(Guid companyId, Guid membershipId, ChangeCompanyMembershipRoleRequest request, CancellationToken cancellationToken)
    {
        var companyContext = await EnsureAdministrativeAccessAsync(companyId, cancellationToken);
        var requestedRole = ValidateRequestedRole(request.MembershipRole);
        var correlationId = CreateCorrelationId();

        var membership = await _dbContext.CompanyMemberships
            .Include(x => x.User)
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == membershipId, cancellationToken);

        if (membership is null)
        {
            throw new KeyNotFoundException("Membership not found.");
        }

        if (membership.Status != CompanyMembershipStatus.Active)
        {
            throw BuildValidationException("MembershipId", "Only active memberships can have their role updated.");
        }

        if (membership.Role == CompanyMembershipRole.Owner &&
            requestedRole != CompanyMembershipRole.Owner)
        {
            var activeOwnerCount = await _dbContext.CompanyMemberships.CountAsync(
                x => x.CompanyId == companyId &&
                     x.Status == CompanyMembershipStatus.Active &&
                     x.Role == CompanyMembershipRole.Owner,
                cancellationToken);

            if (activeOwnerCount <= 1)
            {
                throw BuildValidationException("Role", "The last active owner cannot be demoted.");
            }
        }
        var previousRole = membership.Role;

        if (membership.Role != requestedRole)
        {
            membership.UpdateRole(requestedRole);

            _outboxEnqueuer.Enqueue(companyId, CompanyOutboxTopics.MembershipRoleChanged, new
            {
                membershipId = membership.Id,
                companyId,
                correlationId,
                userId = membership.UserId,
                previousRole,
                newRole = request.MembershipRole,
                changedByUserId = companyContext.UserId
            });

            await WriteAuditEventAsync(
                companyId,
                companyContext.UserId,
                AuditEventActions.CompanyMembershipRoleChanged,
                AuditTargetTypes.CompanyMembership,
                membership.Id,
                correlationId,
                cancellationToken,
                rationaleSummary: "Administrative role change approved inside the company membership boundary.",
                dataSources: ["company_membership_administration", "http_request"],
                metadata: CreateAuditMetadata(("previousRole", previousRole.ToStorageValue()), ("newRole", requestedRole.ToStorageValue()), ("userId", membership.UserId?.ToString("N"))));

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToMembershipDto(membership);
    }

    public async Task<AcceptCompanyInvitationResultDto> AcceptInvitationAsync(AcceptCompanyInvitationRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireCurrentUserId();
        var currentUser = await _dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authenticated user could not be resolved.");

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw BuildValidationException("Token", "Invitation token is required.");
        }

        var invitation = await _dbContext.CompanyInvitations
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.TokenHash == CompanyInvitationTokenHasher.ComputeHash(request.Token), cancellationToken);

        if (invitation is null)
        {
            throw BuildValidationException("Token", "The invitation token is invalid.");
        }

        invitation.SyncExpiration(DateTime.UtcNow);
        EnsureAcceptableInvitation(invitation);

        if (!string.Equals(currentUser.Email, invitation.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw BuildValidationException("Token", "The signed-in user email does not match the invitation email.");
        }

        var membership = await _dbContext.CompanyMemberships
            .SingleOrDefaultAsync(x => x.CompanyId == invitation.CompanyId &&
                                       (x.UserId == userId || x.InvitedEmail == invitation.Email), cancellationToken);

        if (membership?.Status == CompanyMembershipStatus.Active)
        {
            throw BuildValidationException("Token", "The invited user is already an active member of this company.");
        }

        if (membership is null)
        {
            membership = new CompanyMembership(
                Guid.NewGuid(),
                invitation.CompanyId,
                userId,
                invitation.Role,
                CompanyMembershipStatus.Active);

            _dbContext.CompanyMemberships.Add(membership);
        }
        else
        {
            if (membership.Status != CompanyMembershipStatus.Pending)
            {
                throw BuildValidationException("Token", "Only pending memberships can accept this invitation.");
            }

            membership.UpdateRole(invitation.Role);
            membership.Accept(userId);
        }

        invitation.Accept(userId);

        var duplicateInvitations = await _dbContext.CompanyInvitations
            .Where(x => x.CompanyId == invitation.CompanyId &&
                        x.Email == invitation.Email &&
                        x.Id != invitation.Id &&
                        x.Status == CompanyInvitationStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var duplicateInvitation in duplicateInvitations)
        {
            duplicateInvitation.Cancel();
        }

        var correlationId = CreateCorrelationId();
        _outboxEnqueuer.Enqueue(invitation.CompanyId, CompanyOutboxTopics.InvitationAccepted, new
        {
            invitationId = invitation.Id,
            companyId = invitation.CompanyId,
            correlationId,
            membershipId = membership.Id,
            acceptedByUserId = userId,
            email = invitation.Email,
            role = invitation.Role
        });

        await WriteAuditEventAsync(
            invitation.CompanyId,
            userId,
            AuditEventActions.CompanyInvitationAccepted,
            AuditTargetTypes.CompanyInvitation,
            invitation.Id,
            correlationId,
            cancellationToken,
            rationaleSummary: "Invitation token redeemed by the invited user.",
            dataSources: ["http_request", "invitation_token"],
            metadata: CreateAuditMetadata(("email", invitation.Email), ("membershipRole", invitation.Role.ToStorageValue()), ("membershipId", membership.Id.ToString("N"))));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new AcceptCompanyInvitationResultDto(
            invitation.CompanyId,
            invitation.Company.Name,
            membership.Id,
            membership.Role,
            membership.Status);
    }

    private async Task<ResolvedCompanyMembershipContext> EnsureAdministrativeAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null ||
            (membership.MembershipRole != CompanyMembershipRole.Owner && membership.MembershipRole != CompanyMembershipRole.Admin))
        {
            throw new UnauthorizedAccessException("Only owner and admin memberships can manage company invitations and roles.");
        }

        return membership;
    }

    private async Task<CompanyMembership> EnsurePendingMembershipAsync(Guid companyId, string email, CompanyMembershipRole role, CancellationToken cancellationToken)
    {
        await EnsureInvitableAsync(companyId, email, cancellationToken);

        var membership = await FindMembershipByInvitationTargetAsync(companyId, email, cancellationToken);
        if (membership is null)
        {
            membership = new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId: null,
                role,
                CompanyMembershipStatus.Pending,
                invitedEmail: email);

            _dbContext.CompanyMemberships.Add(membership);
            return membership;
        }

        if (membership.Status == CompanyMembershipStatus.Active)
        {
            throw BuildValidationException("Email", "The invited email is already an active company member.");
        }

        membership.RefreshInvitation(role, email);
        return membership;
    }

    private void EnqueueInvitationDeliveryRequested(
        CompanyInvitation invitation,
        string companyName,
        string acceptanceToken,
        string correlationId)
    {
        _outboxEnqueuer.Enqueue(
            invitation.CompanyId,
            CompanyOutboxTopics.InvitationDeliveryRequested,
            new CompanyInvitationDeliveryRequestedMessage(
                invitation.Id,
                invitation.CompanyId,
                companyName,
                invitation.Email,
                invitation.Role,
                acceptanceToken,
                invitation.ExpiresAtUtc,
                invitation.InvitedByUserId,
                correlationId),
            correlationId);
    }

    private void EnqueueInvitationResent(CompanyInvitation invitation, Guid companyId, Guid resentByUserId, string correlationId)
    {
        _outboxEnqueuer.Enqueue(companyId, CompanyOutboxTopics.InvitationResent, new
        {
            invitationId = invitation.Id,
            companyId,
            correlationId,
            email = invitation.Email,
            role = invitation.Role,
            resentByUserId
        });
    }

    private async Task EnsureInvitableAsync(Guid companyId, string email, CancellationToken cancellationToken)
    {
        var isActiveMember = await _dbContext.CompanyMemberships
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId &&
                           x.Status == CompanyMembershipStatus.Active &&
                           ((x.User != null && x.User.Email == email) || x.InvitedEmail == email), cancellationToken);

        if (isActiveMember)
        {
            throw BuildValidationException("Email", "The invited email is already an active company member.");
        }
    }

    private Task<CompanyMembership?> FindMembershipByInvitationTargetAsync(Guid companyId, string email, CancellationToken cancellationToken)
    {
        return _dbContext.CompanyMemberships
            .SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                                       (x.InvitedEmail == email || (x.User != null && x.User.Email == email)),
                cancellationToken);
    }

    private async Task<bool> SynchronizeExpiredInvitationsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var invitations = await _dbContext.CompanyInvitations
            .Where(x => x.CompanyId == companyId &&
                        x.Status == CompanyInvitationStatus.Pending &&
                        x.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var invitation in invitations)
        {
            invitation.SyncExpiration(now);
        }

        return invitations.Count > 0;
    }

    private static void EnsureAcceptableInvitation(CompanyInvitation invitation)
    {
        if (invitation.Status == CompanyInvitationStatus.Pending)
        {
            return;
        }

        var message = invitation.Status switch
        {
            CompanyInvitationStatus.Accepted => "This invitation has already been accepted.",
            CompanyInvitationStatus.Revoked => "This invitation has been revoked.",
            CompanyInvitationStatus.Expired => "This invitation has expired.",
            CompanyInvitationStatus.Cancelled => "This invitation is no longer active.",
            _ => "This invitation is not valid."
        };

        throw BuildValidationException("Token", message);
    }

    private Guid RequireCurrentUserId() =>
        _currentUserAccessor.UserId
        ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw BuildValidationException("Email", "Email is required.");
        }

        var normalized = email.Trim().ToLowerInvariant();
        try
        {
            _ = new MailAddress(normalized);
        }
        catch (FormatException)
        {
            throw BuildValidationException("Email", "Email must be a valid address.");
        }

        return normalized;
    }

    private static CompanyMembershipRole ValidateRequestedRole(CompanyMembershipRole role)
    {
        if (CompanyMembershipRoles.IsSupported(role))
        {
            return role;
        }

        throw BuildValidationException("Role", CompanyMembershipRoles.BuildValidationMessage());
    }

    private static string GenerateInvitationToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string CreateCorrelationId() =>
        string.IsNullOrWhiteSpace(_correlationContextAccessor.CorrelationId)
            ? Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
            : _correlationContextAccessor.CorrelationId!;

    private Task WriteAuditEventAsync(
        Guid companyId,
        Guid? actorId,
        string action,
        string targetType,
        Guid targetId,
        string correlationId,
        CancellationToken cancellationToken,
        string? rationaleSummary = null,
        IReadOnlyCollection<string>? dataSources = null,
        IReadOnlyDictionary<string, string?>? metadata = null) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorId,
                action,
                targetType,
                targetId.ToString("N"),
                AuditEventOutcomes.Succeeded,
                rationaleSummary,
                dataSources,
                metadata,
                correlationId),
            cancellationToken);

    private static IReadOnlyDictionary<string, string?> CreateAuditMetadata(params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            metadata[key] = value;
        }

        return metadata;
    }

    private static Expression<Func<CompanyInvitation, CompanyInvitationDto>> ToInvitationDtoExpression() =>
        x => new CompanyInvitationDto(
            x.Id,
            x.CompanyId,
            x.Email,
            x.Role,
            x.Status,
            x.InvitedByUserId,
            x.AcceptedByUserId,
            x.ExpiresAtUtc,
            x.LastSentUtc,
            x.CreatedUtc,
            x.UpdatedUtc);

    private static CompanyInvitationDto ToInvitationDto(CompanyInvitation invitation) =>
        new(
            invitation.Id,
            invitation.CompanyId,
            invitation.Email,
            invitation.Role,
            invitation.Status,
            invitation.InvitedByUserId,
            invitation.AcceptedByUserId,
            invitation.ExpiresAtUtc,
            invitation.LastSentUtc,
            invitation.CreatedUtc,
            invitation.UpdatedUtc);

    private static CompanyMemberDirectoryEntryDto ToMembershipDto(CompanyMembership membership) =>
        new(
            membership.Id,
            membership.CompanyId,
            membership.Company.Name,
            membership.UserId,
            membership.User?.Email ?? membership.InvitedEmail ?? string.Empty,
            membership.User?.DisplayName,
            membership.Role,
            membership.Status,
            membership.CreatedUtc,
            membership.UpdatedUtc);

    private static CompanyMembershipAdministrationValidationException BuildValidationException(string key, string message) =>
        new(new Dictionary<string, string[]>
        {
            [key] = [message]
        });
}
