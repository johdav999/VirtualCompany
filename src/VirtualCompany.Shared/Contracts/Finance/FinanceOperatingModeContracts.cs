namespace VirtualCompany.Application.Finance;
public sealed record FinanceOperatingModeIssueDto(string Code, string Explanation, bool IsBlocking = true)
{
    public string Code { get; set; } = Code;
    public string Explanation { get; set; } = Explanation;
    public bool IsBlocking { get; set; } = IsBlocking;

    public FinanceOperatingModeIssueDto() : this(string.Empty, string.Empty, default !)
    {
    }
}

/// <summary>
/// The single, server-side decision that describes which finance facts are safe to read and act on.
/// It is derived from durable accounting configuration, authority periods, provider connections, and
/// the explicit company simulation state; it is never inferred from the records currently present.
/// </summary>
public sealed record FinanceOperatingModeDecisionDto(Guid CompanyId, DateOnly AsOfDate, string AccountingAuthority, Guid? AuthorityPeriodId, string? ProviderKey, bool AccountingSetupReady, bool MigrationInProgress, bool ProviderConnected, bool SimulationFeatureEnabled, bool SimulationActive, string AllowedReadSource, string AllowedPostingSource, bool IsReadyForOperationalPosting, string NextAction, IReadOnlyList<FinanceOperatingModeIssueDto> Issues)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateOnly AsOfDate { get; set; } = AsOfDate;
    public string AccountingAuthority { get; set; } = AccountingAuthority;
    public Guid? AuthorityPeriodId { get; set; } = AuthorityPeriodId;
    public string? ProviderKey { get; set; } = ProviderKey;
    public bool AccountingSetupReady { get; set; } = AccountingSetupReady;
    public bool MigrationInProgress { get; set; } = MigrationInProgress;
    public bool ProviderConnected { get; set; } = ProviderConnected;
    public bool SimulationFeatureEnabled { get; set; } = SimulationFeatureEnabled;
    public bool SimulationActive { get; set; } = SimulationActive;
    public string AllowedReadSource { get; set; } = AllowedReadSource;
    public string AllowedPostingSource { get; set; } = AllowedPostingSource;
    public bool IsReadyForOperationalPosting { get; set; } = IsReadyForOperationalPosting;
    public string NextAction { get; set; } = NextAction;
    public IReadOnlyList<FinanceOperatingModeIssueDto> Issues { get; set; } = Issues;

    public FinanceOperatingModeDecisionDto() : this(default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, default !, string.Empty, [])
    {
    }
}
