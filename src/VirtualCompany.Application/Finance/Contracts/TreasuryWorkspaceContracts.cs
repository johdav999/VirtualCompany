namespace VirtualCompany.Application.Finance;

public static class TreasuryWorkspaceSeverity
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";
    public const string Info = "info";
}

public sealed record GetTreasuryWorkspaceQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int HorizonDays = 14,
    int ExceptionLimit = 12,
    int TaskLimit = 8,
    bool CanEdit = false,
    bool CanApprove = false);

public sealed record TreasuryWorkspacePolicyInput(
    bool CanEdit,
    bool CanApprove,
    string? ConnectionStatus = null,
    string? ConnectionReasonCode = null,
    bool HasOpenGap = false,
    string? ReconciliationStatus = null,
    string? PaymentStatus = null,
    bool PaymentCanCancel = false,
    string? LiquidityRisk = null);

public interface ITreasuryWorkspacePolicy
{
    IReadOnlyList<TreasuryWorkspaceActionDecisionDto> Evaluate(TreasuryWorkspacePolicyInput input);
}

public interface ITreasuryWorkspaceQueryService
{
    Task<TreasuryWorkspaceDto> GetAsync(
        GetTreasuryWorkspaceQuery query,
        CancellationToken cancellationToken);
}
