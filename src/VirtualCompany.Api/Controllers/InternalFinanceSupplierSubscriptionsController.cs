using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Api.ProblemHandling;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("internal/companies/{companyId:guid}/finance")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class InternalFinanceSupplierSubscriptionsController : InternalFinanceControllerBase
{
    private readonly ISupplierSubscriptionIntakeProposalService _supplierSubscriptionIntakeProposalService;
    private readonly ISupplierSubscriptionService _supplierSubscriptionService;

    public InternalFinanceSupplierSubscriptionsController(
        ISupplierSubscriptionIntakeProposalService supplierSubscriptionIntakeProposalService,
        ISupplierSubscriptionService supplierSubscriptionService,
        FinanceInitializationProblemHandler initializationProblems,
        ILogger<InternalFinanceSupplierSubscriptionsController> logger)
        : base(initializationProblems, logger)
    {
        _supplierSubscriptionIntakeProposalService = supplierSubscriptionIntakeProposalService;
        _supplierSubscriptionService = supplierSubscriptionService;
    }
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
