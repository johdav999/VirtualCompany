using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAccountingDraftAgentService(
    IManualJournalService manualJournals,
    IAdvancedReconciliationCommandService reconciliationCommands,
    IFinanceAgentDecisionService financeDecisions,
    IAccountingJournalReadService journals,
    IBankTransactionReadService bankTransactions,
    IFinancePaymentReadService paymentReads,
    IFinanceReadService financeReads) : IFinanceAccountingDraftAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] EditableFields =
    [
        "explanation", "description", "postingDate", "voucherSeriesCode", "financeAccountId",
        "dimensionMemberIds", "evidenceDocumentIds"
    ];

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!FinanceAccountingDraftAgentToolIds.Contains(request.ToolName))
            return Failed("unsupported_accounting_draft_tool", "This accounting draft tool is not available.");
        if (request.CompanyId == Guid.Empty || request.AgentId == Guid.Empty || request.ExecutionId == Guid.Empty)
            return Failed("accounting_draft_context_required", "Company, agent, and execution context are required.");
        if (!request.ActorUserId.HasValue || request.ActorUserId == Guid.Empty)
            return Failed("accounting_draft_actor_required", "A current Finance reviewer identity is required.");

        return request.ToolName switch
        {
            FinanceAccountingDraftAgentToolIds.CreateManualJournalDraft =>
                await CreateJournalAsync(request, "manual_journal", false, false, cancellationToken),
            FinanceAccountingDraftAgentToolIds.CreateCorrectionDraft =>
                await CreateJournalAsync(request, "correction_or_reversal", true, false, cancellationToken),
            FinanceAccountingDraftAgentToolIds.CreateAccountingTreatmentDraft =>
                await CreateJournalAsync(request, "accounting_treatment", false, true, cancellationToken),
            FinanceAccountingDraftAgentToolIds.CreateReconciliationDecisionDraft =>
                await CreateReconciliationAsync(request, cancellationToken),
            FinanceAccountingDraftAgentToolIds.SubmitForApproval =>
                await SubmitAsync(request, cancellationToken),
            _ => Failed("unsupported_accounting_draft_tool", "This accounting draft tool is not available.")
        };
    }

    private async Task<InternalToolExecutionResponse> CreateJournalAsync(
        InternalToolExecutionRequest request,
        string draftKind,
        bool requiresCorrection,
        bool accountingTreatment,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure)) return actionFailure;
        var input = RequiredObject<ManualJournalDraftInput>(request, "draft");
        var idempotencyKey = RequiredText(request, "idempotencyKey", 200);
        var rationale = RequiredText(request, "rationale", 1000);
        ValidateInputBounds(input);
        if (requiresCorrection && (!input.OriginalLedgerEntryId.HasValue || string.IsNullOrWhiteSpace(input.CorrectionReason)))
            return Failed("correction_source_required", "A correction draft must identify the original journal and explain the governed correction or reversal.");
        if (requiresCorrection && !(input.SourceRecords ?? []).Any(source =>
                source.RecordId == input.OriginalLedgerEntryId &&
                string.Equals(source.SourceType, FinanceAccountingDraftSourceTypes.LedgerJournal, StringComparison.OrdinalIgnoreCase)))
            return Failed("correction_source_required", "A correction draft must retain the current version of its original journal as source evidence.");
        if (!requiresCorrection && input.OriginalLedgerEntryId.HasValue)
            return Failed("correction_tool_required", "Use the correction or reversal draft tool when original accounting is referenced.");

        var sourceIssue = await ValidateSourceRecordsAsync(request.CompanyId, input.SourceRecords, cancellationToken);
        if (sourceIssue is not null) return sourceIssue;

        FinanceAccountingTreatmentResult? treatment = null;
        if (accountingTreatment)
        {
            var billId = RequiredGuid(request, "billId");
            var selectedAccountId = RequiredGuid(request, "selectedAccountId");
            treatment = await financeDecisions.RecommendAccountingTreatmentAsync(request.CompanyId, request.AgentId,
                request.ActorUserId, new(billId, IsCorrection: false, Objective: rationale), cancellationToken);
            if (!treatment.Candidates.Any(x => x.AccountId == selectedAccountId) ||
                !input.Lines.Any(x => x.FinanceAccountId == selectedAccountId))
                return Failed("accounting_treatment_selection_ineligible",
                    "The selected account is not an eligible deterministic candidate for this source bill and draft.");
            if (!(input.SourceRecords ?? []).Any(x => x.RecordId == billId &&
                    string.Equals(x.SourceType, FinanceAccountingDraftSourceTypes.Bill, StringComparison.OrdinalIgnoreCase)))
                return Failed("accounting_treatment_source_required", "The source bill and its current version must be retained on the draft.");
        }

        ManualJournalDraftDto created = requiresCorrection
            ? await manualJournals.CreateAdjustmentAsync(new(request.CompanyId, input.OriginalLedgerEntryId!.Value,
                input, idempotencyKey, request.ActorUserId!.Value, Correlation(request)), cancellationToken)
            : await manualJournals.CreateAsync(new(request.CompanyId, input, idempotencyKey,
                request.ActorUserId!.Value, Correlation(request)), cancellationToken);
        var preview = await manualJournals.PreviewAsync(new(request.CompanyId, created.Id, created.Version,
            request.ActorUserId.Value), cancellationToken);
        var modelFields = OptionalStrings(request, "modelProposedFields", 25, 80);
        var missingEvidence = preview.Policy.Issues
            .Where(x => x.ReasonCode.Contains("evidence", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Explanation).Distinct(StringComparer.Ordinal).ToArray();
        var blocked = !preview.Policy.IsAllowed || !preview.PostingPreview.IsValid || created.Difference != 0m;
        var result = new FinanceAccountingDraftResultDto(draftKind, preview.Draft, preview.Policy,
            preview.PostingPreview, modelFields, EditableFields, missingEvidence,
            blocked ? ["edit_draft", "add_evidence", "refresh_sources"] : ["review_draft", "submit_for_approval"],
            blocked, FinanceAccountingDraftAgentContract.AuthorityNotice);
        var data = new Dictionary<string, JsonNode?> { ["accountingDraft"] = Serialize(result) };
        if (treatment is not null) data["accountingTreatment"] = Serialize(treatment);
        return Success(request, "A current, unposted accounting draft was created and validated.", data,
            blocked ? "review_required" : "draft_ready_for_review");
    }

    private async Task<InternalToolExecutionResponse> CreateReconciliationAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure)) return actionFailure;
        var input = RequiredObject<ReconciliationDraftInput>(request, "draft");
        if (input.Nodes.Count > FinanceAccountingDraftAgentContract.MaximumEvidenceRecords ||
            input.Edges.Count > FinanceAccountingDraftAgentContract.MaximumEvidenceRecords)
            return Failed("reconciliation_draft_too_large", "Reconciliation decision drafts are limited to 100 nodes and 100 edges.");
        var key = RequiredText(request, "idempotencyKey", 200);
        var rationale = RequiredText(request, "rationale", 1000);
        var sources = RequiredObject<ManualJournalSourceReferenceInput[]>(request, "sourceRecords");
        var sourceIssue = await ValidateSourceRecordsAsync(request.CompanyId, sources, cancellationToken);
        if (sourceIssue is not null) return sourceIssue;
        if (input.Nodes.Where(node => node.RecordId.HasValue).Any(node =>
                !sources.Any(source => source.RecordId == node.RecordId && SourceMatches(node.NodeType, source.SourceType))))
            return Failed("reconciliation_source_required",
                "Every record-backed reconciliation node must retain its current source record version.");
        var result = await reconciliationCommands.CreateGroupAsync(new(request.CompanyId, input.Reference,
            input.Counterparty, input.Currency, input.RuleVersion, input.CorrectionOfGroupId, input.Nodes, input.Edges,
            request.ActorUserId!.Value, Correlation(request), key), cancellationToken);
        return Success(request, "A proposed reconciliation decision was created without applying a match.",
            new() { ["reconciliationDraft"] = Serialize(new FinanceReconciliationDecisionDraftResultDto(result,
                rationale, sources.Select(source => new ManualJournalSourceReferenceDto(
                    source.SourceType, source.RecordId, source.SourceVersion)).ToArray(),
                ["review_reconciliation_draft", "open_reconciliation_workspace"], false,
                FinanceAccountingDraftAgentContract.AuthorityNotice)) }, "draft_ready_for_review");
    }

    private static bool SourceMatches(string nodeType, string sourceType) =>
        string.Equals(nodeType.Trim(), sourceType.Trim(), StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(nodeType.Trim(), "bank_transaction", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(sourceType.Trim(), FinanceAccountingDraftSourceTypes.BankTransaction, StringComparison.OrdinalIgnoreCase));

    private async Task<InternalToolExecutionResponse> SubmitAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure)) return actionFailure;
        if (!RequiredBool(request, "reviewed"))
            return Failed("accounting_draft_review_required", "A reviewer must explicitly confirm the current draft before submission.");
        var draftId = RequiredGuid(request, "draftId");
        var expectedVersion = RequiredLong(request, "expectedVersion");
        var expectedPayloadHash = RequiredText(request, "expectedPayloadHash", 64);
        var idempotencyKey = RequiredText(request, "idempotencyKey", 200);
        var current = await manualJournals.GetAsync(new(request.CompanyId, draftId), cancellationToken);
        if (current.Version != expectedVersion || !string.Equals(current.PayloadHash, expectedPayloadHash, StringComparison.OrdinalIgnoreCase))
            return Failed("accounting_draft_stale", "The draft changed after review. Reload and review the current version before submission.");
        var sourceIssue = await ValidateSourceRecordsAsync(request.CompanyId,
            current.SourceRecords?.Select(x => new ManualJournalSourceReferenceInput(x.SourceType, x.RecordId, x.SourceVersion)).ToArray(),
            cancellationToken);
        if (sourceIssue is not null) return sourceIssue;
        var preview = await manualJournals.PreviewAsync(new(request.CompanyId, draftId, expectedVersion,
            request.ActorUserId!.Value), cancellationToken);
        if (!preview.Policy.IsAllowed || !preview.PostingPreview.IsValid || preview.Draft.Difference != 0m)
            return Failed("accounting_draft_validation_blocked",
                "The draft still has deterministic validation blockers and cannot enter approval.",
                new() { ["validation"] = Serialize(preview) });
        var submission = await manualJournals.SubmitAsync(new(request.CompanyId, draftId, expectedVersion,
            idempotencyKey, request.ActorUserId.Value, Correlation(request)), cancellationToken);
        return Success(request, "The reviewed draft entered the existing approval workflow; no journal was posted.",
            new() { ["accountingDraftSubmission"] = Serialize(new FinanceAccountingDraftSubmissionDto(submission,
                expectedPayloadHash, current.SourceRecords ?? [], ["review_approval", "open_manual_journal"], false,
                FinanceAccountingDraftAgentContract.AuthorityNotice)) }, "awaiting_approval");
    }

    private async Task<InternalToolExecutionResponse?> ValidateSourceRecordsAsync(
        Guid companyId,
        IReadOnlyList<ManualJournalSourceReferenceInput>? sources,
        CancellationToken cancellationToken)
    {
        if (sources is not { Count: > 0 })
            return Failed("accounting_draft_source_required", "At least one source record and current source version are required.");
        if (sources.Count > FinanceAccountingDraftAgentContract.MaximumEvidenceRecords)
            return Failed("accounting_draft_sources_too_large", "Accounting drafts support at most 100 source records.");
        foreach (var source in sources)
        {
            if (source.RecordId == Guid.Empty || string.IsNullOrWhiteSpace(source.SourceVersion) ||
                !FinanceAccountingDraftSourceTypes.Supported.Contains(source.SourceType))
                return Failed("accounting_draft_source_invalid", "Every source requires a supported type, record identity, and version.");
            var currentVersions = await CurrentVersionsAsync(companyId, source.SourceType, source.RecordId, cancellationToken);
            if (currentVersions.Count == 0)
                return Failed("accounting_draft_source_unavailable", "A source record is unavailable in the active company.");
            if (!currentVersions.Contains(source.SourceVersion.Trim(), StringComparer.OrdinalIgnoreCase))
                return Failed("accounting_draft_source_stale", "A source record changed after the draft evidence was read. Refresh the evidence before continuing.");
        }
        return null;
    }

    private async Task<IReadOnlyList<string>> CurrentVersionsAsync(Guid companyId, string sourceType, Guid recordId,
        CancellationToken cancellationToken)
    {
        object? value;
        var aliases = new List<string>();
        switch (sourceType.Trim().ToLowerInvariant())
        {
            case FinanceAccountingDraftSourceTypes.LedgerJournal:
                var journal = await journals.GetAsync(new(companyId, recordId), cancellationToken);
                value = journal;
                if (!string.IsNullOrWhiteSpace(journal.SourceVersion)) aliases.Add(journal.SourceVersion);
                break;
            case FinanceAccountingDraftSourceTypes.BankTransaction:
                value = await bankTransactions.GetDetailAsync(new(companyId, recordId), cancellationToken);
                break;
            case FinanceAccountingDraftSourceTypes.Payment:
                var payment = await paymentReads.GetPaymentDetailAsync(new(companyId, recordId), cancellationToken);
                value = payment;
                if (payment is not null) aliases.Add(payment.UpdatedUtc.Ticks.ToString());
                break;
            case FinanceAccountingDraftSourceTypes.Invoice:
                value = await financeReads.GetInvoiceDetailAsync(new(companyId, recordId), cancellationToken);
                break;
            case FinanceAccountingDraftSourceTypes.Bill:
                value = await financeReads.GetBillDetailAsync(new(companyId, recordId), cancellationToken);
                break;
            default:
                return [];
        }
        if (value is null) return [];
        aliases.Add("sha256:" + Hash(JsonSerializer.Serialize(value, JsonOptions)));
        return aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidateInputBounds(ManualJournalDraftInput input)
    {
        if (input.Lines is not { Count: > 0 } || input.Lines.Count > FinanceAccountingDraftAgentContract.MaximumLines)
            throw new ArgumentException("Accounting drafts require between 1 and 100 lines.");
        if (input.EvidenceDocumentIds.Count > FinanceAccountingDraftAgentContract.MaximumEvidenceRecords)
            throw new ArgumentException("Accounting drafts support at most 100 evidence documents.");
    }

    private static bool EnsureAction(InternalToolExecutionRequest request, ToolActionType expected,
        out InternalToolExecutionResponse failure)
    {
        if (request.Context.ActionType == expected) { failure = null!; return true; }
        failure = Failed("unsupported_action_type", $"The {request.ToolName} tool does not support the requested action type.");
        return false;
    }

    private static InternalToolExecutionResponse Success(InternalToolExecutionRequest request, string summary,
        Dictionary<string, JsonNode?> data, string state) => InternalToolExecutionResponse.Succeeded(summary, data,
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceAccountingDraftAgentContract.Version),
            ["companyId"] = JsonValue.Create(request.CompanyId),
            ["agentId"] = JsonValue.Create(request.AgentId),
            ["executionId"] = JsonValue.Create(request.ExecutionId),
            ["taskId"] = request.TaskId.HasValue ? JsonValue.Create(request.TaskId.Value) : null,
            ["workflowInstanceId"] = request.WorkflowInstanceId.HasValue ? JsonValue.Create(request.WorkflowInstanceId.Value) : null,
            ["correlationId"] = JsonValue.Create(Correlation(request)),
            ["state"] = JsonValue.Create(state),
            ["posted"] = JsonValue.Create(false),
            ["reconciliationApplied"] = JsonValue.Create(false),
            ["authorityNotice"] = JsonValue.Create(FinanceAccountingDraftAgentContract.AuthorityNotice)
        });

    private static InternalToolExecutionResponse Failed(string code, string summary,
        Dictionary<string, JsonNode?>? data = null) => InternalToolExecutionResponse.Failed("failed", code, summary, data);
    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, JsonOptions);
    private static string Correlation(InternalToolExecutionRequest request) =>
        request.CorrelationId ?? request.ExecutionId.ToString("N");
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static T RequiredObject<T>(InternalToolExecutionRequest request, string name) =>
        request.Payload.TryGetValue(name, out var node) && node is not null
            ? node.Deserialize<T>(JsonOptions) ?? throw new ArgumentException($"{name} is required.")
            : throw new ArgumentException($"{name} is required.");
    private static string RequiredText(InternalToolExecutionRequest request, string name, int max)
    {
        if (!request.Payload.TryGetValue(name, out var node) || node is not JsonValue value ||
            !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Trim().Length > max)
            throw new ArgumentException($"{name} is required and must be {max} characters or fewer.");
        return text.Trim();
    }
    private static Guid RequiredGuid(InternalToolExecutionRequest request, string name)
    {
        if (request.Payload.TryGetValue(name, out var node) && node is JsonValue value &&
            ((value.TryGetValue<Guid>(out var id) && id != Guid.Empty) ||
             (value.TryGetValue<string>(out var text) && Guid.TryParse(text, out id) && id != Guid.Empty))) return id;
        throw new ArgumentException($"{name} is required.");
    }
    private static long RequiredLong(InternalToolExecutionRequest request, string name)
    {
        if (request.Payload.TryGetValue(name, out var node) && node is JsonValue value &&
            ((value.TryGetValue<long>(out var number) && number > 0) ||
             (value.TryGetValue<string>(out var text) && long.TryParse(text, out number) && number > 0))) return number;
        throw new ArgumentException($"{name} is required.");
    }
    private static bool RequiredBool(InternalToolExecutionRequest request, string name) =>
        request.Payload.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var result) && result;
    private static IReadOnlyList<string> OptionalStrings(InternalToolExecutionRequest request, string name, int maxItems, int maxLength) =>
        request.Payload.TryGetValue(name, out var node) && node is JsonArray array
            ? array.OfType<JsonValue>().Select(x => x.TryGetValue<string>(out var value) ? value?.Trim() : null)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x!.Length <= maxLength).Take(maxItems).Cast<string>().ToArray()
            : [];

    private sealed record ReconciliationDraftInput(
        string Reference,
        string Counterparty,
        string Currency,
        int? RuleVersion,
        Guid? CorrectionOfGroupId,
        IReadOnlyList<AdvancedReconciliationNodeInputDto> Nodes,
        IReadOnlyList<AdvancedReconciliationEdgeInputDto> Edges);
}
