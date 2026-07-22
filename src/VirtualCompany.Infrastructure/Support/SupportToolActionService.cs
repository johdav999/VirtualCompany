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
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportToolActionService : ISupportToolActionService
{
    private readonly ISupportCaseService _cases;
    private readonly ISupportTriageService _triage;
    private readonly ISupportReplyDraftService _drafts;
    private readonly ISupportRefundWorkflowService _refunds;
    private readonly ISupportKnowledgeGapService _knowledgeGaps;
    private readonly IAuditEventWriter _audit;

    public SupportToolActionService(
        ISupportCaseService cases,
        ISupportTriageService triage,
        ISupportReplyDraftService drafts,
        ISupportRefundWorkflowService refunds,
        ISupportKnowledgeGapService knowledgeGaps,
        IAuditEventWriter audit)
    {
        _cases = cases;
        _triage = triage;
        _drafts = drafts;
        _refunds = refunds;
        _knowledgeGaps = knowledgeGaps;
        _audit = audit;
    }

    public async Task<SupportToolActionResult> ExecuteAsync(Guid companyId, Guid agentId, SupportToolActionRequest request, CancellationToken cancellationToken)
    {
        var tool = request.ToolName.Trim();
        if (agentId == Guid.Empty)
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, null, "support.tool.denied", "support_tool", tool, AuditEventOutcomes.Denied, "Support tool execution requires an agent identity.", ["support"]), cancellationToken);
            return new SupportToolActionResult(false, "denied", "Support tool execution requires an agent identity.", request.SupportCaseId);
        }

        tool = NormalizeToolName(tool);
        var policy = EvaluateToolPolicy(tool, request);
        if (!policy.Allowed)
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.tool.denied", "support_tool", tool, AuditEventOutcomes.Denied, policy.Summary, ["support", "policy"], Metadata: new Dictionary<string, string?> { ["policyDecision"] = policy.Status }), cancellationToken);
            return new SupportToolActionResult(false, policy.Status, policy.Summary, request.SupportCaseId);
        }

        try
        {
            if (tool.Equals("ClassifySupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid classifyId)
            {
                await _triage.TriageAsync(companyId, Guid.Empty, classifyId, cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support case classified.", classifyId);
            }

            if (tool.Equals("DraftSupportReply", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid draftId)
            {
                var draft = await _drafts.GenerateDraftAsync(companyId, Guid.Empty, draftId, new GenerateSupportReplyDraftRequest(), cancellationToken);
                return new SupportToolActionResult(draft is not null, draft is null ? "not_found" : "succeeded", draft is null ? "Support case was not found." : "Support reply drafted.", draftId, draft?.Id);
            }

            if (tool.Equals("UpdateSupportCaseStatus", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid caseId && request.Payload.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status))
            {
                await _cases.ChangeStatusAsync(companyId, Guid.Empty, caseId, new ChangeSupportStatusRequest(status!), cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support case status updated.", caseId);
            }

            if (tool.Equals("AddInternalSupportNote", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid noteCaseId)
            {
                var note = RequiredPayload(request, "note");
                var updated = await _cases.AddInternalNoteAsync(companyId, Guid.Empty, noteCaseId, new AddSupportInternalNoteRequest(note), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Internal note added.", noteCaseId);
            }

            if (tool.Equals("ChangeSupportPriority", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid priorityCaseId)
            {
                var priority = RequiredPayload(request, "priority");
                var updated = await _cases.ChangePriorityAsync(companyId, Guid.Empty, priorityCaseId, new ChangeSupportPriorityRequest(priority, OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support priority updated.", priorityCaseId);
            }

            if (tool.Equals("ChangeSupportCategory", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid categoryCaseId)
            {
                var category = RequiredPayload(request, "category");
                var updated = await _cases.ChangeCategoryAsync(companyId, Guid.Empty, categoryCaseId, new ChangeSupportCategoryRequest(category, OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support category updated.", categoryCaseId);
            }

            if (tool.Equals("AssignSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid assignCaseId)
            {
                var assignedAgentId = OptionalGuidPayload(request, "assignedAgentId");
                var assignedUserId = OptionalGuidPayload(request, "assignedUserId");
                var updated = await _cases.AssignAsync(companyId, Guid.Empty, assignCaseId, new AssignSupportCaseRequest(assignedAgentId, assignedUserId, OptionalPayload(request, "reason")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case assigned.", assignCaseId);
            }

            if (tool.Equals("EscalateSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid escalateCaseId)
            {
                var updated = await _cases.ChangeStatusAsync(companyId, Guid.Empty, escalateCaseId, new ChangeSupportStatusRequest(SupportCaseStatuses.Escalated, OptionalPayload(request, "reason") ?? "Escalated by support agent."), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case escalated.", escalateCaseId);
            }

            if (tool.Equals("RequestMissingInformation", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid missingInfoCaseId)
            {
                var question = OptionalPayload(request, "question") ?? "Please share the missing details so we can continue.";
                var draft = await _drafts.GenerateDraftAsync(companyId, Guid.Empty, missingInfoCaseId, new GenerateSupportReplyDraftRequest("Helpful"), cancellationToken);
                if (draft is not null)
                {
                    draft = await _drafts.EditDraftAsync(companyId, Guid.Empty, draft.Id, new EditSupportReplyDraftRequest($"Hello,\n\n{question}\n\nBest regards,\nSupport", draft.Tone), cancellationToken);
                }
                return new SupportToolActionResult(draft is not null, draft is null ? "not_found" : "succeeded", draft is null ? "Support case was not found." : "Missing-information reply drafted.", missingInfoCaseId, draft?.Id);
            }

            if (tool.Equals("ResolveSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid resolveCaseId)
            {
                var summary = RequiredPayload(request, "summary");
                var updated = await _cases.ResolveAsync(companyId, Guid.Empty, resolveCaseId, new ResolveSupportCaseRequest(summary, OptionalPayload(request, "outcome") ?? "Resolved"), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case resolved.", resolveCaseId);
            }

            if (tool.Equals("ReopenSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid reopenCaseId)
            {
                var updated = await _cases.ReopenAsync(companyId, Guid.Empty, reopenCaseId, new SupportActionRequest(OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case reopened.", reopenCaseId);
            }

            if (tool.Equals("CloseSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid closeCaseId)
            {
                var updated = await _cases.CloseAsync(companyId, Guid.Empty, closeCaseId, new SupportActionRequest(OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case closed.", closeCaseId);
            }

            if (tool.Equals("RequestSupportRefund", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid refundCaseId)
            {
                var refund = await _refunds.RequestRefundAsync(companyId, Guid.Empty, refundCaseId, new CreateSupportRefundRequest(
                    RequiredDecimalPayload(request, "amount"),
                    OptionalPayload(request, "currency") ?? "SEK",
                    OptionalPayload(request, "reasonCode") ?? "customer_support",
                    RequiredPayload(request, "explanation"),
                    OptionalGuidPayload(request, "invoiceId"),
                    OptionalGuidPayload(request, "paymentId")), cancellationToken);
                return new SupportToolActionResult(refund is not null, refund is null ? "not_found" : "succeeded", refund is null ? "Support case was not found." : "Refund or credit approval requested.", refundCaseId, refund?.Id);
            }

            if (tool.Equals("CreateSupportKnowledgeGap", StringComparison.OrdinalIgnoreCase))
            {
                var gap = await _knowledgeGaps.CreateOrIncrementAsync(companyId, new CreateSupportKnowledgeGapRequest(
                    request.SupportCaseId,
                    OptionalGuidPayload(request, "supportReplyDraftId"),
                    OptionalPayload(request, "category") ?? SupportCaseCategories.GeneralQuestion,
                    RequiredPayload(request, "questionSummary"),
                    RequiredPayload(request, "missingInformationSummary"),
                    OptionalPayload(request, "retrievalSourceSummary")), cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support knowledge gap recorded.", request.SupportCaseId, gap.Id);
            }

            if (tool.Equals("SendSupportReply", StringComparison.OrdinalIgnoreCase))
            {
                var sendDraftId = RequiredGuidPayload(request, "draftId");
                var updated = await _drafts.SendDraftAsync(companyId, Guid.Empty, sendDraftId, new SendSupportReplyDraftRequest(BoolPayload(request, "resolveAfterSend"), request.Autonomous), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support reply draft was not found." : "Support reply sent.", updated?.Id ?? request.SupportCaseId);
            }

            return new SupportToolActionResult(false, "unsupported", "Support tool is not supported yet or missing required payload.", request.SupportCaseId);
        }
        finally
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.tool.executed", "support_tool", tool, AuditEventOutcomes.Succeeded, "Support tool execution attempted.", ["support"]), cancellationToken);
        }
    }

    private sealed record SupportToolPolicyDecision(bool Allowed, string Status, string Summary);

    private static string NormalizeToolName(string tool) => tool.Trim() switch
    {
        var value when value.Equals("AddSupportInternalNote", StringComparison.OrdinalIgnoreCase) => "AddInternalSupportNote",
        var value when value.Equals("MarkSupportCaseResolved", StringComparison.OrdinalIgnoreCase) => "ResolveSupportCase",
        var value when value.Equals("RequestRefund", StringComparison.OrdinalIgnoreCase) => "RequestSupportRefund",
        var value when value.Equals("CreateBugReportTask", StringComparison.OrdinalIgnoreCase) => "CreateSupportKnowledgeGap",
        var value when value.Equals("CreateOperationsFollowUpTask", StringComparison.OrdinalIgnoreCase) => "CreateSupportKnowledgeGap",
        var value => value
    };

    private static SupportToolPolicyDecision EvaluateToolPolicy(string tool, SupportToolActionRequest request)
    {
        var knownTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClassifySupportCase",
            "DraftSupportReply",
            "UpdateSupportCaseStatus",
            "AddInternalSupportNote",
            "ChangeSupportPriority",
            "ChangeSupportCategory",
            "AssignSupportCase",
            "EscalateSupportCase",
            "RequestMissingInformation",
            "ResolveSupportCase",
            "ReopenSupportCase",
            "CloseSupportCase",
            "RequestSupportRefund",
            "CreateSupportKnowledgeGap",
            "SendSupportReply"
        };
        if (!knownTools.Contains(tool))
        {
            return new SupportToolPolicyDecision(false, "unsupported", "Support tool is not supported by the shared support tool policy.");
        }

        var requiresApproval = tool.Equals("RequestSupportRefund", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("SendSupportReply", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("ResolveSupportCase", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("CloseSupportCase", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("EscalateSupportCase", StringComparison.OrdinalIgnoreCase);
        if (request.Autonomous && requiresApproval)
        {
            return new SupportToolPolicyDecision(false, "approval_required", "This support action is risky and requires human approval before execution.");
        }

        return new SupportToolPolicyDecision(true, "allowed", "Support tool execution allowed by policy.");
    }
    private static string RequiredPayload(SupportToolActionRequest request, string key) =>
        OptionalPayload(request, key) ?? throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field is required."] });

    private static string? OptionalPayload(SupportToolActionRequest request, string key) =>
        request.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static Guid RequiredGuidPayload(SupportToolActionRequest request, string key) =>
        OptionalGuidPayload(request, key) ?? throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field must be a valid identifier."] });

    private static Guid? OptionalGuidPayload(SupportToolActionRequest request, string key) =>
        Guid.TryParse(OptionalPayload(request, key), out var value) && value != Guid.Empty ? value : null;

    private static decimal RequiredDecimalPayload(SupportToolActionRequest request, string key)
    {
        if (decimal.TryParse(OptionalPayload(request, key), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field must be a positive amount."] });
    }

    private static bool BoolPayload(SupportToolActionRequest request, string key) =>
        bool.TryParse(OptionalPayload(request, key), out var value) && value;
}

