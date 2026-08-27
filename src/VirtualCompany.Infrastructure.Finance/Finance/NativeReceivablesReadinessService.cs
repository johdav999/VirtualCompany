using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class NativeReceivablesReadinessService : INativeReceivablesReadinessService
{
    private const int EvidenceLimit = 25;
    private static readonly string[] ReceivablesApprovalTargets =
    [
        ApprovalTargetEntityType.CustomerInvoiceDraft.ToStorageValue(),
        ApprovalTargetEntityType.CustomerInvoiceSchedule.ToStorageValue(),
        ApprovalTargetEntityType.CustomerCollectionReminder.ToStorageValue(),
        ApprovalTargetEntityType.CustomerInvoiceAccounting.ToStorageValue()
    ];

    private readonly VirtualCompanyDbContext _db;
    private readonly ICustomerInvoiceAccountingService _invoiceAccounting;
    private readonly TimeProvider _timeProvider;

    public NativeReceivablesReadinessService(
        VirtualCompanyDbContext db,
        ICustomerInvoiceAccountingService invoiceAccounting,
        TimeProvider timeProvider)
    {
        _db = db;
        _invoiceAccounting = invoiceAccounting;
        _timeProvider = timeProvider;
    }

    public async Task<NativeReceivablesReadinessDto> GetAsync(
        GetNativeReceivablesReadinessQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
            throw new ArgumentException("CompanyId is required.", nameof(query));

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var staleApprovalBefore = nowUtc.AddDays(-7);
        var expiredWorkSince = nowUtc.AddDays(-30);
        var staleExecutionBefore = nowUtc.AddMinutes(-15);
        var signals = new List<NativeReceivablesReadinessSignalDto>(10);

        var staleApprovals = _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && ReceivablesApprovalTargets.Contains(x.TargetEntityType) &&
                (x.Status == ApprovalRequestStatus.Pending && x.UpdatedUtc <= staleApprovalBefore ||
                 x.Status == ApprovalRequestStatus.Expired && x.UpdatedUtc >= expiredWorkSince));
        signals.Add(await SignalAsync(staleApprovals.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.StaleApprovals,
            NativeReceivablesReadinessStatuses.Attention,
            "Receivables approvals have expired or have waited more than seven days.",
            "Approve, reject, cancel, or replace each request from current source evidence.",
            "All receivables approvals are current.", cancellationToken));

        var gaps = _db.StatutoryDocumentNumberAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == StatutoryDocumentAllocationStatuses.Gap);
        signals.Add(await SignalAsync(gaps.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.NumberingGaps,
            NativeReceivablesReadinessStatuses.Attention,
            "Document number gaps need operator review and retained reasons.",
            "Review the series, gap reason, related issue attempt, and audit evidence; never reuse the number.",
            "No retained document number gap needs review.", cancellationToken));

        var renderFailures = _db.CustomerInvoiceRenderedArtifacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                (x.Status == CustomerInvoiceRenderStatuses.Failed ||
                 x.Status == CustomerInvoiceRenderStatuses.Rendering && x.UpdatedUtc <= staleExecutionBefore));
        signals.Add(await SignalAsync(renderFailures.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.RenderFailures,
            NativeReceivablesReadinessStatuses.Blocking,
            "Invoice PDF generation failed or stopped before completion.",
            "Correct the safe render or object-storage failure, then request a deterministic re-render from the immutable snapshot.",
            "Invoice PDF generation has no failed or stale work.", cancellationToken));

        var ambiguousEmailIds = _db.CustomerInvoiceEmailDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == CustomerInvoiceDeliveryStatuses.ReconciliationRequired)
            .Select(x => x.Id);
        var ambiguousElectronicIds = _db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == CustomerInvoiceElectronicDeliveryStatuses.ReconciliationRequired)
            .Select(x => x.Id);
        var ambiguousReminderIds = _db.CustomerReminderDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == "reconciliation_required")
            .Select(x => x.Id);
        var ambiguousCount = await ambiguousEmailIds.CountAsync(cancellationToken) +
            await ambiguousElectronicIds.CountAsync(cancellationToken) +
            await ambiguousReminderIds.CountAsync(cancellationToken);
        var ambiguousIds = (await ambiguousEmailIds.Take(EvidenceLimit).ToArrayAsync(cancellationToken))
            .Concat(await ambiguousElectronicIds.Take(EvidenceLimit).ToArrayAsync(cancellationToken))
            .Concat(await ambiguousReminderIds.Take(EvidenceLimit).ToArrayAsync(cancellationToken))
            .Take(EvidenceLimit).ToArray();
        signals.Add(Signal(NativeReceivablesReadinessSignalKeys.DeliveryAmbiguity,
            ambiguousCount == 0 ? NativeReceivablesReadinessStatuses.Healthy : NativeReceivablesReadinessStatuses.Blocking,
            ambiguousCount, null, null,
            ambiguousCount == 0 ? "No customer communication has an uncertain external outcome."
                : "Customer delivery acceptance is uncertain and must not be retried yet.",
            "Inspect mailbox or provider evidence and reconcile the original attempt before any resend.", ambiguousIds));

        var recurringBlockers = _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                (x.Status == CustomerInvoiceScheduleOccurrenceStatuses.Blocked ||
                 x.Status == CustomerInvoiceScheduleOccurrenceStatuses.Failed ||
                 x.Status == CustomerInvoiceScheduleOccurrenceStatuses.Processing && x.LeaseExpiresUtc <= nowUtc));
        signals.Add(await SignalAsync(recurringBlockers.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.RecurringBlockers,
            NativeReceivablesReadinessStatuses.Blocking,
            "Recurring invoice generation has blocked, failed, or abandoned work.",
            "Review the linked task and current customer, tax, approval, evidence, and lease state before an explicit retry.",
            "Recurring invoice generation has no blocked or abandoned occurrence.", cancellationToken));

        var rejectedElectronic = _db.CustomerInvoiceElectronicDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == CustomerInvoiceElectronicDeliveryStatuses.Rejected);
        signals.Add(await SignalAsync(rejectedElectronic.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.ElectronicInvoiceRejections,
            NativeReceivablesReadinessStatuses.Blocking,
            "Electronic invoices were definitively rejected and need remediation.",
            "Review participant, profile, and safe rejection evidence; use email fallback only when policy proves it safe.",
            "No electronic invoice rejection needs action.", cancellationToken));

        var refundReconciliation = _db.CustomerInvoiceRefundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId &&
                (x.Status == CustomerInvoiceRefundExecutionStatuses.ReconciliationRequired ||
                 x.Status == CustomerInvoiceRefundExecutionStatuses.Executing && x.ClaimedUtc <= staleExecutionBefore));
        signals.Add(await SignalAsync(refundReconciliation.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.RefundReconciliation,
            NativeReceivablesReadinessStatuses.Blocking,
            "Refund completion is uncertain or its execution lease expired.",
            "Confirm the bank or provider outcome and record evidence before retrying or releasing allocations.",
            "No refund outcome needs reconciliation.", cancellationToken));

        signals.Add(await BuildReceivablesControlSignalAsync(query.CompanyId, cancellationToken));

        var overdueFollowUps = _db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status != CustomerCollectionCaseStatuses.Resolved &&
                x.FollowUpDueUtc != null && x.FollowUpDueUtc < nowUtc);
        signals.Add(await SignalAsync(overdueFollowUps.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.OverdueCollectionFollowUps,
            NativeReceivablesReadinessStatuses.Attention,
            "Customer collection follow-ups are overdue.",
            "Review the owner, dispute or promise evidence, and set the next current action.",
            "No customer collection follow-up is overdue.", cancellationToken));

        var archiveFailures = _db.AccountingExportJobs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Status == AccountingExportStatuses.Failed &&
                x.ExportType == AccountingExportTypeValues.SwedishStatutoryArchive);
        signals.Add(await SignalAsync(archiveFailures.Select(x => x.Id),
            NativeReceivablesReadinessSignalKeys.DocumentArchiveFailures,
            NativeReceivablesReadinessStatuses.Blocking,
            "A statutory document archive failed and its retained object evidence is incomplete.",
            "Correct storage or export inputs, preserve the failed record, and generate a new checksum-verifiable archive.",
            "No statutory document or archive failure needs action.", cancellationToken));

        var blocking = signals.Count(x => x.Status == NativeReceivablesReadinessStatuses.Blocking);
        var attention = signals.Count(x => x.Status == NativeReceivablesReadinessStatuses.Attention);
        var healthy = signals.Count - blocking - attention;
        var status = blocking > 0 ? NativeReceivablesReadinessStatuses.Blocking
            : attention > 0 ? NativeReceivablesReadinessStatuses.Attention
            : NativeReceivablesReadinessStatuses.Healthy;
        return new NativeReceivablesReadinessDto(query.CompanyId, status, blocking == 0, nowUtc,
            blocking, attention, healthy, signals);
    }

    private async Task<NativeReceivablesReadinessSignalDto> BuildReceivablesControlSignalAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _invoiceAccounting.ReconcileAsync(
                new GetCustomerInvoiceReceivableReconciliationQuery(companyId), cancellationToken);
            return Signal(NativeReceivablesReadinessSignalKeys.ReceivablesControl,
                result.IsReconciled ? NativeReceivablesReadinessStatuses.Healthy : NativeReceivablesReadinessStatuses.Blocking,
                result.IsReconciled ? 0 : 1, result.Difference, result.BaseCurrency,
                result.IsReconciled
                    ? "The accounts-receivable control account agrees with posted customer documents."
                    : "The accounts-receivable control account does not agree with posted customer documents.",
                "Investigate invoice journals, credit notes, and allocation evidence before collection or close.", []);
        }
        catch (CustomerInvoiceAccountingException)
        {
            return Signal(NativeReceivablesReadinessSignalKeys.ReceivablesControl,
                NativeReceivablesReadinessStatuses.Blocking, 1, null, null,
                "Accounts-receivable control reconciliation is unavailable because accounting setup is incomplete.",
                "Complete accounting setup and configure the accounts-receivable control role, then rerun readiness.", []);
        }
    }

    private static async Task<NativeReceivablesReadinessSignalDto> SignalAsync(
        IQueryable<Guid> query,
        string key,
        string issueStatus,
        string issueExplanation,
        string action,
        string healthyExplanation,
        CancellationToken cancellationToken)
    {
        var count = await query.CountAsync(cancellationToken);
        var ids = await query.Take(EvidenceLimit).ToArrayAsync(cancellationToken);
        return Signal(key, count == 0 ? NativeReceivablesReadinessStatuses.Healthy : issueStatus,
            count, null, null, count == 0 ? healthyExplanation : issueExplanation, action, ids);
    }

    private static NativeReceivablesReadinessSignalDto Signal(
        string key,
        string status,
        int count,
        decimal? amount,
        string? currency,
        string explanation,
        string action,
        IReadOnlyList<Guid> subjectIds) =>
        new(key, status, count, amount, currency, explanation, action, subjectIds);
}
