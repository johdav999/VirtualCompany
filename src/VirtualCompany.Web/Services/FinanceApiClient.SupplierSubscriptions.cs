using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<IReadOnlyList<SupplierSubscriptionSummaryResponse>> GetSupplierSubscriptionsAsync(
        Guid companyId,
        string? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var uri = $"internal/companies/{companyId}/finance/supplier-subscriptions{BuildQuery(("status", status), ("search", search))}";
        return GetListAsync<SupplierSubscriptionSummaryResponse>(companyId, uri, cancellationToken);
    }

    public Task<SupplierSubscriptionDetailResponse?> GetSupplierSubscriptionAsync(Guid companyId, Guid subscriptionId, CancellationToken cancellationToken = default) =>
        subscriptionId == Guid.Empty
            ? Task.FromResult<SupplierSubscriptionDetailResponse?>(null)
            : GetAsync<SupplierSubscriptionDetailResponse>(companyId, $"internal/companies/{companyId}/finance/supplier-subscriptions/{subscriptionId}", allowNotFound: true, cancellationToken);

    public Task<SupplierSubscriptionDetailResponse> CreateSupplierSubscriptionAsync(Guid companyId, UpsertSupplierSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpsertSupplierSubscriptionRequest, SupplierSubscriptionDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscriptions", request, cancellationToken);
    }

    public Task<SupplierSubscriptionDetailResponse> UpdateSupplierSubscriptionAsync(Guid companyId, Guid subscriptionId, UpsertSupplierSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpsertSupplierSubscriptionRequest, SupplierSubscriptionDetailResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/supplier-subscriptions/{subscriptionId}", request, cancellationToken);
    }

    public Task<SupplierSubscriptionDetailResponse> ChangeSupplierSubscriptionStatusAsync(Guid companyId, Guid subscriptionId, string action, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SupplierSubscriptionStatusRequest, SupplierSubscriptionDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscriptions/{subscriptionId}/status", new SupplierSubscriptionStatusRequest(action), cancellationToken);
    }

    public Task<SupplierBillSubscriptionContextResponse> GetSupplierBillSubscriptionContextAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        SendCompanyScopedGetAsync<SupplierBillSubscriptionContextResponse>(companyId, $"internal/companies/{companyId}/finance/bills/{billId}/subscription-context", allowNotFound: false, cancellationToken)!;

    public Task<SupplierBillSubscriptionContextResponse> EvaluateSupplierBillSubscriptionAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, SupplierBillSubscriptionContextResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/bills/{billId}/subscription-evaluation", new { }, cancellationToken);
    }

    public Task<SupplierBillSubscriptionContextResponse> ConfirmSupplierSubscriptionMatchAsync(Guid companyId, Guid matchId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, SupplierBillSubscriptionContextResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscription-matches/{matchId}/confirm", new { }, cancellationToken);
    }

    public Task<SupplierBillSubscriptionContextResponse> RejectSupplierSubscriptionMatchAsync(Guid companyId, Guid matchId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, SupplierBillSubscriptionContextResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscription-matches/{matchId}/reject", new { }, cancellationToken);
    }
    public Task<SupplierBillSubscriptionContextResponse> LinkSupplierSubscriptionReceiptEvidenceAsync(Guid companyId, Guid subscriptionId, LinkSupplierSubscriptionReceiptEvidenceRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<LinkSupplierSubscriptionReceiptEvidenceRequest, SupplierBillSubscriptionContextResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscriptions/{subscriptionId}/receipt-evidence", request, cancellationToken);
    }


    public Task<IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryResponse>> GetSupplierSubscriptionProposalsAsync(
        Guid companyId,
        string? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var uri = $"internal/companies/{companyId}/finance/supplier-subscription-proposals{BuildQuery(("status", status), ("search", search))}";
        return GetListAsync<SupplierSubscriptionIntakeProposalSummaryResponse>(companyId, uri, cancellationToken);
    }

    public Task<SupplierSubscriptionIntakeProposalDetailResponse?> GetSupplierSubscriptionProposalAsync(Guid companyId, Guid proposalId, CancellationToken cancellationToken = default) =>
        proposalId == Guid.Empty
            ? Task.FromResult<SupplierSubscriptionIntakeProposalDetailResponse?>(null)
            : GetAsync<SupplierSubscriptionIntakeProposalDetailResponse>(companyId, $"internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}", allowNotFound: true, cancellationToken);

    public Task<SupplierSubscriptionDetailResponse> AcceptSupplierSubscriptionProposalAsync(Guid companyId, Guid proposalId, AcceptSupplierSubscriptionProposalRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AcceptSupplierSubscriptionProposalRequest, SupplierSubscriptionDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/accept", request, cancellationToken);
    }

    public Task<SupplierSubscriptionIntakeProposalDetailResponse> RejectSupplierSubscriptionProposalAsync(Guid companyId, Guid proposalId, RejectSupplierSubscriptionProposalRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RejectSupplierSubscriptionProposalRequest, SupplierSubscriptionIntakeProposalDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/reject", request, cancellationToken);
    }

    public Task<SupplierSubscriptionIntakeProposalDetailResponse> RetrySupplierSubscriptionProposalAsync(Guid companyId, Guid proposalId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, SupplierSubscriptionIntakeProposalDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/supplier-subscription-proposals/{proposalId}/retry", new { }, cancellationToken);
    }
    public static string FormatSupplierSubscriptionAmount(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.00} {currency}");
}

public sealed record LinkSupplierSubscriptionReceiptEvidenceRequest(Guid BillId, string? EvidenceSummary);

public sealed record UpsertSupplierSubscriptionRequest(
    Guid CounterpartyId,
    string Name,
    string Currency,
    decimal ExpectedAmount,
    string Cadence,
    int BillingDay,
    DateTime StartDateUtc,
    DateTime NextExpectedBillDateUtc,
    decimal AmountTolerance,
    int DateToleranceDays,
    DateTime? EndDateUtc,
    string? ContractReference,
    string? Description,
    int NoticePeriodDays,
    bool AutoRenews,
    Guid? ContractDocumentId);

public sealed record SupplierSubscriptionStatusRequest(string Action);

public sealed record SupplierSubscriptionSummaryResponse(
    Guid Id,
    Guid CounterpartyId,
    string SupplierName,
    string Name,
    string Currency,
    decimal ExpectedAmount,
    string Cadence,
    string Status,
    string Health,
    string HealthMessage,
    DateTime NextExpectedBillDateUtc,
    DateTime? EndDateUtc,
    DateTime? LastMatchedBillUtc,
    int MatchCount,
    int ReviewCount);

public sealed record SupplierSubscriptionMatchResponse(
    Guid Id,
    Guid SubscriptionId,
    Guid BillId,
    string BillNumber,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateTime ExpectedBillDateUtc,
    decimal ExpectedAmount,
    decimal ActualAmount,
    decimal AmountVariance,
    string Currency,
    string Status,
    string MatchMethod,
    int ConfidenceScore,
    string EvidenceSummary,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    DateTime CreatedUtc);

public sealed record SupplierSubscriptionSourceEvidenceResponse(
    Guid ProposalId,
    string Status,
    string? SourceSubject,
    string? SourceAttachmentName,
    string EvidenceSummary,
    string? DecisionReason,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    DateTime CreatedUtc);
public sealed record SupplierSubscriptionDetailResponse(
    Guid Id,
    Guid CounterpartyId,
    string SupplierName,
    string Name,
    string? ContractReference,
    string? Description,
    string Currency,
    decimal ExpectedAmount,
    decimal AmountTolerance,
    string Cadence,
    int BillingDay,
    DateTime StartDateUtc,
    DateTime? EndDateUtc,
    DateTime NextExpectedBillDateUtc,
    int DateToleranceDays,
    int NoticePeriodDays,
    bool AutoRenews,
    string Status,
    string Health,
    string HealthMessage,
    Guid? ContractDocumentId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    SupplierSubscriptionSourceEvidenceResponse? SourceEvidence,
    IReadOnlyList<SupplierSubscriptionMatchResponse> Matches);

public sealed record SupplierBillSubscriptionContextResponse(
    Guid BillId,
    bool HasContext,
    SupplierSubscriptionSummaryResponse? Subscription,
    SupplierSubscriptionMatchResponse? Match,
    IReadOnlyList<SupplierSubscriptionMatchResponse> Suggestions,
    string Status,
    string Message);
public sealed record SupplierSubscriptionProposalTermsRequest(
    Guid? CounterpartyId,
    string? Name,
    string? Currency,
    decimal? ExpectedAmount,
    string? Cadence,
    int? BillingDay,
    DateTime? StartDateUtc,
    DateTime? NextExpectedBillDateUtc,
    decimal? AmountTolerance,
    int? DateToleranceDays,
    DateTime? EndDateUtc,
    string? ContractReference,
    string? Description,
    int? NoticePeriodDays,
    bool? AutoRenews,
    Guid? ContractDocumentId);

public sealed record AcceptSupplierSubscriptionProposalRequest(SupplierSubscriptionProposalTermsRequest Terms, string? DecisionReason);
public sealed record RejectSupplierSubscriptionProposalRequest(string Reason);

public sealed record SupplierSubscriptionIntakeProposalSummaryResponse(
    Guid Id,
    string Status,
    string Classification,
    string SupplierName,
    string AgreementName,
    string? Currency,
    decimal? ExpectedAmount,
    string? Cadence,
    int ConfidenceScore,
    string EvidenceSummary,
    Guid? AcceptedSubscriptionId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record SupplierSubscriptionIntakeProposalDetailResponse(
    Guid Id,
    string Status,
    string Classification,
    Guid SourceEmailMessageSnapshotId,
    Guid? SourceEmailAttachmentSnapshotId,
    Guid? SourceDocumentId,
    string SourceFingerprint,
    string? SourceSubject,
    string? SourceAttachmentName,
    string SupplierName,
    string? SupplierOrgNumber,
    SupplierSubscriptionProposalTermsRequest Terms,
    int ConfidenceScore,
    string EvidenceSummary,
    string? SafeFailureSummary,
    Guid? AcceptedSubscriptionId,
    Guid? DecidedByUserId,
    string? DecisionReason,
    DateTime? DecidedUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

