namespace VirtualCompany.Application.Finance;

/// <summary>
/// The single, server-side decision that describes which finance facts are safe to read and act on.
/// It is derived from durable accounting configuration, authority periods, provider connections, and
/// the explicit company simulation state; it is never inferred from the records currently present.
/// </summary>
public sealed record FinanceOperatingModeDecisionDto(
    Guid CompanyId,
    DateOnly AsOfDate,
    string AccountingAuthority,
    Guid? AuthorityPeriodId,
    string? ProviderKey,
    bool AccountingSetupReady,
    bool MigrationInProgress,
    bool ProviderConnected,
    bool SimulationFeatureEnabled,
    bool SimulationActive,
    string AllowedReadSource,
    string AllowedPostingSource,
    bool IsReadyForOperationalPosting,
    string NextAction,
    IReadOnlyList<FinanceOperatingModeIssueDto> Issues);

public sealed record FinanceOperatingModeIssueDto(
    string Code,
    string Explanation,
    bool IsBlocking = true);

public sealed record GetFinanceOperatingModeQuery(Guid CompanyId, DateOnly? AsOfDate = null);

public interface IFinanceOperatingModeService
{
    Task<FinanceOperatingModeDecisionDto> GetAsync(
        GetFinanceOperatingModeQuery query,
        CancellationToken cancellationToken);
}
