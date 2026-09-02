using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAutonomyControl : ICompanyOwnedEntity
{
    private FinanceAutonomyControl() { }

    public FinanceAutonomyControl(Guid companyId, FinanceAutonomyControlScope scope, Guid? agentId, string? capabilityId, DateTime nowUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId;
        Scope = scope;
        AgentId = agentId;
        CapabilityId = string.IsNullOrWhiteSpace(capabilityId) ? null : capabilityId.Trim().ToLowerInvariant();
        ScopeKey = CreateScopeKey(scope, agentId, CapabilityId);
        State = FinanceAutonomyControlState.Active;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public FinanceAutonomyControlScope Scope { get; private set; }
    public string ScopeKey { get; private set; } = string.Empty;
    public Guid? AgentId { get; private set; }
    public string? CapabilityId { get; private set; }
    public FinanceAutonomyControlState State { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public int Version { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Change(FinanceAutonomyControlState state, string reason, Guid actorUserId, DateTime nowUtc, int expectedVersion)
    {
        if (expectedVersion > 0 && expectedVersion != Version) throw new InvalidOperationException("The Finance autonomy control changed. Refresh and retry.");
        State = state;
        Reason = reason.Trim();
        ChangedByUserId = actorUserId;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version++;
    }

    public void ChangeBySystem(FinanceAutonomyControlState state, string reason, DateTime nowUtc)
    {
        State = state;
        Reason = reason.Trim();
        ChangedByUserId = null;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version++;
    }

    public static string CreateScopeKey(FinanceAutonomyControlScope scope, Guid? agentId, string? capabilityId) => scope switch
    {
        FinanceAutonomyControlScope.Company when agentId is null && string.IsNullOrWhiteSpace(capabilityId) => "company",
        FinanceAutonomyControlScope.Agent when agentId.HasValue && string.IsNullOrWhiteSpace(capabilityId) => $"agent:{agentId.Value:N}",
        FinanceAutonomyControlScope.Capability when agentId is null && !string.IsNullOrWhiteSpace(capabilityId) => $"capability:{capabilityId.Trim().ToLowerInvariant()}",
        _ => throw new ArgumentException("The Finance autonomy control target does not match its scope.")
    };
}
