using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

// A supervision read model. Commands, approval eligibility and delivery stay with their owners.
public sealed class BusinessWorkEvidenceQueryService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    IAgentWorkQueryService work, ICollaborationEvidenceQueryService collaboration, IApprovalRequestService approvals,
    TimeProvider clock, ILogger<BusinessWorkEvidenceQueryService> logger) : IBusinessWorkEvidenceQueryService
{
    private const int Limit = 24;
    public async Task<BusinessWorkEvidenceDto> GetAsync(Guid companyId, string kind, Guid id, CancellationToken token)
    {
        if (companyId == Guid.Empty || id == Guid.Empty || !new[] { "deal", "case", "invoice", "bill", "campaign", "brief" }.Contains(kind))
            throw new ArgumentException("A valid business record identity is required.");
        try
        {
            var scope = await visibility.ResolveAsync(companyId, token);
            var area = kind switch { "deal" => "sales", "case" => "support", "invoice" or "bill" => "finance", _ => "marketing" };
            if (!scope.Allows(area)) throw new UnauthorizedAccessException();
            var exists = kind switch
            {
                "deal" => await db.Deals.AnyAsync(x => x.CompanyId == companyId && x.Id == id && !x.IsDeleted, token),
                "case" => await db.SupportCases.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token),
                "invoice" => await db.FinanceInvoices.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token),
                "bill" => await db.FinanceBills.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token) ||
                    await db.DetectedBills.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token),
                "campaign" => await db.SalesCampaigns.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token),
                _ => await db.MarketingContentBriefs.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token)
            };
            if (!exists) throw new KeyNotFoundException("Business record is unavailable in this company.");
            var intakeBill = kind == "bill" && await db.DetectedBills.AnyAsync(x => x.CompanyId == companyId && x.Id == id, token);
            var billIds = new List<Guid> { id };
            if (kind == "bill")
            {
                var linkedBills = await db.FinanceBills.AsNoTracking().Where(x => x.CompanyId == companyId &&
                    (x.Id == id || x.SourceDetectedBillId == id)).Select(x => new { x.Id, x.SourceDetectedBillId }).Take(Limit + 1).ToListAsync(token);
                billIds.AddRange(linkedBills.Select(x => x.Id));
                var intakeIds = linkedBills.Where(x => x.SourceDetectedBillId.HasValue).Select(x => x.SourceDetectedBillId!.Value).ToArray();
                billIds.AddRange(await db.DetectedBills.Where(x => x.CompanyId == companyId && intakeIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(token));
            }
            var route = kind switch
            {
                "deal" => $"/app/sales/deals/{id:D}?companyId={companyId:D}",
                "case" => $"/support/cases/{id:D}?companyId={companyId:D}",
                "invoice" => $"/finance/reviews/{id:D}?companyId={companyId:D}",
                "bill" => $"/finance/{(intakeBill ? "bill-inbox" : "supplier-bills")}/{id:D}?companyId={companyId:D}&financeSource=operational",
                _ => $"/marketing/review?companyId={companyId:D}&{(kind == "brief" ? "briefId" : "campaignId")}={id:D}"
            };
            var diagnostics = new List<string>();
            var artifacts = new List<BusinessWorkArtifactDto>();
            var nativeTasks = new HashSet<Guid>();
            var decisions = new List<BusinessWorkDecisionDto>();
            var agents = await scope.Agents(db.Agents.AsNoTracking()).OrderBy(x => x.Id).Take(2001).ToDictionaryAsync(x => x.Id, token);
            AgentWorkPersonDto? Person(Guid? agentId) => agentId.HasValue && agents.TryGetValue(agentId.Value, out var a)
                ? new(a.Id, a.DisplayName, a.RoleName) : null;
            async Task<string?> Decision(Guid? approvalId)
            {
                if (!approvalId.HasValue) return null;
                try
                {
                    var current = await approvals.GetAsync(companyId, approvalId.Value, token);
                    return $"/work?companyId={companyId:D}&tab=approvals&itemId={current.Id:D}";
                }
                catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
                {
                    diagnostics.Add("A linked decision is unavailable in your current access scope. Ask the accountable human to reconcile the association.");
                    logger.LogInformation("Business decision association unavailable: {CompanyId} {Kind} {RecordId} {ApprovalId}", companyId, kind, id, approvalId);
                    return null;
                }
            }
            if (kind == "deal")
            {
                var rows = await db.SalesAgentRecommendations.AsNoTracking().Where(x => x.CompanyId == companyId && x.DealId == id && !x.IsDeleted)
                    .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in rows) artifacts.Add(new(row.Id, "sales_recommendation", row.ActionType, row.Recommendation,
                    row.Status, row.ApprovalStatus, row.ExecutionStatus, row.Rationale, null, row.UpdatedUtc, null, route, null,
                    row.FailureSummary is null ? [] : [row.FailureSummary]));
                if (scope.Allows("finance"))
                {
                    var handoffs = await db.SalesFinanceHandoffs.AsNoTracking().Where(x => x.CompanyId == companyId && x.DealId == id)
                        .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                    foreach (var row in handoffs) artifacts.Add(new(row.Id, "sales_finance_handoff", "Finance handoff", row.Summary,
                        row.Status, row.ApprovalStatus, row.ExecutionStatus, "Finance approval and provider execution are separate steps.", null,
                        row.UpdatedUtc, null, route, await Decision(row.ApprovalId), row.FailureSummary is null ? [] : [row.FailureSummary]));
                }
                else diagnostics.Add("Finance handoff evidence requires Finance access.");
            }
            if (kind == "case")
            {
                var rows = await db.SupportReplyDrafts.AsNoTracking().Where(x => x.CompanyId == companyId && x.SupportCaseId == id)
                    .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in rows)
                {
                    if (row.CreatedByAgentId.HasValue && Person(row.CreatedByAgentId) is null)
                    { diagnostics.Add("A reply contribution is restricted or its agent association is unavailable."); continue; }
                    artifacts.Add(new(row.Id, "support_reply_draft", "Grounded reply draft", row.DraftBody, row.Status,
                        row.Status, row.DeliveryStatus, row.RationaleSummary ?? "Review grounding and safety in the case before approving or sending.",
                        Person(row.CreatedByAgentId), row.UpdatedUtc, null, route, null,
                        (row.SendFailureSummary is null ? Array.Empty<string>() : [row.SendFailureSummary])
                            .Concat(string.IsNullOrWhiteSpace(row.SourceReferencesJson) ? ["No source references are retained for this draft."] : Array.Empty<string>()).ToList()));
                }
                nativeTasks.UnionWith(await db.SupportKnowledgeGaps.Where(x => x.CompanyId == companyId && x.SupportCaseId == id && x.LinkedTaskId != null)
                    .Select(x => x.LinkedTaskId!.Value).Take(Limit + 1).ToListAsync(token));
            }
            if (kind == "invoice")
            {
                var rows = await db.CustomerInvoiceCorrections.AsNoTracking().Where(x => x.CompanyId == companyId && x.InvoiceId == id)
                    .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in rows)
                {
                    if (row.TaskId.HasValue) nativeTasks.Add(row.TaskId.Value);
                    artifacts.Add(new(row.Id, "invoice_correction", row.CorrectionType, row.Reason, row.Status, row.Status, row.Status,
                        "A proposed correction does not establish posting, refund or statutory approval.", null, row.UpdatedUtc, row.Version,
                        route, await Decision(row.ApprovalRequestId), row.FailureSummary is null ? [] : [row.FailureSummary]));
                }
            }
            if (kind == "bill")
            {
                var intake = await db.FinanceBillReviewStates.AsNoTracking().Where(x => x.CompanyId == companyId && billIds.Contains(x.DetectedBillId))
                    .OrderByDescending(x => x.UpdatedUtc).Take(Limit + 1).ToListAsync(token);
                foreach (var row in intake) artifacts.Add(new(row.Id, "bill_intake_review", "Supplier bill intake review", row.ProposalSummary,
                    row.Status, row.Status, "Posting and payment require their owning workflow", "Extraction review does not establish posting or settlement.",
                    null, row.UpdatedUtc, null, route, null, []));
                var rows = await db.SupplierInvoicePaymentProposals.AsNoTracking().Where(x => x.CompanyId == companyId && billIds.Contains(x.BillId))
                    .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in rows)
                {
                    if (row.TaskId.HasValue) nativeTasks.Add(row.TaskId.Value);
                    artifacts.Add(new(row.Id, "payment_proposal", "Supplier payment proposal", $"{row.Amount} {row.Currency} · {row.SupplierName}",
                        row.Status, row.Status, row.ExportStatus, "Approval authorizes this proposal's next governed step. It does not confirm money movement.",
                        null, row.UpdatedUtc, null, route, await Decision(row.ApprovalRequestId),
                        row.ExportResponseSummary is null ? [] : [row.ExportResponseSummary]));
                }
                nativeTasks.UnionWith(await db.SupplierInvoiceCorrectionActions.Where(x => x.CompanyId == companyId && billIds.Contains(x.BillId) && x.TaskId != null)
                    .Select(x => x.TaskId!.Value).Take(Limit + 1).ToListAsync(token));
                nativeTasks.UnionWith(await db.SupplierInvoiceEnrichmentActions.Where(x => x.CompanyId == companyId && billIds.Contains(x.BillId) && x.TaskId != null)
                    .Select(x => x.TaskId!.Value).Take(Limit + 1).ToListAsync(token));
            }
            if (kind is "campaign" or "brief")
            {
                var briefs = await db.MarketingContentBriefs.AsNoTracking().Where(x => x.CompanyId == companyId &&
                    (kind == "brief" ? x.Id == id : x.SalesCampaignId == id)).OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in briefs)
                {
                    if (row.OwnerAgentId.HasValue && Person(row.OwnerAgentId) is null)
                    { diagnostics.Add("A content contribution is restricted or its agent association is unavailable."); continue; }
                    var briefRoute = $"/marketing/review?companyId={companyId:D}&briefId={row.Id:D}";
                    artifacts.Add(new(row.Id, "content_brief", row.Title, row.Purpose, row.Status, row.Status, "See channel delivery record",
                        "Content review and customer publication are separate governed steps.", Person(row.OwnerAgentId), row.UpdatedUtc, row.Version, briefRoute, null, []));
                }
                var briefIds = briefs.Where(x => !x.OwnerAgentId.HasValue || Person(x.OwnerAgentId) is not null).Select(x => x.Id).ToArray();
                var variants = await db.MarketingContentVariants.AsNoTracking().Where(x => x.CompanyId == companyId && briefIds.Contains(x.MarketingContentBriefId))
                    .OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in variants) artifacts.Add(new(row.Id, "content_variant", row.Name, row.Body, row.Status, row.Status,
                    "Draft artifact; delivery not established", "Review the source brief and grounding before publication.", null, row.CreatedUtc, row.VersionNumber, route, null,
                    string.IsNullOrWhiteSpace(row.SourceReferences) || row.SourceReferences.Trim() == "[]" ? ["No source references are retained for this variant."] : []));
                var actions = await db.MarketingChannelActions.AsNoTracking().Where(x => x.CompanyId == companyId &&
                    (kind == "brief" ? x.MarketingContentBriefId == id : x.SalesCampaignId == id))
                    .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).ToListAsync(token);
                foreach (var row in actions) artifacts.Add(new(row.Id, "channel_action", row.ActionType, "Review the retained channel action in Launch and delivery records.",
                    row.Status, "See current decision", row.Status, "External channel action requires its current approval and owning outbox checks.", null,
                    row.UpdatedUtc, row.Version, route, await Decision(row.ApprovalRequestId), row.FailureCode is null ? [] : [row.FailureCode]));
                nativeTasks.UnionWith(await db.WorkTasks.Where(x => x.CompanyId == companyId && x.BusinessBriefId != null && briefIds.Contains(x.BusinessBriefId.Value))
                    .Select(x => x.Id).Take(Limit + 1).ToListAsync(token));
            }
            var taskQuery = db.WorkTasks.AsNoTracking().Where(x => x.CompanyId == companyId && (nativeTasks.Contains(x.Id) ||
                (kind == "deal" && x.BusinessDealId == id) || (kind == "case" && x.BusinessCaseId == id) ||
                (kind == "invoice" && x.BusinessInvoiceId == id) || (kind == "bill" && x.BusinessBillId.HasValue && billIds.Contains(x.BusinessBillId.Value)) ||
                (kind == "campaign" && x.BusinessCampaignId == id) || (kind == "brief" && x.BusinessBriefId == id)));
            var linkedIds = await taskQuery.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit + 1).Select(x => x.Id).ToListAsync(token);
            var permittedIds = await scope.Tasks(taskQuery).Where(x => linkedIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(token);
            var items = new List<AgentWorkItemDto>();
            var collaborations = new List<CollaborationEvidenceDto>();
            foreach (var taskId in linkedIds.Take(Limit))
            {
                if (!permittedIds.Contains(taskId) || !await CollaborationContentVisibility.AllowsAsync(db, scope, taskId, token))
                { diagnostics.Add("Some linked work and contribution evidence is restricted. Ask the accountable human to review it."); continue; }
                try
                {
                    items.Add(await work.GetAsync(companyId, "task", taskId, token));
                    collaborations.Add(await collaboration.GetAsync(companyId, "task", taskId, token));
                    var approvalId = await db.ApprovalRequests.Where(x => x.CompanyId == companyId && x.TargetEntityType == "task" && x.TargetEntityId == taskId)
                        .OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
                    if (approvalId.HasValue)
                    {
                        var decisionRoute = await Decision(approvalId);
                        if (decisionRoute is not null)
                        {
                            var current = await approvals.GetAsync(companyId, approvalId.Value, token);
                            decisions.Add(new(taskId, current.Id, current.Status, current.RationaleSummary, current.ThresholdSummary,
                                current.Review?.Reviewer ?? "Reviewer not recorded", current.Review?.ExpiresAt,
                                current.Review?.ProposalChanged ?? false, decisionRoute));
                        }
                        else items[^1] = items[^1] with { RelatedRecords = items[^1].RelatedRecords.Where(x => x.Label != "Review approval").ToList() };
                    }
                }
                catch (KeyNotFoundException)
                {
                    diagnostics.Add("A linked work record changed or is unavailable. Refresh the business record to reconcile it.");
                    logger.LogInformation("Business work association unavailable: {CompanyId} {Kind} {RecordId} {TaskId}", companyId, kind, id, taskId);
                }
            }
            if (nativeTasks.Except(linkedIds).Any() && linkedIds.Count <= Limit)
            {
                diagnostics.Add("A retained work association has no available task in this company. Its outcome is unknown.");
                logger.LogWarning("Business work has missing associations: {CompanyId} {Kind} {RecordId}", companyId, kind, id);
            }
            var partial = agents.Count > 2000 || linkedIds.Count > Limit || artifacts.Count > Limit || collaborations.Any(x => x.IsPartial);
            if (partial) diagnostics.Add("This panel shows the latest 24 artifacts and work records. Open the owning records for older or partial evidence.");
            if (items.Count == 0 && artifacts.Count == 0) diagnostics.Add("No retained agent contribution is linked to this record. This does not establish that no work occurred.");
            var meaning = area switch
            {
                "sales" => "A recommendation or proposal is retained intent. Draft creation, approval and customer delivery have separate recorded outcomes.",
                "support" => "A grounded reply draft is separate from a sent reply. The case owns review, safety and delivery.",
                "finance" => "Review, proposed accounting changes and payment proposals are separate from posting or money movement.",
                _ => "Approved content is separate from published content. Channel delivery remains owned by its governed workflow."
            };
            return new(companyId, kind, id, clock.GetUtcNow().UtcDateTime, meaning,
                "Open the retained work or current decision. Use the business record's controls for actions permitted by its workflow; refresh after a decision elsewhere.",
                artifacts.OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).Take(Limit).ToList(), items, collaborations,
                diagnostics.Distinct().ToList(), partial, decisions);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or UnauthorizedAccessException or KeyNotFoundException))
        {
            logger.LogWarning("Business work evidence refresh failed: {CompanyId} {Kind} {RecordId} {FailureType}", companyId, kind, id, ex.GetType().Name);
            throw;
        }
    }
}
