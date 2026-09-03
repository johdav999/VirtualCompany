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
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillForReviewActionAsync(command.CompanyId, command.BillId, cancellationToken);
        var validationWarnings = ParseValidationWarnings(bill);
        if (HasUnresolvedValidationFailures(bill, validationWarnings))
        {
            throw new InvalidOperationException("Finance bill approval is blocked while validation failures are unresolved.");
        }

        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == command.CompanyId && x.DetectedBillId == command.BillId,
                cancellationToken);
        var priorStatus = FinanceBillInboxStatuses.Normalize(ResolveInboxStatus(bill, state));

        if (string.Equals(priorStatus, FinanceBillInboxStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            var existingOperationalBillId = await PromoteDetectedBillAsync(bill, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new FinanceBillReviewActionResultDto(
                command.BillId,
                FormatStatus(priorStatus),
                FormatStatus(priorStatus),
                state?.UpdatedUtc ?? _timeProvider.GetUtcNow().UtcDateTime,
                existingOperationalBillId);
        }

        EnsureActiveReviewStatus(priorStatus, "approve");
        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var proposalSummary = BuildProposalSummary(
            bill,
            validationWarnings,
            BuildDuplicateWarnings(bill),
            state?.ProposalSummary).Summary;

        if (state is null)
        {
            state = new FinanceBillReviewState(
                Guid.NewGuid(),
                command.CompanyId,
                command.BillId,
                priorStatus,
                proposalSummary,
                occurredUtc,
                occurredUtc);
            _dbContext.FinanceBillReviewStates.Add(state);
        }

        var action = state.Approve(
            command.ActorUserId,
            command.ActorDisplayName,
            command.Rationale,
            occurredUtc,
            hasUnresolvedValidationFailures: false);
        _dbContext.FinanceBillReviewActions.Add(action);

        var existingProposal = await _dbContext.BillApprovalProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x => x.CompanyId == command.CompanyId && x.DetectedBillId == command.BillId,
                cancellationToken);
        if (!existingProposal)
        {
            _dbContext.BillApprovalProposals.Add(new BillApprovalProposal(
                Guid.NewGuid(),
                command.CompanyId,
                command.BillId,
                state.Id,
                proposalSummary,
                command.ActorUserId,
                occurredUtc));
        }

        var operationalBillId = await PromoteDetectedBillAsync(bill, cancellationToken);
        await WriteAuditAsync(
            command.CompanyId,
            command.ActorUserId,
            "finance.bill_inbox.approved",
            command.BillId,
            AuditEventOutcomes.Approved,
            action,
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(
            command.BillId,
            FormatStatus(priorStatus),
            FormatStatus(FinanceBillInboxStatuses.Approved),
            occurredUtc,
            operationalBillId);
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

        await WriteAuditAsync(companyId, actorUserId, auditActionName, billId, auditOutcome, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(billId, FormatStatus(priorStatus), FormatStatus(newStatus), occurredUtc);
    }

    private async Task<Guid> PromoteDetectedBillAsync(DetectedBill detectedBill, CancellationToken cancellationToken)
    {
        var billNumberCandidate = string.IsNullOrWhiteSpace(detectedBill.InvoiceNumber)
            ? detectedBill.SourceAttachmentId ?? detectedBill.Id.ToString("D")
            : detectedBill.InvoiceNumber.Trim();
        var billNumber = string.IsNullOrWhiteSpace(billNumberCandidate) || billNumberCandidate.Trim().Length > 64
            ? $"detected-{detectedBill.Id:N}"
            : billNumberCandidate.Trim();

        var operationalBill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == detectedBill.CompanyId && x.SourceDetectedBillId == detectedBill.Id,
                cancellationToken);

        operationalBill ??= await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == detectedBill.CompanyId && x.BillNumber == billNumber,
                cancellationToken);

        if (operationalBill is not null)
        {
            operationalBill.LinkDetectedBill(detectedBill.Id);
            operationalBill.ApplyBusinessApproval(_timeProvider.GetUtcNow().UtcDateTime);
            return operationalBill.Id;
        }

        var supplierName = string.IsNullOrWhiteSpace(detectedBill.SupplierName)
            ? "Unknown supplier"
            : detectedBill.SupplierName.Trim();
        var supplierOrgNumber = string.IsNullOrWhiteSpace(detectedBill.SupplierOrgNumber)
            ? null
            : detectedBill.SupplierOrgNumber.Trim();

        FinanceCounterparty? supplier = null;
        if (supplierOrgNumber is not null)
        {
            supplier = await _dbContext.FinanceCounterparties
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == detectedBill.CompanyId &&
                    x.CounterpartyType == "supplier" &&
                    x.MergedIntoCounterpartyId == null &&
                    x.TaxId == supplierOrgNumber)
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        supplier ??= await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == detectedBill.CompanyId &&
                x.CounterpartyType == "supplier" &&
                x.MergedIntoCounterpartyId == null &&
                x.Name.ToLower() == supplierName.ToLower())
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (supplier is null)
        {
            supplier = new FinanceCounterparty(
                Guid.NewGuid(),
                detectedBill.CompanyId,
                supplierName,
                "supplier",
                taxId: supplierOrgNumber,
                createdUtc: occurredUtc,
                updatedUtc: occurredUtc);
            _dbContext.FinanceCounterparties.Add(supplier);
        }

        var fallbackCurrency = await _dbContext.AccountingConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == detectedBill.CompanyId)
            .Select(x => x.BaseCurrency)
            .SingleOrDefaultAsync(cancellationToken);
        var currency = detectedBill.Currency?.Trim();
        if (currency?.Length != 3)
        {
            currency = fallbackCurrency?.Trim();
        }

        if (currency?.Length != 3)
        {
            currency = "SEK";
        }

        var receivedUtc = detectedBill.CreatedUtc;
        var dueUtc = detectedBill.DueDateUtc ?? detectedBill.InvoiceDateUtc?.AddDays(30) ?? receivedUtc.AddDays(30);
        operationalBill = new FinanceBill(
            Guid.NewGuid(),
            detectedBill.CompanyId,
            supplier.Id,
            billNumber,
            receivedUtc,
            dueUtc,
            detectedBill.TotalAmount ?? 0m,
            currency,
            "approved",
            createdUtc: occurredUtc,
            updatedUtc: occurredUtc,
            settlementStatus: FinanceSettlementStatuses.Unpaid,
            postingStatus: FinanceDocumentPostingStatuses.Draft,
            documentKind: FinanceDocumentKinds.SupplierInvoice,
            processingStatus: FinanceDocumentProcessingStatuses.None,
            sourceDetectedBillId: detectedBill.Id);
        _dbContext.FinanceBills.Add(operationalBill);
        return operationalBill.Id;
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
            catch (DbUpdateException exception) when (
                string.Equals(actionName, "approve", StringComparison.Ordinal) &&
                attempt < maxAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "Bill inbox approval hit an idempotency conflict; retrying with a fresh change tracker. CompanyId: {CompanyId}. BillId: {BillId}. Attempt: {Attempt}.",
                    companyId,
                    billId,
                    attempt);
                _dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Review action retry loop exited unexpectedly.");
    }

}
