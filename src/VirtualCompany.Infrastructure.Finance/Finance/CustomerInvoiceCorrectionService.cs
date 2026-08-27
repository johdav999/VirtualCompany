using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceCorrectionService : ICustomerInvoiceCorrectionService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICustomerInvoiceCorrectionPolicy _policy;
    private readonly ICustomerInvoiceDraftService _drafts;
    private readonly ICustomerInvoiceDraftCalculationPolicy _calculations;
    private readonly IApprovalRequestService _approvals;
    private readonly IAccountingPostingService _posting;
    private readonly IVatReturnService _vatReturns;
    private readonly IAuditEventWriter _audit;
    private readonly IReadOnlyDictionary<string, ICustomerRefundExecutionProvider> _refundProviders;
    private readonly TimeProvider _time;
    private readonly CustomerInvoiceCorrectionTelemetry _telemetry;

    public CustomerInvoiceCorrectionService(VirtualCompanyDbContext db, ICustomerInvoiceCorrectionPolicy policy,
        ICustomerInvoiceDraftService drafts, ICustomerInvoiceDraftCalculationPolicy calculations,
        IApprovalRequestService approvals, IAccountingPostingService posting, IVatReturnService vatReturns,
        IAuditEventWriter audit, TimeProvider time, CustomerInvoiceCorrectionTelemetry telemetry,
        IEnumerable<ICustomerRefundExecutionProvider>? refundProviders = null)
    {
        _db = db; _policy = policy; _drafts = drafts; _calculations = calculations; _approvals = approvals;
        _posting = posting; _vatReturns = vatReturns; _audit = audit; _time = time; _telemetry = telemetry;
        _refundProviders = (refundProviders ?? []).GroupBy(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    public Task<CustomerInvoiceCorrectionPolicyDecisionDto> EvaluateAsync(
        EvaluateCustomerInvoiceCorrectionQuery query, CancellationToken cancellationToken) =>
        _policy.EvaluateAsync(query, cancellationToken);

    public async Task<CustomerInvoiceCorrectionDto> ProposeAsync(ProposeCustomerInvoiceCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.InvoiceId, command.ActorUserId, command.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.EvidenceReference))
            throw Error(CustomerInvoiceCorrectionReasonCodes.EvidenceRequired,
                "A correction reason and retained evidence reference are required.");
        var payloadHash = Hash(string.Join('|', command.InvoiceId, command.CorrectionType,
            command.Amount.ToString("G29", CultureInfo.InvariantCulture), command.Currency,
            command.Reason.Trim(), command.EvidenceReference.Trim(), command.BeneficiaryReference,
            command.PaymentEvidenceReference, command.ProviderKey));
        var replay = await _db.CustomerInvoiceCorrections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == command.IdempotencyKey.Trim(), cancellationToken);
        if (replay is not null)
        {
            if (!string.Equals(replay.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                throw Error(CustomerInvoiceCorrectionReasonCodes.IdempotencyConflict,
                    "This request identity was already used for a different correction.", true);
            _telemetry.Record("propose", replay.CorrectionType, "replayed", true);
            return (await MapAsync(replay.CompanyId, replay.Id, cancellationToken)) with { IsIdempotentReplay = true };
        }

        var decision = await _policy.EvaluateAsync(new(command.CompanyId, command.InvoiceId,
            command.CorrectionType, command.Amount, command.Currency, command.ProviderKey), cancellationToken);
        if (!decision.IsAllowed) throw Error(decision.ReasonCode, decision.Explanation);
        var type = CustomerInvoiceCorrectionTypes.Normalize(command.CorrectionType);
        if (type == CustomerInvoiceCorrectionTypes.Refund &&
            (string.IsNullOrWhiteSpace(command.BeneficiaryReference) || string.IsNullOrWhiteSpace(command.PaymentEvidenceReference)))
            throw Error(CustomerInvoiceCorrectionReasonCodes.PaymentEvidenceRequired,
                "Refund proposals require beneficiary and payment evidence before approval.");

        CustomerInvoiceDraftDto? preparedDraft = null;
        CustomerInvoiceDraftCalculation? creditCalculation = null;
        if (CustomerInvoiceCorrectionTypes.CreditTypes.Contains(type))
        {
            if (command.CreditDraft is null || command.CreditDraft.DocumentType != CustomerInvoiceDraftDocumentTypes.CreditNote ||
                command.CreditDraft.OriginalInvoiceId != command.InvoiceId)
                throw Error(CustomerInvoiceCorrectionReasonCodes.CreditDraftRequired,
                    "A linked native customer credit-note draft is required for this correction.");
            creditCalculation = await _calculations.CalculateAsync(command.CompanyId, command.CreditDraft, cancellationToken);
            if (creditCalculation.Blockers.Count > 0)
                throw Error(creditCalculation.Blockers[0].ReasonCode, creditCalculation.Blockers[0].Explanation);
            if (decimal.Round(creditCalculation.GrossTotal, 2) != decimal.Round(command.Amount, 2))
                throw Error(CustomerInvoiceCorrectionReasonCodes.AmountExceedsBalance,
                    "The approved correction amount must equal the credit-note draft gross total.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var correction = new CustomerInvoiceCorrection(Guid.NewGuid(), command.CompanyId, command.InvoiceId,
            type, command.Amount, command.Currency, command.Reason, decision.SourceVersion, decision.SourceHash,
            payloadHash, command.IdempotencyKey, command.EvidenceReference, command.ActorUserId, now,
            command.BeneficiaryReference, command.PaymentEvidenceReference, command.ProviderKey,
            decision.OriginalVatReturnId);
        _db.CustomerInvoiceCorrections.Add(correction);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            if (command.CreditDraft is not null)
            {
                preparedDraft = await _drafts.CreateAsync(new(command.CompanyId, command.CreditDraft,
                    $"{command.IdempotencyKey.Trim()}:draft", command.ActorUserId, command.CorrelationId), cancellationToken);
                correction.BindCreditDraft(preparedDraft.Id, now);
                await _db.SaveChangesAsync(cancellationToken);
                var submission = await _drafts.SubmitAsync(new(command.CompanyId, preparedDraft.Id,
                    preparedDraft.Version, $"{command.IdempotencyKey.Trim()}:approval", command.ActorUserId,
                    command.CorrelationId), cancellationToken);
                var task = BuildTask(correction, command.ActorUserId, now);
                _db.WorkTasks.Add(task);
                correction.BindApproval(submission.ApprovalRequestId, task.Id, now);
            }
            else
            {
                var task = BuildTask(correction, command.ActorUserId, now);
                _db.WorkTasks.Add(task);
                await _db.SaveChangesAsync(cancellationToken);
                var approval = await _approvals.CreateAsync(command.CompanyId, new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.Task.ToStorageValue(), task.Id, AuditActorTypes.User,
                    command.ActorUserId, $"customer_invoice_{type}", new Dictionary<string, JsonNode?>
                    {
                        ["correctionId"] = correction.Id.ToString("D"), ["sourceVersion"] = decision.SourceVersion,
                        ["sourceHash"] = decision.SourceHash, ["payloadHash"] = payloadHash,
                        ["amount"] = command.Amount, ["currency"] = command.Currency
                    }, RequiredRole: "finance_approver"), cancellationToken);
                correction.BindApproval(approval.Id, task.Id, now);
            }
            await WriteAuditAsync(correction, command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceCorrectionProposed,
                "A receivables correction was proposed with current balance, source, approval, and evidence facts.",
                command.CorrelationId, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            correction.MarkFailed("proposal_initialization_failed",
                "The proposal could not initialize its approval workflow. Any retained native draft remains non-issuable until the correction is explicitly reproposed.",
                _time.GetUtcNow().UtcDateTime);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        _telemetry.Record("propose", correction.CorrectionType, "approval_requested");
        return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
    }

    public async Task<CustomerInvoiceCorrectionDto> ExecuteAsync(ExecuteCustomerInvoiceCorrectionCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.CorrectionId, command.ActorUserId, command.IdempotencyKey);
        var correction = await Query(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.CorrectionId, cancellationToken)
            ?? throw Error(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound, "The customer invoice correction could not be found.");
        if (correction.Version != command.ExpectedVersion)
            throw Error(CustomerInvoiceCorrectionReasonCodes.VersionConflict,
                $"This correction is now version {correction.Version}. Reload it before continuing.", true, correction.Version);
        if (correction.Status == CustomerInvoiceCorrectionStatuses.Executed)
            return (await MapAsync(command.CompanyId, correction.Id, cancellationToken)) with { IsIdempotentReplay = true };
        var decision = await _policy.EvaluateAsync(new(command.CompanyId, correction.InvoiceId,
            correction.CorrectionType, correction.Amount, correction.Currency, correction.ProviderKey,
            correction.Id), cancellationToken);
        if (!decision.IsAllowed) throw Error(decision.ReasonCode, decision.Explanation);
        if (!string.Equals(command.ExpectedSourceHash, correction.SourceHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(decision.SourceHash, correction.SourceHash, StringComparison.OrdinalIgnoreCase))
            throw Error(CustomerInvoiceCorrectionReasonCodes.SourceChanged,
                "The invoice, settlement, allocation, period, tax, or correction facts changed after approval.", true, correction.Version);
        EnsureApproval(correction);

        if (decision.RequiresVatCorrectionReturn && correction.CorrectionVatReturnId is null && decision.OriginalVatReturnId.HasValue)
        {
            var vat = await _vatReturns.CreateCorrectionAsync(new(command.CompanyId,
                decision.OriginalVatReturnId.Value, correction.Reason, correction.EvidenceReference,
                $"ar-correction:{correction.Id:N}:vat", command.ActorUserId), cancellationToken);
            correction.BindVatCorrection(vat.Id, _time.GetUtcNow().UtcDateTime);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (CustomerInvoiceCorrectionTypes.CreditTypes.Contains(correction.CorrectionType))
            return await ExecuteCreditAsync(correction, command, cancellationToken);
        if (correction.CorrectionType == CustomerInvoiceCorrectionTypes.Refund)
            return await QueueRefundAsync(correction, command, cancellationToken);
        if (correction.CorrectionType == CustomerInvoiceCorrectionTypes.Cancellation)
        {
            correction.Invoice.CancelForReceivablesCorrection(_time.GetUtcNow().UtcDateTime);
            correction.MarkExecuted(command.ActorUserId, _time.GetUtcNow().UtcDateTime);
            await CompleteAsync(correction, command.ActorUserId,
                "The legally eligible unposted and undelivered invoice was cancelled without changing historical documents or journals.",
                command.CorrelationId, cancellationToken);
            return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
        }
        return await ExecuteJournalCorrectionAsync(correction, command, cancellationToken);
    }

    public async Task<CustomerInvoiceCorrectionDto> ReconcileRefundAsync(
        ReconcileCustomerInvoiceRefundCommand command, CancellationToken cancellationToken)
    {
        var correction = await Query(true).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == command.CorrectionId, cancellationToken) ?? throw Error(
            CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound, "The refund correction could not be found.");
        if (correction.Version != command.ExpectedVersion)
            throw Error(CustomerInvoiceCorrectionReasonCodes.VersionConflict,
                $"This correction is now version {correction.Version}.", true, correction.Version);
        var refundExecution = correction.RefundExecution;
        var refundStatus = refundExecution?.Status;
        if (refundExecution is null || refundStatus is not (CustomerInvoiceRefundExecutionStatuses.ReconciliationRequired or
            CustomerInvoiceRefundExecutionStatuses.ManualInstruction))
            throw Error(CustomerInvoiceCorrectionReasonCodes.RefundReconciliationRequired,
                "Only a refund awaiting provider reconciliation or manual payment confirmation can be resolved here.");
        if (string.IsNullOrWhiteSpace(command.EvidenceReference) ||
            command.ProviderConfirmedSucceeded == command.ProviderConfirmedAbsent)
            throw Error(CustomerInvoiceCorrectionReasonCodes.EvidenceRequired,
                "Record evidence and exactly one confirmed provider outcome.");
        var now = _time.GetUtcNow().UtcDateTime;
        if (command.ProviderConfirmedSucceeded)
        {
            refundExecution.MarkSucceeded(command.ProviderReference ?? "operator-confirmed", now);
            await ReleaseAllocationsAsync(correction, now, cancellationToken);
            correction.MarkExecuted(command.ActorUserId, now);
        }
        else if (refundStatus == CustomerInvoiceRefundExecutionStatuses.ReconciliationRequired)
        {
            refundExecution.ScheduleRetry("provider_confirmed_absent",
                "The provider confirmed that no refund was made; a bounded retry is safe.", now);
            correction.MarkQueued(now);
        }
        else
        {
            refundExecution.MarkFailed("manual_instruction_not_executed",
                "The approved manual refund instruction was confirmed as not executed.", now);
            correction.MarkFailed("customer_invoice_refund_not_executed",
                "The approved manual refund instruction was confirmed as not executed.", now);
        }
        await WriteAuditAsync(correction, command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceRefundReconciled,
            "A refund provider outcome was reconciled from retained operator evidence.", command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Record("reconcile_refund", correction.CorrectionType,
            command.ProviderConfirmedSucceeded ? "succeeded" : "confirmed_absent");
        return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionDto> GetAsync(Guid companyId, Guid correctionId,
        CancellationToken cancellationToken) => MapAsync(companyId, correctionId, cancellationToken);

    public async Task<CustomerInvoiceCorrectionListResult> ListAsync(ListCustomerInvoiceCorrectionsQuery query,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, query.Skip); var take = Math.Clamp(query.Take, 1, 250);
        var source = _db.CustomerInvoiceCorrections.AsNoTracking().Where(x => x.CompanyId == query.CompanyId);
        if (query.InvoiceId.HasValue) source = source.Where(x => x.InvoiceId == query.InvoiceId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var total = await source.CountAsync(cancellationToken);
        var ids = await source.OrderByDescending(x => x.UpdatedUtc).Skip(skip).Take(take).Select(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<CustomerInvoiceCorrectionDto>(ids.Count);
        foreach (var id in ids) items.Add(await MapAsync(query.CompanyId, id, cancellationToken));
        return new(items, total, skip, take);
    }

    private async Task<CustomerInvoiceCorrectionDto> ExecuteCreditAsync(CustomerInvoiceCorrection correction,
        ExecuteCustomerInvoiceCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (correction.CreditDraft is null || command.SeriesId is null || command.FiscalPeriodId is null ||
            command.AccountingDate is null || string.IsNullOrWhiteSpace(command.VoucherSeriesCode))
            throw Error(CustomerInvoiceCorrectionReasonCodes.CreditDraftRequired,
                "Credit-note execution requires its approved draft, number series, open period, and voucher series.");
        var issued = await _drafts.IssueAsync(new(command.CompanyId, correction.CreditDraft.Id,
            correction.CreditDraft.Version, correction.CreditDraft.ResultHash, command.SeriesId.Value,
            command.FiscalPeriodId.Value, command.AccountingDate.Value, command.VoucherSeriesCode,
            command.IdempotencyKey, command.ActorUserId, command.CorrelationId), cancellationToken);
        correction.MarkExecuted(command.ActorUserId, _time.GetUtcNow().UtcDateTime,
            issued.InvoiceId, issued.LedgerEntryId);
        await CompleteAsync(correction, command.ActorUserId,
            "The approved linked credit note was issued, numbered, snapshotted, and posted through the native invoice boundary.",
            command.CorrelationId, cancellationToken);
        return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
    }

    private async Task<CustomerInvoiceCorrectionDto> QueueRefundAsync(CustomerInvoiceCorrection correction,
        ExecuteCustomerInvoiceCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (correction.RefundExecution is not null) return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
        var hasProvider = !string.IsNullOrWhiteSpace(correction.ProviderKey) && _refundProviders.ContainsKey(correction.ProviderKey);
        var now = _time.GetUtcNow().UtcDateTime;
        var execution = new CustomerInvoiceRefundExecution(Guid.NewGuid(), correction.CompanyId, correction.Id,
            hasProvider ? correction.ProviderKey : null, $"refund:{correction.Id:N}:{correction.SourceHash}",
            correction.BeneficiaryReference!, correction.PaymentEvidenceReference!, !hasProvider, now);
        _db.CustomerInvoiceRefundExecutions.Add(execution);
        if (hasProvider) correction.MarkQueued(now); else correction.MarkManualInstruction(now);
        await WriteAuditAsync(correction, command.ActorUserId, AuditEventActions.AccountingCustomerInvoiceRefundQueued,
            hasProvider
                ? "The approved refund was queued for durable provider execution."
                : "No refund provider is configured; an approved manual payment instruction was created without claiming money moved.",
            command.CorrelationId, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Record("queue_refund", correction.CorrectionType, hasProvider ? "queued" : "manual_instruction");
        return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
    }

    private async Task<CustomerInvoiceCorrectionDto> ExecuteJournalCorrectionAsync(CustomerInvoiceCorrection correction,
        ExecuteCustomerInvoiceCorrectionCommand command, CancellationToken cancellationToken)
    {
        if (command.FiscalPeriodId is null || command.AccountingDate is null ||
            string.IsNullOrWhiteSpace(command.VoucherSeriesCode) || command.ExpenseAccountId is null)
            throw Error(CustomerInvoiceCorrectionReasonCodes.PeriodUnavailable,
                "Write-off, bad-debt, and recovery execution requires an open current period, voucher series, and selected correction account.");
        var period = await _db.FiscalPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == command.FiscalPeriodId, cancellationToken);
        if (period is null || period.IsClosed || period.IsReportingLocked ||
            command.AccountingDate < DateOnly.FromDateTime(period.StartUtc) ||
            command.AccountingDate >= DateOnly.FromDateTime(period.EndUtc))
            throw Error(CustomerInvoiceCorrectionReasonCodes.PeriodUnavailable,
                "The selected current accounting period is unavailable.");
        var configuration = await _db.AccountingConfigurations.Include(x => x.AccountRoles)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken)
            ?? throw Error(CustomerInvoiceCorrectionReasonCodes.AccountUnavailable, "Accounting configuration is unavailable.");
        var receivableId = configuration.AccountRoles.SingleOrDefault(x => x.RoleKey == "accounts_receivable")?.FinanceAccountId
            ?? throw Error(CustomerInvoiceCorrectionReasonCodes.AccountUnavailable, "The accounts receivable control account is unavailable.");
        var expense = await _db.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == command.ExpenseAccountId && x.IsPostingEnabled, cancellationToken)
            ?? throw Error(CustomerInvoiceCorrectionReasonCodes.AccountUnavailable, "The selected correction account is unavailable.");
        if (expense.Id == receivableId)
            throw Error(CustomerInvoiceCorrectionReasonCodes.AccountUnavailable,
                "The correction account must be separate from accounts receivable.");
        var originalProfile = await _db.CustomerInvoiceAccountingProfiles.AsNoTracking().SingleAsync(x =>
            x.CompanyId == command.CompanyId && x.InvoiceId == correction.InvoiceId, cancellationToken);
        var recovery = correction.CorrectionType == CustomerInvoiceCorrectionTypes.BadDebtRecovery;
        var lines = new[]
        {
            new ProposedAccountingLine(receivableId, recovery ? correction.Amount : 0m,
                recovery ? 0m : correction.Amount, correction.Currency, "Accounts receivable correction"),
            new ProposedAccountingLine(expense.Id, recovery ? 0m : correction.Amount,
                recovery ? correction.Amount : 0m, correction.Currency,
                recovery ? "Bad-debt recovery" : "Receivables write-off")
        };
        var entry = new ProposedAccountingEntry(command.CompanyId, command.FiscalPeriodId.Value,
            command.VoucherSeriesCode.Trim().ToUpperInvariant(), DateOnly.FromDateTime(correction.Invoice.IssuedUtc),
            command.AccountingDate.Value, LedgerPostingTypeValues.Adjustment,
            $"{correction.CorrectionType.Replace('_', ' ')} for {correction.Invoice.InvoiceNumber}",
            "customer_invoice_correction", correction.Id.ToString("D"), correction.Version.ToString(CultureInfo.InvariantCulture),
            command.IdempotencyKey, lines, command.ActorUserId, correction.ApprovalRequestId, false,
            new Dictionary<string, string> { ["sourceHash"] = correction.SourceHash,
                ["correctionType"] = correction.CorrectionType, ["evidenceReference"] = correction.EvidenceReference },
            "post", correction.PayloadHash, OriginalLedgerEntryId: originalProfile.LedgerEntryId,
            CorrectionReason: correction.Reason);
        var posted = await _posting.PostAsync(new(entry, command.CorrelationId), cancellationToken);
        correction.MarkExecuted(command.ActorUserId, _time.GetUtcNow().UtcDateTime,
            ledgerEntryId: posted.Journal.Id, expenseAccountId: expense.Id);
        await CompleteAsync(correction, command.ActorUserId,
            "The approved receivables correction was posted as a linked current-period journal without changing the original journal.",
            command.CorrelationId, cancellationToken);
        return await MapAsync(command.CompanyId, correction.Id, cancellationToken);
    }

    private void EnsureApproval(CustomerInvoiceCorrection correction)
    {
        if (correction.ApprovalRequest is null || correction.ApprovalRequest.Status != ApprovalRequestStatus.Approved)
            throw Error(CustomerInvoiceCorrectionReasonCodes.ApprovalPending,
                "The current correction proposal is waiting for approval.");
        if (correction.CreditDraft is not null) return;
        var context = correction.ApprovalRequest.ThresholdContext;
        var sourceVersion = context.TryGetValue("sourceVersion", out var versionNode) ? versionNode?.ToString().Trim('"') : null;
        var sourceHash = context.TryGetValue("sourceHash", out var sourceNode) ? sourceNode?.ToString().Trim('"') : null;
        var payloadHash = context.TryGetValue("payloadHash", out var payloadNode) ? payloadNode?.ToString().Trim('"') : null;
        if (!string.Equals(sourceVersion, correction.SourceVersion, StringComparison.Ordinal) ||
            !string.Equals(sourceHash, correction.SourceHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payloadHash, correction.PayloadHash, StringComparison.OrdinalIgnoreCase))
            throw Error(CustomerInvoiceCorrectionReasonCodes.ApprovalStale,
                "The approval does not match the current correction source and payload.", true, correction.Version);
    }

    private WorkTask BuildTask(CustomerInvoiceCorrection correction, Guid actorId, DateTime now) => new(
        Guid.NewGuid(), correction.CompanyId, $"finance.customer_invoice_{correction.CorrectionType}",
        $"Approve {correction.CorrectionType.Replace('_', ' ')} for invoice {correction.Invoice?.InvoiceNumber ?? correction.InvoiceId.ToString("N")}",
        $"Review {correction.Amount:0.00} {correction.Currency}. Execution rechecks the invoice, allocations, tax return, period, and approval before any accounting or money movement.",
        WorkTaskPriority.High, null, null, AuditActorTypes.User, actorId,
        new Dictionary<string, JsonNode?> { ["correctionId"] = correction.Id.ToString("D"),
            ["invoiceId"] = correction.InvoiceId.ToString("D"), ["sourceHash"] = correction.SourceHash,
            ["evidenceReference"] = correction.EvidenceReference }, rationaleSummary:
        "Receivables corrections require explicit human approval and retained evidence.",
        correlationId: $"ar-correction:{correction.Id:N}", sourceType: WorkTaskSourceTypes.User,
        triggerSource: "finance_customer_invoice", creationReason: correction.Reason,
        triggerEventId: correction.InvoiceId.ToString("N"), status: WorkTaskStatus.AwaitingApproval);

    private IQueryable<CustomerInvoiceCorrection> Query(bool tracking) =>
        (tracking ? _db.CustomerInvoiceCorrections : _db.CustomerInvoiceCorrections.AsNoTracking())
        .Include(x => x.Invoice).Include(x => x.ApprovalRequest).Include(x => x.CreditDraft).ThenInclude(x => x!.ApprovalRequest)
        .Include(x => x.RefundExecution);

    private async Task<CustomerInvoiceCorrectionDto> MapAsync(Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var x = await Query(false).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken)
            ?? throw Error(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound, "The customer invoice correction could not be found.");
        var refund = x.RefundExecution is null ? null : new CustomerInvoiceRefundExecutionDto(x.RefundExecution.Id,
            x.RefundExecution.ProviderKey, x.RefundExecution.Status, x.RefundExecution.AttemptCount,
            x.RefundExecution.AvailableUtc, x.RefundExecution.ProviderReference, x.RefundExecution.FailureCategory,
            x.RefundExecution.SafeFailureSummary, x.RefundExecution.CreatedUtc, x.RefundExecution.UpdatedUtc,
            x.RefundExecution.CompletedUtc);
        var actions = new List<string>();
        if (x.Status is CustomerInvoiceCorrectionStatuses.AwaitingApproval or CustomerInvoiceCorrectionStatuses.DraftCreated) actions.Add("wait_for_approval");
        if (x.ApprovalRequest?.Status == ApprovalRequestStatus.Approved && x.Status is not (CustomerInvoiceCorrectionStatuses.Executed or CustomerInvoiceCorrectionStatuses.Queued)) actions.Add("execute");
        if (x.Status == CustomerInvoiceCorrectionStatuses.ReconciliationRequired) actions.Add("reconcile_refund");
        if (x.Status == CustomerInvoiceCorrectionStatuses.ManualInstruction) actions.Add("record_manual_payment_outcome");
        return new(x.Id, x.CompanyId, x.InvoiceId, x.Invoice.InvoiceNumber, x.CorrectionType, x.Amount,
            x.Currency, x.Reason, x.Status, x.Version, x.SourceVersion, x.SourceHash, x.EvidenceReference,
            x.ApprovalRequestId, x.ApprovalRequest?.Status.ToStorageValue(), x.TaskId, x.CreditDraftId,
            x.CorrectingInvoiceId, x.LedgerEntryId, x.OriginalVatReturnId, x.CorrectionVatReturnId,
            x.ExpenseAccountId, x.ProviderKey, x.BeneficiaryReference, x.PaymentEvidenceReference,
            x.CreatedByUserId, x.ExecutedByUserId, x.CreatedUtc, x.UpdatedUtc, x.ExecutedUtc,
            x.FailureReasonCode, x.FailureSummary, refund, actions);
    }

    private async Task CompleteAsync(CustomerInvoiceCorrection correction, Guid actorId, string summary,
        string? correlationId, CancellationToken cancellationToken)
    {
        await WriteAuditAsync(correction, actorId, AuditEventActions.AccountingCustomerInvoiceCorrectionExecuted,
            summary, correlationId, _time.GetUtcNow().UtcDateTime, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Record("execute", correction.CorrectionType, correction.Status);
    }

    private Task WriteAuditAsync(CustomerInvoiceCorrection correction, Guid actorId, string action,
        string summary, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new AuditEventWriteRequest(correction.CompanyId, AuditActorTypes.User, actorId,
            action, AuditTargetTypes.CustomerInvoiceCorrection, correction.Id.ToString("N"),
            AuditEventOutcomes.Succeeded, summary,
            ["finance_invoice", "customer_invoice_correction", "approval", "accounting_posting"],
            new Dictionary<string, string?> { ["invoiceId"] = correction.InvoiceId.ToString("N"),
                ["correctionType"] = correction.CorrectionType, ["sourceHash"] = correction.SourceHash,
                ["approvalRequestId"] = correction.ApprovalRequestId?.ToString("N"),
                ["ledgerEntryId"] = correction.LedgerEntryId?.ToString("N") }, correlationId, now), cancellationToken);

    private static void Validate(Guid companyId, Guid targetId, Guid actorId, string idempotencyKey)
    {
        if (companyId == Guid.Empty || targetId == Guid.Empty || actorId == Guid.Empty)
            throw Error(CustomerInvoiceCorrectionReasonCodes.InvoiceNotFound, "Company, invoice, and actor are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 200)
            throw Error(CustomerInvoiceCorrectionReasonCodes.IdempotencyConflict, "A stable request identity is required.");
    }

    internal static async Task ReleaseAllocationsAsync(VirtualCompanyDbContext db,
        CustomerInvoiceCorrection correction, DateTime now, CancellationToken cancellationToken)
    {
        var allocations = await db.PaymentAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == correction.CompanyId && x.InvoiceId == correction.InvoiceId &&
                x.Payment.Status == PaymentStatuses.Completed && x.Payment.PaymentType == PaymentTypes.Incoming)
            .OrderBy(x => x.CreatedUtc).ToListAsync(cancellationToken);
        var allocationIds = allocations.Select(x => x.Id).ToArray();
        var alreadyReleased = await db.CustomerInvoiceCorrectionAllocationAdjustments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == correction.CompanyId && allocationIds.Contains(x.PaymentAllocationId))
            .GroupBy(x => x.PaymentAllocationId).Select(x => new { x.Key, Amount = x.Sum(y => y.ReleasedAmount) })
            .ToDictionaryAsync(x => x.Key, x => x.Amount, cancellationToken);
        var remaining = correction.Amount;
        foreach (var allocation in allocations)
        {
            var available = Math.Max(0m, allocation.AllocatedAmount - alreadyReleased.GetValueOrDefault(allocation.Id));
            var release = Math.Min(remaining, available);
            if (release <= 0m) continue;
            db.CustomerInvoiceCorrectionAllocationAdjustments.Add(new(Guid.NewGuid(), correction.CompanyId,
                correction.Id, allocation.Id, release, correction.Currency, now));
            remaining -= release;
            if (remaining == 0m) break;
        }
        if (remaining != 0m) throw Error(CustomerInvoiceCorrectionReasonCodes.RefundExceedsPaid,
            "The payment allocation changed before the refund could be reconciled.", true, correction.Version);
    }

    private Task ReleaseAllocationsAsync(CustomerInvoiceCorrection correction, DateTime now,
        CancellationToken cancellationToken) => ReleaseAllocationsAsync(_db, correction, now, cancellationToken);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static CustomerInvoiceCorrectionException Error(string code, string message, bool conflict = false,
        long? version = null) => new(code, message, conflict, version);
}

public sealed class CustomerInvoiceRefundExecutionRunner : ICustomerInvoiceRefundExecutionRunner
{
    private const int MaximumAttempts = 5;
    private readonly VirtualCompanyDbContext _db;
    private readonly IReadOnlyDictionary<string, ICustomerRefundExecutionProvider> _providers;
    private readonly TimeProvider _time;
    private readonly ILogger<CustomerInvoiceRefundExecutionRunner> _logger;
    private readonly CustomerInvoiceCorrectionTelemetry _telemetry;
    private readonly ICustomerInvoiceCorrectionPolicy _policy;
    private readonly IAuditEventWriter _audit;

    public CustomerInvoiceRefundExecutionRunner(VirtualCompanyDbContext db,
        IEnumerable<ICustomerRefundExecutionProvider> providers, TimeProvider time,
        ILogger<CustomerInvoiceRefundExecutionRunner> logger, CustomerInvoiceCorrectionTelemetry telemetry,
        ICustomerInvoiceCorrectionPolicy policy, IAuditEventWriter audit)
    {
        _db = db; _time = time; _logger = logger; _telemetry = telemetry; _policy = policy; _audit = audit;
        _providers = providers.GroupBy(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> RunBatchAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var stale = await _db.CustomerInvoiceRefundExecutions.IgnoreQueryFilters()
            .Include(x => x.Correction)
            .Where(x => x.Status == CustomerInvoiceRefundExecutionStatuses.Executing &&
                x.ClaimedUtc < now.AddMinutes(-2)).Take(20).ToListAsync(cancellationToken);
        foreach (var execution in stale)
        {
            execution.MarkReconciliationRequired("stale_execution_lease",
                "The refund worker stopped after claiming this payment. Confirm the provider outcome before retrying.",
                execution.ProviderReference, now);
            execution.Correction.MarkReconciliationRequired(
                CustomerInvoiceCorrectionReasonCodes.RefundReconciliationRequired,
                "The refund execution outcome is uncertain after an expired worker lease.", now);
        }
        if (stale.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        var ids = await _db.CustomerInvoiceRefundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == CustomerInvoiceRefundExecutionStatuses.Queued ||
                         x.Status == CustomerInvoiceRefundExecutionStatuses.RetryScheduled) && x.AvailableUtc <= now)
            .OrderBy(x => x.AvailableUtc).Take(20).Select(x => new { x.CompanyId, x.Id }).ToListAsync(cancellationToken);
        var processed = 0;
        foreach (var item in ids)
        {
            var execution = await _db.CustomerInvoiceRefundExecutions.IgnoreQueryFilters()
                .Include(x => x.Correction).ThenInclude(x => x.Invoice)
                .Include(x => x.Correction).ThenInclude(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == item.CompanyId && x.Id == item.Id, cancellationToken);
            if (execution is null || string.IsNullOrWhiteSpace(execution.ProviderKey) ||
                !_providers.TryGetValue(execution.ProviderKey, out var provider)) continue;
            var decision = await _policy.EvaluateAsync(new(execution.CompanyId, execution.Correction.InvoiceId,
                execution.Correction.CorrectionType, execution.Correction.Amount, execution.Correction.Currency,
                execution.ProviderKey, execution.CorrectionId), cancellationToken);
            if (!decision.IsAllowed ||
                !string.Equals(decision.SourceHash, execution.Correction.SourceHash, StringComparison.OrdinalIgnoreCase) ||
                !HasCurrentApproval(execution.Correction.ApprovalRequest, execution.Correction.SourceVersion,
                    execution.Correction.SourceHash, execution.Correction.PayloadHash))
            {
                const string summary = "The refund approval or its invoice, settlement, allocation, tax, or period source changed before provider execution. Repropose it from current facts.";
                execution.MarkFailed("approval_or_source_stale", summary, now);
                execution.Correction.MarkFailed(CustomerInvoiceCorrectionReasonCodes.ApprovalStale, summary, now);
                await WriteExecutionAuditAsync(execution, "blocked_stale", AuditEventOutcomes.Blocked,
                    summary, now, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                _telemetry.RecordRefund("blocked_stale", execution.ProviderKey);
                processed++;
                continue;
            }
            var token = Guid.NewGuid().ToString("N");
            if (!execution.TryClaim(token, now, TimeSpan.FromMinutes(2))) continue;
            await _db.SaveChangesAsync(cancellationToken);
            CustomerRefundExecutionResult result;
            try
            {
                result = await provider.ExecuteAsync(new(execution.CompanyId, execution.CorrectionId,
                    execution.Correction.InvoiceId, execution.Correction.Amount, execution.Correction.Currency,
                    execution.BeneficiaryReference, execution.PaymentEvidenceReference, execution.IdempotencyKey,
                    $"refund:{execution.CorrectionId:N}"), cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
            {
                result = new(CustomerRefundProviderOutcome.Ambiguous, null,
                    "The refund provider outcome is uncertain and requires reconciliation before any retry.");
            }
            now = _time.GetUtcNow().UtcDateTime;
            switch (result.Outcome)
            {
                case CustomerRefundProviderOutcome.Succeeded:
                    execution.MarkSucceeded(result.ProviderReference ?? "accepted", now);
                    await CustomerInvoiceCorrectionService.ReleaseAllocationsAsync(_db, execution.Correction, now, cancellationToken);
                    execution.Correction.MarkExecuted(execution.Correction.CreatedByUserId, now);
                    break;
                case CustomerRefundProviderOutcome.RetryableFailure when execution.AttemptCount < MaximumAttempts:
                    execution.ScheduleRetry("transient_provider_failure", result.SafeSummary,
                        now.AddSeconds(Math.Min(900, 30 * Math.Pow(2, execution.AttemptCount - 1))));
                    break;
                case CustomerRefundProviderOutcome.RetryableFailure:
                case CustomerRefundProviderOutcome.PermanentFailure:
                    execution.MarkFailed("provider_failure", result.SafeSummary, now);
                    execution.Correction.MarkFailed("customer_invoice_refund_failed", result.SafeSummary, now);
                    break;
                case CustomerRefundProviderOutcome.Ambiguous:
                    execution.MarkReconciliationRequired("ambiguous_provider_outcome", result.SafeSummary,
                        result.ProviderReference, now);
                    execution.Correction.MarkReconciliationRequired(
                        CustomerInvoiceCorrectionReasonCodes.RefundReconciliationRequired, result.SafeSummary, now);
                    break;
            }
            await WriteExecutionAuditAsync(execution, result.Outcome.ToString().ToLowerInvariant(),
                result.Outcome == CustomerRefundProviderOutcome.Succeeded ? AuditEventOutcomes.Succeeded :
                result.Outcome == CustomerRefundProviderOutcome.Ambiguous ? AuditEventOutcomes.Blocked : AuditEventOutcomes.Failed,
                result.SafeSummary, now, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Processed customer refund execution {ExecutionId} with outcome {Outcome}.", execution.Id, result.Outcome);
            _telemetry.RecordRefund(result.Outcome.ToString().ToLowerInvariant(), execution.ProviderKey);
            processed++;
        }
        return processed;
    }

    private Task WriteExecutionAuditAsync(CustomerInvoiceRefundExecution execution, string providerOutcome,
        string auditOutcome, string summary, DateTime now, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new AuditEventWriteRequest(execution.CompanyId, AuditActorTypes.System, null,
            AuditEventActions.AccountingCustomerInvoiceRefundExecutionUpdated,
            AuditTargetTypes.CustomerInvoiceCorrection, execution.CorrectionId.ToString("N"), auditOutcome,
            summary, ["customer_invoice_correction", "payment_allocation", "refund_provider"],
            new Dictionary<string, string?>
            {
                ["refundExecutionId"] = execution.Id.ToString("N"),
                ["providerKey"] = execution.ProviderKey,
                ["providerOutcome"] = providerOutcome,
                ["providerReference"] = execution.ProviderReference,
                ["sourceHash"] = execution.Correction.SourceHash
            }, $"refund:{execution.CorrectionId:N}", now), cancellationToken);

    internal static bool HasCurrentApproval(ApprovalRequest? approvalRequest, string sourceVersion,
        string sourceHash, string payloadHash)
    {
        if (approvalRequest?.Status != ApprovalRequestStatus.Approved) return false;
        var context = approvalRequest.ThresholdContext;
        var approvedSourceVersion = context.TryGetValue("sourceVersion", out var versionNode) ? versionNode?.ToString().Trim('"') : null;
        var approvedSourceHash = context.TryGetValue("sourceHash", out var sourceNode) ? sourceNode?.ToString().Trim('"') : null;
        var approvedPayloadHash = context.TryGetValue("payloadHash", out var payloadNode) ? payloadNode?.ToString().Trim('"') : null;
        return string.Equals(approvedSourceVersion, sourceVersion, StringComparison.Ordinal) &&
            string.Equals(approvedSourceHash, sourceHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(approvedPayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CustomerInvoiceRefundExecutionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CustomerInvoiceRefundExecutionBackgroundService> _logger;
    public CustomerInvoiceRefundExecutionBackgroundService(IServiceScopeFactory scopes,
        ILogger<CustomerInvoiceRefundExecutionBackgroundService> logger)
    {
        _scopes = scopes; _logger = logger;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ICustomerInvoiceRefundExecutionRunner>()
                    .RunBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Customer refund background execution failed; the durable queue will be retried.");
            }
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
