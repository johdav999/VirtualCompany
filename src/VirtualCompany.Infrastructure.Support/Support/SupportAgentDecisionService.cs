using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportAgentDecisionService(
    VirtualCompanyDbContext db,
    ISupportAgentAnalysisService analysis,
    ISupportKnowledgeContextProvider knowledge,
    ISupportReplySafetyPolicy safety) : ISupportAgentDecisionService
{
    public async Task<SupportQueueAnalysisResult> AnalyzeQueueAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SupportQueueAnalysisRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var query = OpenCases(companyId);
        if (request.SupportCaseId.HasValue) query = query.Where(x => x.Id == request.SupportCaseId.Value);
        var cases = await query.OrderBy(x => x.ResolutionDueUtc).ThenBy(x => x.CreatedUtc).Take(200).ToListAsync(ct);
        if (request.SupportCaseId.HasValue && cases.Count == 0) throw new KeyNotFoundException("Support case not found.");
        var items = cases.Select(x => QueueItem(x, now)).OrderByDescending(x => x.PriorityScore)
            .ThenBy(x => x.ResolutionDueUtc ?? DateTime.MaxValue).ThenBy(x => x.SupportCaseId).Take(limit).ToArray();
        var missing = new List<string>();
        if (cases.Any(x => !x.ResolutionDueUtc.HasValue)) missing.Add("Authoritative resolution deadlines for every open case");
        if (cases.Any(x => !x.ContactId.HasValue)) missing.Add("Confirmed customer context for every open case");
        var advice = await Analyze(companyId, agentId, actorUserId, SupportAgentAnalysisTypes.TriageAnalysis,
            request.SupportCaseId, request.Objective, now, ct);
        return new SupportQueueAnalysisResult(advice, items, missing,
            advice.RequiresReview || items.Any(x => x.RequiresReview));
    }

    public async Task<SupportAnswerabilityResult> AnalyzeAnswerabilityAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SupportAnswerabilityRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var supportCase = await db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.SupportCaseId, ct)
            ?? throw new KeyNotFoundException("Support case not found.");
        var context = await knowledge.RetrieveAsync(companyId, supportCase.Id, ct);
        var trusted = context.Sources.Where(IsTrustedGrounding).ToArray();
        var trustedIds = trusted.Select(SourceId).Distinct(StringComparer.Ordinal).ToArray();
        var claims = new List<SupportAnswerabilityClaimDto>
        {
            new("case_fact", $"Case category is {supportCase.Category} and priority is {supportCase.Priority}.",
                "confirmed", [$"support-case:{supportCase.Id:N}"])
        };
        foreach (var source in trusted.Take(10))
            claims.Add(new SupportAnswerabilityClaimDto("company_or_record_fact", source.Label, "grounded", [SourceId(source)]));
        var missing = new List<string>();
        var questions = new List<string>();
        if (!context.HasTrustedGrounding)
        {
            missing.Add("Processed, indexed, accessible company knowledge or authoritative business records");
            questions.Add("Which product, plan, order, invoice, or account record should this answer use?");
        }
        if (!supportCase.ContactId.HasValue)
        {
            missing.Add("Confirmed customer identity");
            questions.Add("Can the customer confirm the account email or non-sensitive account reference?");
        }
        if (supportCase.Category is SupportCaseCategories.Billing or SupportCaseCategories.Refund &&
            !supportCase.RelatedInvoiceId.HasValue && !supportCase.RelatedPaymentId.HasValue)
        {
            missing.Add("Linked invoice or payment evidence");
            questions.Add("Which invoice or payment is the question about?");
        }
        var severe = SevereRiskTypes(supportCase).Count > 0;
        SupportReplySafetyDecision? draftSafety = null;
        if (!string.IsNullOrWhiteSpace(request.DraftBody))
            draftSafety = await safety.EvaluateAsync(companyId, supportCase.Id, request.DraftBody,
                BuildSourceReferences(trusted), ct);
        var score = context.HasTrustedGrounding ? Math.Clamp(context.RetrievalConfidence, 0m, 1m) : 0m;
        var canDraft = score >= .55m && !severe && (draftSafety is null || draftSafety.Decision == "allow");
        var state = canDraft ? "answerable" : score > 0m ? "partially_answerable" : "not_answerable";
        var advice = await Analyze(companyId, agentId, actorUserId, SupportAgentAnalysisTypes.GroundedReply,
            supportCase.Id, request.Objective, now, ct);
        return new SupportAnswerabilityResult(advice, supportCase.Id, score, state, claims, missing, questions,
            trustedIds, draftSafety, canDraft, !canDraft || advice.RequiresReview || draftSafety is null);
    }

    public async Task<SupportRiskAssessmentResult> AnalyzeRiskAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SupportRiskAssessmentRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var query = OpenCases(companyId);
        if (request.SupportCaseId.HasValue) query = query.Where(x => x.Id == request.SupportCaseId.Value);
        var cases = await query.OrderBy(x => x.ResolutionDueUtc).ThenBy(x => x.Id)
            .Take(Math.Clamp(request.Limit, 1, 100)).ToListAsync(ct);
        if (request.SupportCaseId.HasValue && cases.Count == 0) throw new KeyNotFoundException("Support case not found.");
        var risks = new List<SupportRiskItemDto>();
        foreach (var supportCase in cases)
        {
            var confirmed = SevereRiskTypes(supportCase);
            if (supportCase.IsSlaBreached) confirmed.Add("sla_breach");
            else if (supportCase.IsSlaRisk) confirmed.Add("sla_risk");
            if (supportCase.Category == SupportCaseCategories.Refund) confirmed.Add("refund_or_credit_request");
            var hypotheses = new List<string>();
            if (supportCase.IsChurnRisk || supportCase.Category == SupportCaseCategories.ChurnRisk) hypotheses.Add("customer_retention_risk");
            if (string.Equals(supportCase.Sentiment, "negative", StringComparison.OrdinalIgnoreCase)) hypotheses.Add("negative_sentiment");
            if (confirmed.Count == 0 && hypotheses.Count == 0) continue;
            var mandatory = confirmed.Any(x => x is "security" or "privacy" or "legal" or "threat" or "sla_breach");
            decimal? minutes = supportCase.ResolutionDueUtc.HasValue
                ? (decimal)(supportCase.ResolutionDueUtc.Value - now).TotalMinutes : null;
            var severity = mandatory ? "critical" : supportCase.IsSlaRisk || supportCase.Priority == SupportPriorities.Urgent ? "high" : "review";
            var role = confirmed.Contains("refund_or_credit_request") ? "finance" : mandatory ? "support_supervisor" : "support_owner";
            var allowed = confirmed.Contains("refund_or_credit_request")
                ? new[] { "review_case", "prepare_finance_handoff", "request_missing_evidence" }
                : new[] { "review_case", "escalate_internally", "request_missing_evidence" };
            risks.Add(new SupportRiskItemDto(supportCase.Id, supportCase.CaseNumber, supportCase.Subject, severity,
                confirmed, hypotheses, supportCase.ResolutionDueUtc, minutes, role, mandatory, allowed,
                [$"support-case:{supportCase.Id:N}"]));
        }
        risks = risks.OrderBy(x => x.Severity == "critical" ? 0 : x.Severity == "high" ? 1 : 2)
            .ThenBy(x => x.ResolutionDueUtc ?? DateTime.MaxValue).ToList();
        var missing = cases.Any(x => !x.ResolutionDueUtc.HasValue)
            ? new[] { "Authoritative SLA deadline for every assessed case" } : [];
        var advice = await Analyze(companyId, agentId, actorUserId, SupportAgentAnalysisTypes.RiskEscalation,
            request.SupportCaseId, request.Objective, now, ct);
        return new SupportRiskAssessmentResult(advice, risks, missing, advice.RequiresReview || risks.Count > 0);
    }

    public async Task<SupportRecurringIssueResult> AnalyzeRecurringIssuesAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SupportRecurringIssueRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var window = Math.Clamp(request.WindowDays, 7, 365);
        var minimum = Math.Clamp(request.MinimumCases, 2, 20);
        var cases = await db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CreatedUtc >= now.AddDays(-window))
            .OrderBy(x => x.Category).ThenBy(x => x.Id).Take(1000).ToListAsync(ct);
        var clusters = new List<SupportRecurringIssueDto>();
        foreach (var group in cases.GroupBy(x => x.Category).Where(x => x.Count() >= minimum))
        {
            var members = group.OrderBy(x => x.Id).ToArray();
            var sourceIds = members.Select(x => $"support-case:{x.Id:N}").ToArray();
            var versionInput = string.Join('|', members.Select(x => $"{x.Id:N}:{x.UpdatedUtc.Ticks}"));
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionInput)))[..16].ToLowerInvariant();
            var id = $"support-cluster:{group.Key}:{now:yyyyMMdd}";
            var shared = new List<string> { $"All {members.Length} cases have authoritative category '{group.Key}'." };
            var differences = new List<string>
            {
                $"Statuses represented: {string.Join(", ", members.Select(x => x.Status).Distinct().Order())}.",
                $"Priorities represented: {string.Join(", ", members.Select(x => x.Priority).Distinct().Order())}."
            };
            var hypotheses = new[] { "A shared product, documentation, or process issue may exist; case category alone does not prove root cause." };
            clusters.Add(new SupportRecurringIssueDto(id, version, group.Key, members.Length,
                members.Select(x => x.Id).ToArray(), shared, differences, hypotheses,
                "Review representative cases, confirm a reproducible pattern, then assign a bounded investigation.",
                sourceIds, true));
        }
        var missing = clusters.Count == 0 ? new[] { $"At least {minimum} cases in one category during the selected window" } : [];
        var advice = await Analyze(companyId, agentId, actorUserId, SupportAgentAnalysisTypes.RootCauseAnalysis,
            null, request.Objective, now, ct);
        return new SupportRecurringIssueResult(advice, clusters.OrderByDescending(x => x.CaseCount).ToArray(),
            missing, true);
    }

    public async Task<SupportKnowledgeCoverageResult> AnalyzeKnowledgeCoverageAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SupportKnowledgeCoverageRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var gaps = await db.SupportKnowledgeGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.FrequencyCount)
            .ThenByDescending(x => x.UpdatedUtc).Take(Math.Clamp(request.Limit, 1, 100)).ToListAsync(ct);
        var documentIds = gaps.Where(x => x.LinkedKnowledgeDocumentId.HasValue)
            .Select(x => x.LinkedKnowledgeDocumentId!.Value).Distinct().ToArray();
        var documents = await db.CompanyKnowledgeDocuments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && documentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var recommendations = gaps.Select(gap =>
        {
            var verified = gap.LinkedKnowledgeDocumentId is { } documentId && documents.TryGetValue(documentId, out var document) &&
                           document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
                           document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed && document.ActiveChunkCount > 0;
            return new SupportDocumentationRecommendationDto(gap.Id, gap.QuestionSummary, gap.Category,
                gap.FrequencyCount, gap.MissingInformationSummary, gap.Status, gap.LinkedKnowledgeDocumentId,
                verified, "support_knowledge_owner", [$"support-knowledge-gap:{gap.Id:N}"]);
        }).ToArray();
        var open = recommendations.Count(x => x.Status != SupportKnowledgeGapStatuses.Resolved || !x.ResolutionVerified);
        var repeated = recommendations.Count(x => x.Frequency > 1);
        var missing = new List<string>();
        if (gaps.Count == 0) missing.Add("Recorded answerability and knowledge-gap outcomes");
        if (recommendations.Any(x => x.Status == SupportKnowledgeGapStatuses.Resolved && !x.ResolutionVerified))
            missing.Add("Processed and indexed replacement knowledge for every resolved gap");
        var advice = await Analyze(companyId, agentId, actorUserId, SupportAgentAnalysisTypes.KnowledgeCoverage,
            null, request.Objective, now, ct);
        return new SupportKnowledgeCoverageResult(advice, open, repeated, recommendations, missing,
            advice.RequiresReview || open > 0);
    }

    private IQueryable<SupportCase> OpenCases(Guid companyId) => db.SupportCases.IgnoreQueryFilters().AsNoTracking()
        .Where(x => x.CompanyId == companyId && x.Status != SupportCaseStatuses.Closed && x.Status != SupportCaseStatuses.Resolved);

    private static SupportQueueItemDto QueueItem(SupportCase supportCase, DateTime now)
    {
        var severe = SevereRiskTypes(supportCase);
        var mandatory = severe.Count > 0 || supportCase.IsSlaBreached;
        decimal? minutes = supportCase.ResolutionDueUtc.HasValue
            ? (decimal)(supportCase.ResolutionDueUtc.Value - now).TotalMinutes : null;
        var reasons = new List<string>();
        var score = 20;
        if (mandatory) { score += 60; reasons.Add("mandatory_escalation"); }
        if (supportCase.IsSlaRisk) { score += 25; reasons.Add("sla_risk"); }
        if (supportCase.IsSlaBreached) { score += 20; reasons.Add("sla_breached"); }
        if (supportCase.Priority == SupportPriorities.Urgent) { score += 25; reasons.Add("urgent_priority"); }
        else if (supportCase.Priority == SupportPriorities.High) { score += 15; reasons.Add("high_priority"); }
        if (minutes <= 240m) { score += 15; reasons.Add("deadline_within_four_hours"); }
        if (!supportCase.ContactId.HasValue) reasons.Add("customer_context_missing");
        score += Math.Min(15, Math.Max(0, (now.Date - supportCase.CreatedUtc.Date).Days));
        score = Math.Clamp(score, 0, 100);
        return new SupportQueueItemDto(supportCase.Id, supportCase.CaseNumber, supportCase.Subject, score,
            mandatory ? "critical" : score >= 75 ? "urgent" : score >= 50 ? "high" : "normal",
            supportCase.Category, supportCase.Status, supportCase.ResolutionDueUtc, minutes, mandatory,
            mandatory || !supportCase.ContactId.HasValue || !supportCase.ConfidenceScore.HasValue, reasons,
            [$"support-case:{supportCase.Id:N}"]);
    }

    private static List<string> SevereRiskTypes(SupportCase supportCase)
    {
        var text = $"{supportCase.Category} {supportCase.Subject} {supportCase.Summary} {supportCase.Description}".ToLowerInvariant();
        var types = new List<string>();
        if (Contains(text, "security", "breach", "hacked", "vulnerability")) types.Add("security");
        if (Contains(text, "privacy", "personal data", "gdpr", "data request")) types.Add("privacy");
        if (Contains(text, "legal", "lawyer", "lawsuit", "regulator")) types.Add("legal");
        if (Contains(text, "threat", "violence", "harm")) types.Add("threat");
        return types;
    }

    private static bool Contains(string text, params string[] values) => values.Any(text.Contains);
    private static bool IsTrustedGrounding(SupportKnowledgeSourceReference source) => source.IsTrusted &&
        source.Relevance >= .55m && source.Type is "knowledge_chunk" or "business_record";
    private static string SourceId(SupportKnowledgeSourceReference source) => source.EntityId.HasValue
        ? $"{source.Type}:{source.EntityId:N}" : $"{source.Type}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Label)))[..12].ToLowerInvariant()}";
    private static string BuildSourceReferences(IEnumerable<SupportKnowledgeSourceReference> sources) =>
        JsonSerializer.Serialize(sources.Select(x => new { type = x.Type, trusted = true, relevance = x.Relevance }));
    private Task<RoleAgentAnalysisResult> Analyze(Guid companyId, Guid agentId, Guid? actorUserId, string type,
        Guid? subjectId, string? objective, DateTime now, CancellationToken ct) => analysis.AnalyzeAsync(companyId,
        agentId, actorUserId, new RoleAgentAnalysisRequest(type, subjectId, Objective: objective, AsOfUtc: now), ct);
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static void ValidateIds(Guid companyId, Guid agentId)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
    }
}
