namespace VirtualCompany.Application.Finance;
public sealed record FinanceIntegrationSyncResult(string ProviderKey, Guid CompanyId, Guid ConnectionId, DateTime StartedUtc, DateTime CompletedUtc, string Status, int Created, int Updated, int Skipped, int Errors, IReadOnlyList<FinanceIntegrationEntitySyncResult> Entities, string? ErrorSummary = null, int RetryAttempts = 0, string? RetryOutcome = null)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid ConnectionId { get; set; } = ConnectionId;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime CompletedUtc { get; set; } = CompletedUtc;
    public string Status { get; set; } = Status;
    public int Created { get; set; } = Created;
    public int Updated { get; set; } = Updated;
    public int Skipped { get; set; } = Skipped;
    public int Errors { get; set; } = Errors;
    public IReadOnlyList<FinanceIntegrationEntitySyncResult> Entities { get; set; } = Entities;
    public string? ErrorSummary { get; set; } = ErrorSummary;
    public int RetryAttempts { get; set; } = RetryAttempts;
    public string? RetryOutcome { get; set; } = RetryOutcome;

    public FinanceIntegrationSyncResult() : this(string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, [], default !, default !, default !)
    {
    }
}

public sealed record FinanceIntegrationSyncHistoryItem(Guid Id, Guid? ConnectionId, DateTime StartedUtc, DateTime? CompletedUtc, string Status, int Created, int Updated, int Skipped, int Errors, string Summary, string? ErrorSummary, int RetryAttempts = 0, string? RetryOutcome = null, IReadOnlyList<FinanceIntegrationEntitySyncResult>? Entities = null)
{
    public Guid Id { get; set; } = Id;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public DateTime StartedUtc { get; set; } = StartedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;
    public string Status { get; set; } = Status;
    public int Created { get; set; } = Created;
    public int Updated { get; set; } = Updated;
    public int Skipped { get; set; } = Skipped;
    public int Errors { get; set; } = Errors;
    public string Summary { get; set; } = Summary;
    public string? ErrorSummary { get; set; } = ErrorSummary;
    public int RetryAttempts { get; set; } = RetryAttempts;
    public string? RetryOutcome { get; set; } = RetryOutcome;
    public IReadOnlyList<FinanceIntegrationEntitySyncResult>? Entities { get; set; } = Entities;

    public FinanceIntegrationSyncHistoryItem() : this(default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, [])
    {
    }
}

public sealed record FinanceIntegrationEntitySyncResult(string EntityType, int Created, int Updated, int Skipped, int Errors, string? ErrorSummary = null)
{
    public string EntityType { get; set; } = EntityType;
    public int Created { get; set; } = Created;
    public int Updated { get; set; } = Updated;
    public int Skipped { get; set; } = Skipped;
    public int Errors { get; set; } = Errors;
    public string? ErrorSummary { get; set; } = ErrorSummary;

    public FinanceIntegrationEntitySyncResult() : this(string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceIntegrationSyncHistoryResult(string ProviderKey, Guid CompanyId, IReadOnlyList<FinanceIntegrationSyncHistoryItem> Items)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public Guid CompanyId { get; set; } = CompanyId;
    public IReadOnlyList<FinanceIntegrationSyncHistoryItem> Items { get; set; } = Items;

    public FinanceIntegrationSyncHistoryResult() : this(string.Empty, default !, [])
    {
    }
}

public sealed record FinanceIntegrationConnectionDisconnectResult(string ProviderKey, Guid CompanyId, Guid? ConnectionId, string Status, DateTime DisconnectedUtc, string Message)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public Guid CompanyId { get; set; } = CompanyId;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public string Status { get; set; } = Status;
    public DateTime DisconnectedUtc { get; set; } = DisconnectedUtc;
    public string Message { get; set; } = Message;

    public FinanceIntegrationConnectionDisconnectResult() : this(string.Empty, default !, default !, string.Empty, default !, string.Empty)
    {
    }
}

public sealed record FinanceIntegrationConnectionStatusResult(string ProviderKey, bool IsConnected, Guid? ConnectionId, string? ConnectionStatus, DateTime? ConnectedAtUtc, DateTime? AccessTokenExpiresUtc, DateTime? LastRefreshAttemptUtc, string? LastErrorSummary, DateTime? LastSuccessfulSyncUtc = null)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public bool IsConnected { get; set; } = IsConnected;
    public Guid? ConnectionId { get; set; } = ConnectionId;
    public string? ConnectionStatus { get; set; } = ConnectionStatus;
    public DateTime? ConnectedAtUtc { get; set; } = ConnectedAtUtc;
    public DateTime? AccessTokenExpiresUtc { get; set; } = AccessTokenExpiresUtc;
    public DateTime? LastRefreshAttemptUtc { get; set; } = LastRefreshAttemptUtc;
    public string? LastErrorSummary { get; set; } = LastErrorSummary;
    public DateTime? LastSuccessfulSyncUtc { get; set; } = LastSuccessfulSyncUtc;

    public FinanceIntegrationConnectionStatusResult() : this(string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}
