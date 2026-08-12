using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Controllers;

public sealed partial class InternalFinanceController
{
    [HttpGet("supplier-subscriptions")]
    public async Task<ActionResult<IReadOnlyList<SupplierSubscriptionSummaryDto>>> GetSupplierSubscriptionsAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null) =>
        await ExecuteReadAsync(
            () => _supplierSubscriptionService.GetAsync(
                new GetSupplierSubscriptionsQuery(companyId, status, search),
                cancellationToken));

    [HttpGet("supplier-subscriptions/{subscriptionId:guid}")]
    public async Task<ActionResult<SupplierSubscriptionDetailDto>> GetSupplierSubscriptionAsync(
        Guid companyId,
        Guid subscriptionId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _supplierSubscriptionService.GetAsync(
                new GetSupplierSubscriptionQuery(companyId, subscriptionId),
                cancellationToken),
            "Supplier subscription was not found.");

    [HttpPost("supplier-subscriptions")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionDetailDto>> CreateSupplierSubscriptionAsync(
        Guid companyId,
        [FromBody] UpsertSupplierSubscriptionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.CreateAsync(
                request.ToCreateCommand(companyId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpPut("supplier-subscriptions/{subscriptionId:guid}")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionDetailDto>> UpdateSupplierSubscriptionAsync(
        Guid companyId,
        Guid subscriptionId,
        [FromBody] UpsertSupplierSubscriptionRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.UpdateAsync(
                request.ToUpdateCommand(companyId, subscriptionId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpPost("supplier-subscriptions/{subscriptionId:guid}/status")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionDetailDto>> ChangeSupplierSubscriptionStatusAsync(
        Guid companyId,
        Guid subscriptionId,
        [FromBody] SupplierSubscriptionStatusRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.ChangeStatusAsync(
                new ChangeSupplierSubscriptionStatusCommand(companyId, subscriptionId, request.Action, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpGet("bills/{billId:guid}/subscription-context")]
    public async Task<ActionResult<SupplierBillSubscriptionContextDto>> GetSupplierBillSubscriptionContextAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken) =>
        await ExecuteReadAsync(
            () => _supplierSubscriptionService.GetBillContextAsync(
                new GetSupplierBillSubscriptionContextQuery(companyId, billId),
                cancellationToken));

    [HttpPost("bills/{billId:guid}/subscription-evaluation")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillSubscriptionContextDto>> EvaluateSupplierBillSubscriptionAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.EvaluateBillAsync(
                new EvaluateSupplierSubscriptionBillCommand(companyId, billId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpPost("supplier-subscription-matches/{matchId:guid}/confirm")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillSubscriptionContextDto>> ConfirmSupplierSubscriptionMatchAsync(
        Guid companyId,
        Guid matchId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.DecideMatchAsync(
                new DecideSupplierSubscriptionMatchCommand(companyId, matchId, true, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpPost("supplier-subscription-matches/{matchId:guid}/reject")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillSubscriptionContextDto>> RejectSupplierSubscriptionMatchAsync(
        Guid companyId,
        Guid matchId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.DecideMatchAsync(
                new DecideSupplierSubscriptionMatchCommand(companyId, matchId, false, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    [HttpPost("supplier-subscriptions/{subscriptionId:guid}/receipt-evidence")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierBillSubscriptionContextDto>> LinkSupplierSubscriptionReceiptEvidenceAsync(
        Guid companyId,
        Guid subscriptionId,
        [FromBody] LinkSupplierSubscriptionReceiptEvidenceRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionService.LinkReceiptEvidenceAsync(
                new LinkSupplierSubscriptionReceiptEvidenceCommand(companyId, subscriptionId, request.BillId, request.EvidenceSummary ?? string.Empty, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
    [HttpGet("supplier-subscription-proposals")]
    public async Task<ActionResult<IReadOnlyList<SupplierSubscriptionIntakeProposalSummaryDto>>> GetSupplierSubscriptionProposalsAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null) =>
        await ExecuteReadAsync(
            () => _supplierSubscriptionIntakeProposalService.GetAsync(
                new GetSupplierSubscriptionIntakeProposalsQuery(companyId, status, search),
                cancellationToken));

    [HttpGet("supplier-subscription-proposals/{proposalId:guid}")]
    public async Task<ActionResult<SupplierSubscriptionIntakeProposalDetailDto>> GetSupplierSubscriptionProposalAsync(
        Guid companyId,
        Guid proposalId,
        CancellationToken cancellationToken) =>
        await ExecuteReadOptionalAsync(
            () => _supplierSubscriptionIntakeProposalService.GetAsync(
                new GetSupplierSubscriptionIntakeProposalQuery(companyId, proposalId),
                cancellationToken),
            "Supplier subscription proposal was not found.");

    [HttpPost("supplier-subscription-proposals/{proposalId:guid}/accept")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionDetailDto>> AcceptSupplierSubscriptionProposalAsync(
        Guid companyId,
        Guid proposalId,
        [FromBody] AcceptSupplierSubscriptionProposalRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionIntakeProposalService.AcceptAsync(
                new AcceptSupplierSubscriptionIntakeProposalCommand(companyId, proposalId, request.Terms, ResolveActorId(), ResolveActorDisplayName(), request.DecisionReason),
                cancellationToken));

    [HttpPost("supplier-subscription-proposals/{proposalId:guid}/reject")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionIntakeProposalDetailDto>> RejectSupplierSubscriptionProposalAsync(
        Guid companyId,
        Guid proposalId,
        [FromBody] RejectSupplierSubscriptionProposalRequest request,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionIntakeProposalService.RejectAsync(
                new RejectSupplierSubscriptionIntakeProposalCommand(companyId, proposalId, request.Reason, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));

    [HttpPost("supplier-subscription-proposals/{proposalId:guid}/retry")]
    [Authorize(Policy = CompanyPolicies.FinanceApproval)]
    public async Task<ActionResult<SupplierSubscriptionIntakeProposalDetailDto>> RetrySupplierSubscriptionProposalAsync(
        Guid companyId,
        Guid proposalId,
        CancellationToken cancellationToken) =>
        await ExecuteWriteAsync(
            () => _supplierSubscriptionIntakeProposalService.RetryAsync(
                new RetrySupplierSubscriptionIntakeProposalCommand(companyId, proposalId, ResolveActorId(), ResolveActorDisplayName()),
                cancellationToken));
}

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
    Guid? ContractDocumentId)
{
    public CreateSupplierSubscriptionCommand ToCreateCommand(Guid companyId, Guid? actorUserId, string actorDisplayName) =>
        new(companyId, CounterpartyId, Name, Currency, ExpectedAmount, Cadence, BillingDay, StartDateUtc, NextExpectedBillDateUtc, AmountTolerance, DateToleranceDays, EndDateUtc, ContractReference, Description, NoticePeriodDays, AutoRenews, ContractDocumentId, actorUserId, actorDisplayName);

    public UpdateSupplierSubscriptionCommand ToUpdateCommand(Guid companyId, Guid subscriptionId, Guid? actorUserId, string actorDisplayName) =>
        new(companyId, subscriptionId, CounterpartyId, Name, Currency, ExpectedAmount, Cadence, BillingDay, StartDateUtc, NextExpectedBillDateUtc, AmountTolerance, DateToleranceDays, EndDateUtc, ContractReference, Description, NoticePeriodDays, AutoRenews, ContractDocumentId, actorUserId, actorDisplayName);
}

public sealed record SupplierSubscriptionStatusRequest(string Action);
public sealed record LinkSupplierSubscriptionReceiptEvidenceRequest(Guid BillId, string? EvidenceSummary);



public sealed record AcceptSupplierSubscriptionProposalRequest(SupplierSubscriptionProposalTermsDto Terms, string? DecisionReason);
public sealed record RejectSupplierSubscriptionProposalRequest(string Reason);

