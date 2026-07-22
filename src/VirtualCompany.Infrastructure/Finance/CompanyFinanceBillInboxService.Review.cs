using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Finance;


public sealed partial class CompanyFinanceBillInboxService
{
    public async Task<FinanceBillReviewActionResultDto> ApproveAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "approve",
            command.CompanyId,
            command.BillId,
            () => ApproveOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> ApproveOnceAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "approve",
            FinanceBillInboxStatuses.Approved,
            "finance.bill_inbox.approved",
            AuditEventOutcomes.Approved,
            addApprovalProposal: true,
            blockOnValidationFailures: true,
            cancellationToken);
    }

    public async Task<FinanceBillReviewActionResultDto> RejectAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "reject",
            command.CompanyId,
            command.BillId,
            () => RejectOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> RejectOnceAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "reject",
            FinanceBillInboxStatuses.Rejected,
            "finance.bill_inbox.rejected",
            AuditEventOutcomes.Rejected,
            addApprovalProposal: false,
            blockOnValidationFailures: false,
            cancellationToken);
    }

    public async Task<FinanceBillReviewActionResultDto> RequestClarificationAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewActionWithConcurrencyRetryAsync(
            "request clarification",
            command.CompanyId,
            command.BillId,
            () => RequestClarificationOnceAsync(command, cancellationToken),
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> RequestClarificationOnceAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken)
    {
        return await ExecuteReviewTransitionOnceAsync(
            command.CompanyId,
            command.BillId,
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            "clarification_requested",
            FinanceBillInboxStatuses.NeedsReview,
            "finance.bill_inbox.clarification_requested",
            AuditEventOutcomes.Requested,
            addApprovalProposal: false,
            blockOnValidationFailures: false,
            cancellationToken);
    }

    private async Task<FinanceBillReviewActionResultDto> ExecuteReviewTransitionOnceAsync(
        Guid companyId,
        Guid billId,
        Guid? actorUserId,
        string actorDisplayName,
        string rationale,
        string actionName,
        string newStatus,
        string auditActionName,
        string auditOutcome,
        bool addApprovalProposal,
        bool blockOnValidationFailures,
        CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var bill = await LoadBillForReviewActionAsync(companyId, billId, cancellationToken);
        var validationWarnings = ParseValidationWarnings(bill);
        if (blockOnValidationFailures && HasUnresolvedValidationFailures(bill, validationWarnings))
        {
            throw new InvalidOperationException("Finance bill approval is blocked while validation failures are unresolved.");
        }

        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DetectedBillId == billId, cancellationToken);
        var priorStatus = FinanceBillInboxStatuses.Normalize(ResolveInboxStatus(bill, state));
        EnsureActiveReviewStatus(priorStatus, actionName);

        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var reviewStateId = state?.Id ?? Guid.NewGuid();
        var proposalSummary = BuildProposalSummary(bill, validationWarnings, BuildDuplicateWarnings(bill), state?.ProposalSummary).Summary;

        if (state is null)
        {
            _dbContext.FinanceBillReviewStates.Add(new FinanceBillReviewState(
                reviewStateId,
                companyId,
                billId,
                newStatus,
                proposalSummary,
                occurredUtc,
                occurredUtc));
        }
        else
        {
            var updatedRows = await _dbContext.FinanceBillReviewStates
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Id == reviewStateId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, newStatus)
                        .SetProperty(x => x.UpdatedUtc, occurredUtc),
                    cancellationToken);
            if (updatedRows == 0)
            {
                throw new DbUpdateConcurrencyException("The finance bill review state changed before the review action could be saved.");
            }
        }

        var action = new FinanceBillReviewAction(
            Guid.NewGuid(),
            companyId,
            reviewStateId,
            billId,
            actionName,
            actorUserId,
            actorDisplayName,
            priorStatus,
            newStatus,
            rationale,
            occurredUtc);
        _dbContext.FinanceBillReviewActions.Add(action);

        if (addApprovalProposal)
        {
            var existingProposal = await _dbContext.BillApprovalProposals
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.DetectedBillId == billId, cancellationToken);
            if (!existingProposal)
            {
                _dbContext.BillApprovalProposals.Add(new BillApprovalProposal(
                    Guid.NewGuid(),
                    companyId,
                    billId,
                    reviewStateId,
                    proposalSummary,
                    actorUserId,
                    occurredUtc));
            }
        }

        await WriteAuditAsync(companyId, actorUserId, auditActionName, billId, auditOutcome, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(billId, FormatStatus(priorStatus), FormatStatus(newStatus), occurredUtc);
    }

    private async Task<T> ExecuteReviewActionWithConcurrencyRetryAsync<T>(
        string actionName,
        Guid companyId,
        Guid billId,
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 2;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < maxAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "Bill inbox review action hit a concurrency conflict; retrying with a fresh change tracker. Action: {ActionName}. CompanyId: {CompanyId}. BillId: {BillId}. Attempt: {Attempt}.",
                    actionName,
                    companyId,
                    billId,
                    attempt);
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Review action retry loop exited unexpectedly.");
    }

}

