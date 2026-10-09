using VirtualCompany.Application.Finance;

namespace VirtualCompany.Shared.Contracts.FinanceIntegrationConnections;
public sealed record StartFinanceIntegrationConnectionRequest(string? ReturnUri, bool Reconnect = false);
public sealed record SyncFinanceIntegrationNowRequest(Guid? ConnectionId = null, bool FullSync = false);
public sealed record RepairFortnoxSupplierInvoicesRequest(Guid ConnectionId, IReadOnlyList<RepairFortnoxSupplierInvoiceRequest> Invoices);
public sealed record RepairFortnoxSupplierInvoiceRequest(string ExternalId, string SupplierNumber, string SupplierName, DateOnly InvoiceDate, DateOnly DueDate, decimal Total, decimal Balance, string Currency = "SEK", bool Cancelled = false, bool Booked = true, bool FullyPaid = false);
public sealed record CompleteFinanceIntegrationConnectionRequest(string? Code, string State, string? Nonce = null, string? ProviderError = null);
public sealed record StartFinanceIntegrationConnectionResponse(string AuthorizationUrl, DateTime ExpiresUtc)
{
    public string AuthorizationUrl { get; set; } = AuthorizationUrl;
    public DateTime ExpiresUtc { get; set; } = ExpiresUtc;

    public StartFinanceIntegrationConnectionResponse() : this(string.Empty, default !)
    {
    }
}

public sealed record FinanceIntegrationProviderMetadataResponse(string ProviderKey, string DisplayName, IReadOnlyCollection<string> Capabilities, FinanceIntegrationConnectionStatusResult Status)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public string DisplayName { get; set; } = DisplayName;
    public IReadOnlyCollection<string> Capabilities { get; set; } = Capabilities;
    public FinanceIntegrationConnectionStatusResult Status { get; set; } = Status;

    public FinanceIntegrationProviderMetadataResponse() : this(string.Empty, string.Empty, [], new())
    {
    }
}

public sealed record FinanceIntegrationWriteCommandRequest(Guid? ConnectionId, Guid? ActorUserId, string? CommandType, string HttpMethod, string Path, string TargetCompany, string PayloadSummary, string PayloadHash, string SanitizedPayloadJson, Guid WriteRequestId, string? ProviderPayloadType = null, string? CorrelationId = null, Guid? ApprovedApprovalId = null);
