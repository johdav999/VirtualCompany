using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceBillInboxService : IFinanceBillInboxService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly TimeProvider _timeProvider;

    public CompanyFinanceBillInboxService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<IReadOnlyList<FinanceBillInboxRowDto>> GetInboxAsync(GetFinanceBillInboxQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = Math.Clamp(query.Limit <= 0 ? 100 : query.Limit, 1, 500);

        var rows = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .GroupJoin(
                _dbContext.FinanceBillReviewStates.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == query.CompanyId),
                bill => bill.Id,
                state => state.DetectedBillId,
                (bill, states) => new { Bill = bill, State = states.FirstOrDefault() })
            .OrderByDescending(x => x.Bill.UpdatedUtc)
            .ThenByDescending(x => x.Bill.CreatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(x =>
            {
                var status = ResolveInboxStatus(x.Bill, x.State);
                return new FinanceBillInboxRowDto(
                    x.Bill.Id,
                    x.Bill.SupplierName ?? "Unknown supplier",
                    x.Bill.InvoiceNumber ?? x.Bill.SourceAttachmentId ?? x.Bill.Id.ToString("D"),
                    x.Bill.TotalAmount,
                    x.Bill.Currency,
                    x.Bill.CreatedUtc,
                    FormatBillStatus(status),
                    FormatStatus(x.Bill.ConfidenceLevel),
                    CountValidationWarnings(x.Bill),
                    x.Bill.DuplicateCheck?.IsDuplicate == true ? 1 : 0);
            })
            .Where(x => IsAllowedDisplayStatus(x.Status))
            .ToList();
    }

    public async Task<FinanceBillInboxDetailDto?> GetDetailAsync(GetFinanceBillInboxDetailQuery query, CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var bill = await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.DuplicateCheck)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken);

        if (bill is null)
        {
            return null;
        }

        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Actions)
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.DetectedBillId == query.BillId, cancellationToken);

        var validationWarnings = ParseValidationWarnings(bill);
        var duplicateWarnings = BuildDuplicateWarnings(bill);
        var hasUnresolvedValidationFailures = HasUnresolvedValidationFailures(bill, validationWarnings);
        var status = ResolveInboxStatus(bill, state);
        var proposalSummary = BuildProposalSummary(bill, validationWarnings, duplicateWarnings, state?.ProposalSummary);

        return new FinanceBillInboxDetailDto(
            bill.Id,
            bill.SupplierName ?? "Unknown supplier",
            bill.SupplierOrgNumber,
            bill.InvoiceNumber ?? bill.SourceAttachmentId ?? bill.Id.ToString("D"),
            bill.InvoiceDateUtc,
            bill.DueDateUtc,
            bill.TotalAmount,
            bill.VatAmount,
            bill.Currency,
            FormatBillStatus(status),
            bill.Confidence,
            FormatStatus(bill.ConfidenceLevel),
            bill.Fields.OrderBy(x => x.FieldName).Select(MapField).ToList(),
            validationWarnings,
            duplicateWarnings,
            proposalSummary,
            state?.Actions.OrderByDescending(x => x.OccurredUtc).Select(MapAction).ToList() ?? [],
            !hasUnresolvedValidationFailures && !string.Equals(status, FinanceBillInboxStatuses.Approved, StringComparison.OrdinalIgnoreCase),
            hasUnresolvedValidationFailures ? "Resolve validation failures before approving this bill." : null);
    }

    public async Task<FinanceBillReviewActionResultDto> ApproveAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var state = await LoadOrCreateStateAsync(bill, cancellationToken);
        var validationWarnings = ParseValidationWarnings(bill);
        var hasFailures = HasUnresolvedValidationFailures(bill, validationWarnings);
        var occurredUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var action = state.Approve(command.ActorUserId, command.ActorDisplayName, command.Rationale, occurredUtc, hasFailures);

        _dbContext.BillApprovalProposals.Add(new BillApprovalProposal(
            Guid.NewGuid(),
            command.CompanyId,
            bill.Id,
            state.Id,
            BuildProposalSummary(bill, validationWarnings, BuildDuplicateWarnings(bill), state.ProposalSummary).Summary,
            command.ActorUserId,
            occurredUtc));

        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.bill_inbox.approved", bill.Id, AuditEventOutcomes.Approved, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(bill.Id, FormatStatus(action.PriorStatus), FormatStatus(action.NewStatus), action.OccurredUtc);
    }

    public async Task<FinanceBillReviewActionResultDto> RejectAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var state = await LoadOrCreateStateAsync(bill, cancellationToken);
        var action = state.Reject(command.ActorUserId, command.ActorDisplayName, command.Rationale, _timeProvider.GetUtcNow().UtcDateTime);

        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.bill_inbox.rejected", bill.Id, AuditEventOutcomes.Rejected, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(bill.Id, FormatStatus(action.PriorStatus), FormatStatus(action.NewStatus), action.OccurredUtc);
    }

    public async Task<FinanceBillReviewActionResultDto> RequestClarificationAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillForWriteAsync(command.CompanyId, command.BillId, cancellationToken);
        var state = await LoadOrCreateStateAsync(bill, cancellationToken);
        var action = state.RequestClarification(command.ActorUserId, command.ActorDisplayName, command.Rationale, _timeProvider.GetUtcNow().UtcDateTime);

        await WriteAuditAsync(command.CompanyId, command.ActorUserId, "finance.bill_inbox.clarification_requested", bill.Id, AuditEventOutcomes.Requested, action, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new FinanceBillReviewActionResultDto(bill.Id, FormatStatus(action.PriorStatus), FormatStatus(action.NewStatus), action.OccurredUtc);
    }

    private async Task<DetectedBill> LoadBillForWriteAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.DetectedBills
            .IgnoreQueryFilters()
            .Include(x => x.DuplicateCheck)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new InvalidOperationException("The selected finance bill was not found in the active company.");

    private async Task<FinanceBillReviewState> LoadOrCreateStateAsync(DetectedBill bill, CancellationToken cancellationToken)
    {
        var state = await _dbContext.FinanceBillReviewStates
            .IgnoreQueryFilters()
            .Include(x => x.Actions)
            .SingleOrDefaultAsync(x => x.CompanyId == bill.CompanyId && x.DetectedBillId == bill.Id, cancellationToken);

        if (state is not null)
        {
            return state;
        }

        state = new FinanceBillReviewState(
            Guid.NewGuid(),
            bill.CompanyId,
            bill.Id,
            ResolveInboxStatus(bill, null),
            BuildProposalSummary(bill, ParseValidationWarnings(bill), BuildDuplicateWarnings(bill), null).Summary,
            _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.FinanceBillReviewStates.Add(state);
        return state;
    }

    private async Task WriteAuditAsync(Guid companyId, Guid? actorUserId, string actionName, Guid billId, string outcome, FinanceBillReviewAction action, CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                actorUserId,
                actionName,
                "finance_bill_inbox_item",
                billId.ToString("D"),
                outcome,
                action.Rationale,
                Metadata: new Dictionary<string, string?>
                {
                    ["priorStatus"] = FormatBillStatus(action.PriorStatus),
                    ["newStatus"] = FormatBillStatus(action.NewStatus),
                    ["reviewActionId"] = action.Id.ToString("D")
                },
                OccurredUtc: action.OccurredUtc),
            cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested finance bill inbox is outside the active company context.");
        }
    }

    private static string ResolveInboxStatus(DetectedBill bill, FinanceBillReviewState? state)
    {
        if (state is not null)
        {
            return state.Status;
        }

        if (bill.ValidationStatusPersisted && bill.IsEligibleForApprovalProposal)
        {
            return FinanceBillInboxStatuses.ProposedForApproval;
        }

        if (bill.RequiresReview || string.Equals(bill.ValidationStatus, "flagged", StringComparison.OrdinalIgnoreCase))
        {
            return FinanceBillInboxStatuses.NeedsReview;
        }

        return bill.Fields.Count > 0 ? FinanceBillInboxStatuses.Extracted : FinanceBillInboxStatuses.Detected;
    }

    private static FinanceBillProposalSummaryDto BuildProposalSummary(
        DetectedBill bill,
        IReadOnlyList<FinanceBillWarningDto> validationWarnings,
        IReadOnlyList<FinanceBillWarningDto> duplicateWarnings,
        string? storedSummary)
    {
        var reference = bill.InvoiceNumber ?? bill.SourceAttachmentId ?? bill.Id.ToString("D");
        var amount = bill.TotalAmount.HasValue && !string.IsNullOrWhiteSpace(bill.Currency)
            ? $"{bill.TotalAmount.Value:0.##} {bill.Currency}"
            : "the extracted amount";
        var due = bill.DueDateUtc.HasValue ? $" due {bill.DueDateUtc.Value:yyyy-MM-dd}" : string.Empty;
        var supplier = bill.SupplierName ?? "the supplier";
        var unresolvedWarnings = validationWarnings.Concat(duplicateWarnings)
            .Where(x => !x.IsResolved)
            .Select(x => $"{x.Severity}: {x.Message}")
            .Take(5)
            .ToList();
        var riskFlags = unresolvedWarnings.Count == 0
            ? ["No unresolved validation or duplicate warnings were found."]
            : unresolvedWarnings;
        var recommendedAction = unresolvedWarnings.Count == 0
            ? "Approve the bill proposal if the extracted details match your source records."
            : "Request clarification or reject the proposal until the unresolved warnings are addressed.";
        var approvalAsk = "Please approve, reject, or request clarification for this bill proposal. Approval records the decision only and does not initiate payment or export.";
        var generatedSummary = $"FinanceAgent proposal: bill {reference} from {supplier} is for {amount}{due}. " +
            $"Confidence is {FormatStatus(bill.ConfidenceLevel)}. {string.Join(" ", riskFlags)} {recommendedAction} {approvalAsk}";
        var summary = NormalizeProposalSummary(storedSummary, generatedSummary, approvalAsk);

        return new FinanceBillProposalSummaryDto(
            $"Proposal for {reference}",
            summary,
            riskFlags,
            approvalAsk,
            recommendedAction,
            true,
            false);
    }

    private static string NormalizeProposalSummary(string? storedSummary, string generatedSummary, string approvalAsk)
    {
        if (string.IsNullOrWhiteSpace(storedSummary) || ContainsAutoPaymentLanguage(storedSummary))
        {
            return generatedSummary;
        }

        var summary = storedSummary.Trim();
        return summary.Contains("approve", StringComparison.OrdinalIgnoreCase) &&
               summary.Contains("does not initiate payment", StringComparison.OrdinalIgnoreCase)
            ? summary
            : $"{summary} {approvalAsk}";
    }

    private static bool ContainsAutoPaymentLanguage(string value)
    {
        var normalized = value.ToLowerInvariant();
        return normalized.Contains("payment was initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment has been initiated", StringComparison.Ordinal) ||
               normalized.Contains("payment will be initiated", StringComparison.Ordinal) ||
               normalized.Contains("automatically pay", StringComparison.Ordinal) ||
               normalized.Contains("exported for payment", StringComparison.Ordinal);
    }

    private static FinanceBillExtractedFieldDto MapField(DetectedBillField field) =>
        new(
            field.FieldName,
            FormatFieldName(field.FieldName),
            field.RawValue,
            field.NormalizedValue,
            field.FieldConfidence,
            [
                new FinanceBillEvidenceReferenceDto(
                    field.SourceDocument,
                    field.SourceDocumentType,
                    field.PageReference,
                    field.SectionReference,
                    field.TextSpan,
                    field.Locator,
                    field.Snippet)
            ]);

    private static FinanceBillReviewActionDto MapAction(FinanceBillReviewAction action) =>
        new(
            action.Id,
            FormatStatus(action.Action),
            action.ActorDisplayName,
            action.ActorUserId,
            action.OccurredUtc,
            FormatBillStatus(action.PriorStatus),
            FormatBillStatus(action.NewStatus),
            action.Rationale);

    private static IReadOnlyList<FinanceBillWarningDto> ParseValidationWarnings(DetectedBill bill)
    {
        if (string.IsNullOrWhiteSpace(bill.ValidationIssuesJson) || bill.ValidationIssuesJson == "[]")
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(bill.ValidationIssuesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [new FinanceBillWarningDto("validation_payload", bill.ValidationStatus, bill.ValidationIssuesJson, false)];
            }

            return document.RootElement.EnumerateArray()
                .Select((item, index) =>
                {
                    var code = TryGetString(item, "code") ?? $"validation_{index + 1}";
                    var severity = TryGetString(item, "severity") ?? bill.ValidationStatus;
                    var message = TryGetString(item, "message") ?? item.ToString();
                    var resolved = item.TryGetProperty("isResolved", out var resolvedProperty) && resolvedProperty.ValueKind == JsonValueKind.True;
                    return new FinanceBillWarningDto(code, FormatStatus(severity), message, resolved);
                })
                .ToList();
        }
        catch (JsonException)
        {
            return [new FinanceBillWarningDto("validation_payload", FormatStatus(bill.ValidationStatus), bill.ValidationIssuesJson, false)];
        }
    }

    private static IReadOnlyList<FinanceBillWarningDto> BuildDuplicateWarnings(DetectedBill bill)
    {
        if (bill.DuplicateCheck is null || !bill.DuplicateCheck.IsDuplicate)
        {
            return [];
        }

        return
        [
            new FinanceBillWarningDto(
                "possible_duplicate",
                FormatStatus(bill.DuplicateCheck.ResultStatus),
                bill.DuplicateCheck.CriteriaSummary,
                false)
        ];
    }

    private static bool HasUnresolvedValidationFailures(DetectedBill bill, IReadOnlyList<FinanceBillWarningDto> warnings)
    {
        if (warnings.Any(x => !x.IsResolved))
        {
            return true;
        }

        return string.Equals(bill.ValidationStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bill.ValidationStatus, "flagged", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bill.ValidationStatus, "rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountValidationWarnings(DetectedBill bill) => ParseValidationWarnings(bill).Count(x => !x.IsResolved);

    private static string? TryGetString(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string FormatFieldName(string value) => FormatStatus(value.Replace("Utc", string.Empty, StringComparison.Ordinal));

    private static string FormatStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => char.ToUpperInvariant(x[0]) + x[1..]));

    private static string FormatBillStatus(string value) =>
        FinanceBillInboxStatuses.Normalize(value) switch
        {
            FinanceBillInboxStatuses.Detected => "Detected",
            FinanceBillInboxStatuses.Extracted => "Extracted",
            FinanceBillInboxStatuses.NeedsReview => "Needs review",
            FinanceBillInboxStatuses.ProposedForApproval => "Proposed for approval",
            FinanceBillInboxStatuses.Approved => "Approved",
            FinanceBillInboxStatuses.Rejected => "Rejected",
            FinanceBillInboxStatuses.SentToPaymentExported => "Sent to payment/exported",
            _ => FormatStatus(value)
        };

    private static bool IsAllowedDisplayStatus(string value) =>
        new[] { "Detected", "Extracted", "Needs review", "Proposed for approval", "Approved", "Rejected", "Sent to payment/exported" }
            .Contains(value, StringComparer.OrdinalIgnoreCase);
}