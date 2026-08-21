using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportRefundFinanceService : ISupportRefundFinanceService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IFinanceAccountingActionService? _financeActions;

    public SupportRefundFinanceService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        TimeProvider timeProvider,
        IFinanceAccountingActionService? financeActions = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _timeProvider = timeProvider;
        _financeActions = financeActions;
    }

    public async Task<SupportRefundFinanceActionResult> CreateApprovedActionAsync(Guid companyId, Guid refundRequestId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is Guid existingActionId)
        {
            return new SupportRefundFinanceActionResult(refund.Id, existingActionId, false, 0m, refund.Status, "The finance action already exists.");
        }

        if (!string.Equals(refund.Status, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only approved support refunds can create finance actions.");
        }

        if (refund.InvoiceId is not Guid invoiceId)
        {
            throw new InvalidOperationException("Link a customer invoice before creating the refund or credit action.");
        }

        var invoice = await _dbContext.FinanceInvoices
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("The linked customer invoice was not found.");
        if (!string.Equals(invoice.DocumentKind, FinanceDocumentKinds.Invoice, StringComparison.OrdinalIgnoreCase) || invoice.Amount <= 0m)
        {
            throw new InvalidOperationException("The linked record is not an eligible customer invoice.");
        }

        if (!string.Equals(invoice.Currency, refund.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The refund currency must match the customer invoice currency.");
        }

        if (refund.PaymentId is Guid paymentId)
        {
            var paymentMatches = await _dbContext.Payments.AnyAsync(x =>
                x.CompanyId == companyId && x.Id == paymentId && x.Currency == refund.Currency,
                cancellationToken);
            if (!paymentMatches)
            {
                throw new InvalidOperationException("The linked payment was not found or uses another currency.");
            }
        }

        var paidAmount = Math.Max(invoice.PaidAmount, invoice.Allocations.Sum(x => x.AllocatedAmount));
        if (paidAmount <= 0m)
        {
            throw new InvalidOperationException("The customer invoice has no recorded payment to refund.");
        }

        var committedAmount = await _dbContext.SupportRefundRequests
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoice.Id && x.Id != refund.Id && x.FinanceActionReferenceId != null)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var refundableBalance = Math.Max(0m, decimal.Round(paidAmount - committedAmount, 2, MidpointRounding.AwayFromZero));
        if (refund.Amount > refundableBalance)
        {
            throw new InvalidOperationException($"The refund amount exceeds the refundable balance of {refundableBalance:0.00} {refund.Currency}.");
        }

        var actionId = CreateDeterministicId("support-refund-credit", companyId, refund.Id);
        var existing = await _dbContext.FinanceInvoices.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == actionId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            existing = new FinanceInvoice(
                actionId,
                companyId,
                invoice.CounterpartyId,
                $"SUP-CR-{refund.Id:N}"[..Math.Min(64, $"SUP-CR-{refund.Id:N}".Length)],
                now,
                now,
                -refund.Amount,
                refund.Currency,
                "approved",
                settlementStatus: FinanceSettlementStatuses.Unpaid,
                postingStatus: FinanceDocumentPostingStatuses.Draft,
                dueStatus: FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.CreditNote,
                processingStatus: FinanceDocumentProcessingStatuses.None);
            _dbContext.FinanceInvoices.Add(existing);
        }

        var created = refund.LinkFinanceAction(existing.Id);
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            AuditActorTypes.System,
            null,
            "support.refund.finance_action_created",
            "support_refund_request",
            refund.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Approved support refund was converted into an internal customer credit action.",
            ["support", "finance"],
            Metadata: new Dictionary<string, string?>
            {
                ["financeActionReferenceId"] = existing.Id.ToString("D"),
                ["sourceInvoiceId"] = invoice.Id.ToString("D"),
                ["refundableBalance"] = refundableBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            }), cancellationToken);
        return new SupportRefundFinanceActionResult(refund.Id, existing.Id, created, refundableBalance, refund.Status, "Customer credit action is ready for finance execution.");
    }

    public async Task<SupportRefundRequestDto> RequestExecutionAsync(
        Guid companyId,
        Guid refundRequestId,
        Guid? actorUserId,
        string actorDisplayName,
        CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is not Guid creditActionId)
        {
            throw new InvalidOperationException("Create the internal customer credit action before requesting provider execution.");
        }

        var financeActions = _financeActions
            ?? throw new InvalidOperationException("Customer credit provider execution is not configured.");
        var creditAction = await _dbContext.FinanceInvoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == creditActionId, cancellationToken)
            ?? throw new InvalidOperationException("The internal customer credit action was not found.");
        var writeRequestId = FinanceIntegrationWriteIdentity.CustomerInvoice("create", creditActionId, null);
        var state = await financeActions.RequestCustomerDocumentExportAsync(
            new RequestFinanceCustomerDocumentExportCommand(
                companyId,
                creditActionId,
                DateOnly.FromDateTime(creditAction.IssuedUtc),
                writeRequestId,
                actorUserId,
                actorDisplayName,
                $"support-refund:{refund.Id:N}:customer-credit"),
            cancellationToken);
        if (state.Status != FinanceAccountingActionStatuses.FinanceReviewRequired)
        {
            refund.MarkPendingFinanceApproval(state.WriteRequestId, state.ApprovalId);
        }
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            actorUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            actorUserId,
            "support.refund.finance_execution_requested",
            "support_refund_request",
            refund.Id.ToString("D"),
            AuditEventOutcomes.Pending,
            "Customer credit is waiting for accounting-system approval.",
            ["support", "finance", "approvals"],
            Metadata: new Dictionary<string, string?>
            {
                ["financeActionReferenceId"] = creditActionId.ToString("D"),
                ["writeRequestId"] = state.WriteRequestId.ToString("D"),
                ["approvalRequestId"] = state.ApprovalId?.ToString("D"),
                ["destination"] = state.DestinationName,
                ["authority"] = state.Authority
            }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto?> RefreshExecutionAsync(Guid companyId, Guid financeActionReferenceId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.FinanceActionReferenceId == financeActionReferenceId, cancellationToken);
        if (refund is null)
        {
            return null;
        }

        var writeRequestId = FinanceIntegrationWriteIdentity.CustomerInvoice("create", financeActionReferenceId, null);
        var write = await _dbContext.FinanceIntegrationWriteCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);
        if (write is null)
        {
            return SupportCaseService.MapRefund(refund);
        }

        if (refund.ApplyFinanceExecutionStatus(write.Status, write.SafeFailureSummary))
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.System,
                null,
                "support.refund.finance_execution_updated",
                "support_refund_request",
                refund.Id.ToString("D"),
                write.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
                BuildSafeExecutionSummary(refund.Status),
                ["support", "finance"],
                Metadata: new Dictionary<string, string?>
                {
                    ["financeActionReferenceId"] = financeActionReferenceId.ToString("D"),
                    ["writeRequestId"] = write.Id.ToString("D"),
                    ["writeStatus"] = write.Status
                }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto?> RefreshByWriteRequestAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var actionIds = await _dbContext.SupportRefundRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FinanceActionReferenceId != null)
            .Select(x => x.FinanceActionReferenceId!.Value)
            .ToListAsync(cancellationToken);
        var actionId = actionIds.FirstOrDefault(id =>
            FinanceIntegrationWriteIdentity.CustomerInvoice("create", id, null) == writeRequestId);
        return actionId == Guid.Empty
            ? null
            : await RefreshExecutionAsync(companyId, actionId, cancellationToken);
    }

    public async Task<SupportRefundRequestDto> CancelAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, string reason, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(reason, nameof(reason));
        var refund = await _dbContext.SupportRefundRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.CancelBeforeExecution())
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId, "support.refund.cancelled", "support_refund_request", refund.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Refund or credit request cancelled before provider execution.", ["support", "finance"], Metadata: new Dictionary<string, string?> { ["reason"] = reason.Trim()[..Math.Min(reason.Trim().Length, 500)] }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto> ReconcileAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is not Guid actionId) throw new InvalidOperationException("No accounting-system action exists to reconcile.");
        var refreshed = await RefreshExecutionAsync(companyId, actionId, cancellationToken);
        if (refreshed is not null && refreshed.Status is not (SupportRefundRequestStatuses.Failed or SupportRefundRequestStatuses.ReconciliationRequired)) return refreshed;
        refund.MarkReconciliationRequired("The accounting-system result is missing or inconclusive. Verify the provider record before retrying.");
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId, "support.refund.reconciliation_requested", "support_refund_request", refund.Id.ToString("D"), AuditEventOutcomes.Pending, "Refund or credit requires accounting-system reconciliation.", ["support", "finance", "reconciliation"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }

    private static string BuildSafeExecutionSummary(string status) => status switch
    {
        SupportRefundRequestStatuses.Completed => "Customer credit was completed in the accounting system.",
        SupportRefundRequestStatuses.Failed => "Customer credit execution failed and can be reviewed safely.",
        SupportRefundRequestStatuses.Cancelled => "Customer credit execution did not receive final approval.",
        SupportRefundRequestStatuses.ReconciliationRequired => "Customer credit outcome needs reconciliation before retrying.",
        _ => "Customer credit execution state was updated."
    };

    private static Guid CreateDeterministicId(string purpose, Guid companyId, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{companyId:N}:{sourceId:N}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}
