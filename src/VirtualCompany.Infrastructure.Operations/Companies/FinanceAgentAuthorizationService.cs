using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAgentAuthorizationService : IFinanceAgentAuthorizationService
{
    public const string PolicyVersion = "finance-agent-actor-auth-v1";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICompanyMembershipContextResolver _membershipResolver;

    public FinanceAgentAuthorizationService(
        VirtualCompanyDbContext dbContext,
        ICurrentUserAccessor currentUserAccessor,
        ICompanyMembershipContextResolver membershipResolver)
    {
        _dbContext = dbContext;
        _currentUserAccessor = currentUserAccessor;
        _membershipResolver = membershipResolver;
    }

    public async Task<FinanceAgentAuthorizationDecisionDto> AuthorizeAsync(
        FinanceAgentAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var decision = await AuthorizeCoreAsync(request, cancellationToken);
        FinanceAgentAuthorityTelemetry.RecordAuthorization(decision);
        return decision;
    }

    private async Task<FinanceAgentAuthorizationDecisionDto> AuthorizeCoreAsync(
        FinanceAgentAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var evaluatedAtUtc = DateTime.UtcNow;
        var requirements = ResolveRequirements(request.ToolName, request.ActionType);

        if (request.ActorUserId.HasValue && request.IsApprovedContinuation)
        {
            return await AuthorizeHumanAsync(
                request, request.ActorUserId.Value, requirements, evaluatedAtUtc, cancellationToken);
        }

        if (request.ActorUserId.HasValue)
        {
            return Deny(request, FinanceAgentActorTypes.Missing, null, FinanceAgentMembershipStates.Missing,
                requirements, FinanceAgentAuthorizationReasonCodes.ActorMissing,
                "An interactive actor or persisted delegation is required.", [], evaluatedAtUtc);
        }

        if (_currentUserAccessor.UserId is Guid currentUserId)
        {
            return await AuthorizeCurrentHumanAsync(
                request, currentUserId, requirements, evaluatedAtUtc, cancellationToken);
        }

        if (!request.DelegationAuthorityId.HasValue)
        {
            return Deny(request, FinanceAgentActorTypes.Missing, null, FinanceAgentMembershipStates.Missing,
                requirements, FinanceAgentAuthorizationReasonCodes.ActorMissing,
                "An authorized Finance actor is required.", [], evaluatedAtUtc);
        }

        return await AuthorizeDelegationAsync(request, requirements, evaluatedAtUtc, cancellationToken);
    }

    private async Task<FinanceAgentAuthorizationDecisionDto> AuthorizeCurrentHumanAsync(
        FinanceAgentAuthorizationRequest request,
        Guid actorUserId,
        FinancePermissionRequirements requirements,
        DateTime evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(request.CompanyId, cancellationToken);
        if (membership is null || membership.UserId != actorUserId)
        {
            return Deny(request, FinanceAgentActorTypes.Human, actorUserId, FinanceAgentMembershipStates.Missing,
                requirements, FinanceAgentAuthorizationReasonCodes.MembershipMissing,
                "The Finance action is not available for the current actor.",
                [new("membership", "current", "not_active")], evaluatedAtUtc);
        }

        return EvaluateMembership(request, FinanceAgentActorTypes.Human, actorUserId, membership,
            requirements, evaluatedAtUtc, null, []);
    }

    private async Task<FinanceAgentAuthorizationDecisionDto> AuthorizeHumanAsync(
        FinanceAgentAuthorizationRequest request,
        Guid actorUserId,
        FinancePermissionRequirements requirements,
        DateTime evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var membership = await LoadMembershipAsync(request.CompanyId, actorUserId, cancellationToken);
        if (membership is null)
        {
            return Deny(request, FinanceAgentActorTypes.Human, actorUserId, FinanceAgentMembershipStates.Missing,
                requirements, FinanceAgentAuthorizationReasonCodes.MembershipMissing,
                "The Finance action is not available for the current actor.",
                [new("membership", "persisted", "not_active")], evaluatedAtUtc);
        }

        return EvaluateMembership(request, FinanceAgentActorTypes.Human, actorUserId, membership,
            requirements, evaluatedAtUtc, null, []);
    }

    private async Task<FinanceAgentAuthorizationDecisionDto> AuthorizeDelegationAsync(
        FinanceAgentAuthorizationRequest request,
        FinancePermissionRequirements requirements,
        DateTime evaluatedAtUtc,
        CancellationToken cancellationToken)
    {
        var authority = await _dbContext.FinanceAgentDelegationAuthorities.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.DelegationAuthorityId && x.CompanyId == request.CompanyId,
                cancellationToken);

        if (authority is null)
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationMissing,
                "The delegated Finance authority is unavailable.", evaluatedAtUtc, null);
        }

        if (authority.AgentId != request.AgentId)
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationAgentMismatch,
                "The delegated Finance authority is unavailable.", evaluatedAtUtc, authority);
        }

        if (authority.RevokedUtc.HasValue)
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationRevoked,
                "The delegated Finance authority is no longer active.", evaluatedAtUtc, authority);
        }

        if (authority.ExpiresUtc <= evaluatedAtUtc)
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationExpired,
                "The delegated Finance authority has expired.", evaluatedAtUtc, authority);
        }

        if (!request.WorkflowInstanceId.HasValue ||
            authority.OriginatingWorkflowInstanceId != request.WorkflowInstanceId.Value)
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationWorkflowMismatch,
                "The delegated Finance authority does not apply to this workflow.", evaluatedAtUtc, authority);
        }

        if (!string.Equals(authority.Capability, "finance", StringComparison.OrdinalIgnoreCase))
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationCapabilityMismatch,
                "The delegated authority does not include Finance.", evaluatedAtUtc, authority);
        }

        var action = request.ActionType.ToStorageValue();
        if (!authority.AllowedActionClasses.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationActionMismatch,
                "The delegated authority does not include this action class.", evaluatedAtUtc, authority);
        }

        if (authority.AllowedScopes.Count > 0 &&
            (string.IsNullOrWhiteSpace(request.Scope) ||
             !authority.AllowedScopes.Contains(request.Scope.Trim(), StringComparer.OrdinalIgnoreCase)))
        {
            return DenyDelegation(request, requirements, FinanceAgentAuthorizationReasonCodes.DelegationScopeMismatch,
                "The delegated authority does not include this data scope.", evaluatedAtUtc, authority);
        }

        var membership = await LoadMembershipAsync(request.CompanyId, authority.DelegatedActorUserId, cancellationToken);
        if (membership is null)
        {
            return Deny(request, FinanceAgentActorTypes.DelegatedBackground, authority.DelegatedActorUserId,
                FinanceAgentMembershipStates.Missing, requirements,
                FinanceAgentAuthorizationReasonCodes.MembershipMissing,
                "The delegated Finance actor is no longer authorized.",
                DelegationEvidence(authority, "validated"), evaluatedAtUtc, authority.Id,
                authority.OriginatingWorkflowInstanceId);
        }

        return EvaluateMembership(request, FinanceAgentActorTypes.DelegatedBackground,
            authority.DelegatedActorUserId, membership, requirements, evaluatedAtUtc, authority,
            DelegationEvidence(authority, "validated"));
    }

    private FinanceAgentAuthorizationDecisionDto EvaluateMembership(
        FinanceAgentAuthorizationRequest request,
        string actorType,
        Guid actorUserId,
        ResolvedCompanyMembershipContext membership,
        FinancePermissionRequirements requirements,
        DateTime evaluatedAtUtc,
        FinanceAgentDelegationAuthority? authority,
        IReadOnlyList<FinanceAgentAuthorizationEvidenceDto> additionalEvidence)
    {
        var role = membership.MembershipRole.ToStorageValue();
        var missing = requirements.Permissions.Where(permission => !HasPermission(role, permission)).ToArray();
        var evidence = additionalEvidence.Concat(
        [
            new FinanceAgentAuthorizationEvidenceDto("membership", membership.MembershipId.ToString("N"), "active"),
            new FinanceAgentAuthorizationEvidenceDto("membership_role", role, missing.Length == 0 ? "satisfies_requirements" : "insufficient")
        ]).ToArray();

        if (missing.Length > 0)
        {
            return Deny(request, actorType, actorUserId, FinanceAgentMembershipStates.Active, requirements,
                FinanceAgentAuthorizationReasonCodes.PermissionMissing,
                "The Finance action is not available for the current actor.", evidence, evaluatedAtUtc,
                authority?.Id, authority?.OriginatingWorkflowInstanceId);
        }

        return new FinanceAgentAuthorizationDecisionDto(
            request.CompanyId, request.AgentId, request.ExecutionId, actorType, actorUserId,
            FinanceAgentMembershipStates.Active, request.ToolName, request.ActionType.ToStorageValue(),
            NormalizeScope(request.Scope), requirements.Policies, requirements.Permissions,
            FinanceAgentAuthorizationOutcomes.Allowed, FinanceAgentAuthorizationReasonCodes.Authorized,
            "The actor has the Finance permissions required for this action.", evidence, evaluatedAtUtc,
            PolicyVersion, authority?.Id, authority?.OriginatingWorkflowInstanceId);
    }

    private async Task<ResolvedCompanyMembershipContext?> LoadMembershipAsync(
        Guid companyId,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        await _dbContext.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UserId == actorUserId && x.Status == CompanyMembershipStatus.Active)
            .Select(x => new ResolvedCompanyMembershipContext(x.Id, x.CompanyId, x.UserId!.Value,
                x.Company.Name, x.Role, x.Status, x.Company.Timezone, x.Company.Currency))
            .SingleOrDefaultAsync(cancellationToken);

    public static FinancePermissionRequirements ResolveRequirements(string toolName, ToolActionType actionType)
    {
        if (actionType is ToolActionType.Read or ToolActionType.Recommend)
        {
            return new([CompanyPolicies.FinanceView], [FinancePermissions.View]);
        }

        var policies = new List<string> { CompanyPolicies.FinanceEdit };
        var permissions = new List<string> { FinancePermissions.Edit };

        if (string.Equals(toolName, "approve_invoice", StringComparison.OrdinalIgnoreCase))
        {
            policies.Add(CompanyPolicies.FinanceApproval);
            permissions.Add(FinancePermissions.Approve);
        }
        else if (string.Equals(toolName, "post_paid_supplier_bill_expense", StringComparison.OrdinalIgnoreCase))
        {
            policies.Add(CompanyPolicies.AccountingAdmin);
            permissions.Add(FinancePermissions.AccountingAdmin);
        }
        else if (toolName.StartsWith("finance.migration.", StringComparison.OrdinalIgnoreCase))
        {
            policies.Add(CompanyPolicies.FinanceIntegrationAdmin);
            permissions.Add(FinancePermissions.ManageIntegrations);
        }

        return new(policies, permissions);
    }

    private static bool HasPermission(string role, string permission) => permission switch
    {
        FinancePermissions.View => FinanceAccess.CanView(role),
        FinancePermissions.Edit => FinanceAccess.CanEdit(role),
        FinancePermissions.Approve => FinanceAccess.CanApproveInvoices(role),
        FinancePermissions.AccountingAdmin => FinanceAccess.CanManageAccounting(role),
        FinancePermissions.ManageIntegrations => FinanceAccess.CanManageFinanceIntegrations(role),
        _ => false
    };

    private static FinanceAgentAuthorizationDecisionDto DenyDelegation(
        FinanceAgentAuthorizationRequest request,
        FinancePermissionRequirements requirements,
        string reasonCode,
        string explanation,
        DateTime evaluatedAtUtc,
        FinanceAgentDelegationAuthority? authority) =>
        Deny(request, FinanceAgentActorTypes.DelegatedBackground, authority?.DelegatedActorUserId,
            FinanceAgentMembershipStates.NotApplicable, requirements, reasonCode, explanation,
            authority is null ? [] : DelegationEvidence(authority, "rejected"), evaluatedAtUtc,
            request.DelegationAuthorityId, authority?.OriginatingWorkflowInstanceId);

    private static FinanceAgentAuthorizationDecisionDto Deny(
        FinanceAgentAuthorizationRequest request,
        string actorType,
        Guid? actorId,
        string membershipState,
        FinancePermissionRequirements requirements,
        string reasonCode,
        string explanation,
        IReadOnlyList<FinanceAgentAuthorizationEvidenceDto> evidence,
        DateTime evaluatedAtUtc,
        Guid? delegationAuthorityId = null,
        Guid? originatingWorkflowInstanceId = null) =>
        new(request.CompanyId, request.AgentId, request.ExecutionId, actorType, actorId, membershipState,
            request.ToolName, request.ActionType.ToStorageValue(), NormalizeScope(request.Scope),
            requirements.Policies, requirements.Permissions, FinanceAgentAuthorizationOutcomes.Denied,
            reasonCode, explanation, evidence, evaluatedAtUtc, PolicyVersion,
            delegationAuthorityId, originatingWorkflowInstanceId);

    private static IReadOnlyList<FinanceAgentAuthorizationEvidenceDto> DelegationEvidence(
        FinanceAgentDelegationAuthority authority,
        string result) =>
        [
            new("delegation", authority.Id.ToString("N"), result),
            new("delegation_issuer", authority.IssuedByUserId.ToString("N"), "persisted"),
            new("originating_workflow", authority.OriginatingWorkflowInstanceId.ToString("N"), "bound")
        ];

    private static string? NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();

    public sealed record FinancePermissionRequirements(
        IReadOnlyList<string> Policies,
        IReadOnlyList<string> Permissions);
}
