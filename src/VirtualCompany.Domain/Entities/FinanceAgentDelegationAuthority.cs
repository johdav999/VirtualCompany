using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAgentDelegationAuthority : ICompanyOwnedEntity
{
    private FinanceAgentDelegationAuthority()
    {
    }

    public FinanceAgentDelegationAuthority(
        Guid id,
        Guid companyId,
        Guid agentId,
        Guid delegatedActorUserId,
        Guid issuedByUserId,
        Guid originatingWorkflowInstanceId,
        string capability,
        IEnumerable<ToolActionType> allowedActionClasses,
        IEnumerable<string> allowedScopes,
        DateTime issuedUtc,
        DateTime expiresUtc)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || delegatedActorUserId == Guid.Empty ||
            issuedByUserId == Guid.Empty || originatingWorkflowInstanceId == Guid.Empty)
        {
            throw new ArgumentException("Company, agent, actor, issuer, and workflow identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(capability))
        {
            throw new ArgumentException("Capability is required.", nameof(capability));
        }

        if (expiresUtc <= issuedUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresUtc), "Delegation expiry must follow issuance.");
        }

        var actions = allowedActionClasses.Distinct().ToArray();
        if (actions.Length == 0)
        {
            throw new ArgumentException("At least one action class is required.", nameof(allowedActionClasses));
        }

        foreach (var action in actions)
        {
            ToolActionTypeValues.EnsureSupported(action, nameof(allowedActionClasses));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        DelegatedActorUserId = delegatedActorUserId;
        IssuedByUserId = issuedByUserId;
        OriginatingWorkflowInstanceId = originatingWorkflowInstanceId;
        Capability = capability.Trim().ToLowerInvariant();
        AllowedActionClasses = actions.Select(static action => action.ToStorageValue()).ToList();
        AllowedScopes = allowedScopes.Where(static scope => !string.IsNullOrWhiteSpace(scope))
            .Select(static scope => scope.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        IssuedUtc = DateTime.SpecifyKind(issuedUtc, DateTimeKind.Utc);
        ExpiresUtc = DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc);
        CreatedUtc = IssuedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid DelegatedActorUserId { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public Guid OriginatingWorkflowInstanceId { get; private set; }
    public string Capability { get; private set; } = string.Empty;
    public List<string> AllowedActionClasses { get; private set; } = [];
    public List<string> AllowedScopes { get; private set; } = [];
    public DateTime IssuedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? RevocationReason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Revoke(Guid revokedByUserId, string reason, DateTime revokedUtc)
    {
        if (revokedByUserId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Revoking user and reason are required.");
        }

        RevokedByUserId = revokedByUserId;
        RevocationReason = reason.Trim();
        RevokedUtc = DateTime.SpecifyKind(revokedUtc, DateTimeKind.Utc);
    }
}
