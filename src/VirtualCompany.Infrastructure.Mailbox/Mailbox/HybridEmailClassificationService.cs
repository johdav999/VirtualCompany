using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed partial class HybridEmailClassificationService : IEmailClassificationService
{
    private const decimal HighConfidence = 0.85m;
    private const decimal ReviewConfidence = 0.75m;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IBillDetectionService _billDetection;
    private readonly IAgentReasoningGateway _reasoning;
    private readonly ILogger<HybridEmailClassificationService> _logger;

    public HybridEmailClassificationService(
        VirtualCompanyDbContext dbContext,
        IBillDetectionService billDetection,
        IAgentReasoningGateway reasoning,
        ILogger<HybridEmailClassificationService> logger)
    {
        _dbContext = dbContext;
        _billDetection = billDetection;
        _reasoning = reasoning;
        _logger = logger;
    }

    public async Task<EmailClassificationResult> ClassifyAsync(EmailClassificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }

        var deterministic = ClassifyDeterministically(request);
        if (!ShouldUseAi(request, deterministic))
        {
            return deterministic;
        }

        var ai = await ClassifyWithAiAsync(request, deterministic, cancellationToken);
        return ai ?? deterministic;
    }

    private EmailClassificationResult ClassifyDeterministically(EmailClassificationRequest request)
    {
        var text = Normalize(CollectText(request));
        var rules = new List<string> { $"purpose:{request.MailboxPurpose.ToString().ToLowerInvariant()}" };

        if (ContainsAny(text, "unsubscribe", "newsletter", "weekly digest", "view in browser"))
        {
            rules.Add("obvious_newsletter");
            return Result(EmailClassificationDomains.Unknown, EmailClassificationIntents.Newsletter, 0.95m,
                "Deterministic newsletter indicators were present.", EmailClassificationActions.Ignore, false, rules, EmailClassificationIntents.Newsletter);
        }

        return request.MailboxPurpose switch
        {
            MailboxPurpose.Finance => ClassifyFinance(request, text, rules),
            MailboxPurpose.Sales => ClassifySales(text, rules),
            MailboxPurpose.Support => ClassifySupport(text, rules),
            _ => Result(EmailClassificationDomains.Unknown, EmailClassificationIntents.Unknown, 0.35m,
                "The mailbox purpose is not supported for business email classification.", EmailClassificationActions.HumanReview, true, rules)
        };
    }

    private EmailClassificationResult ClassifyFinance(EmailClassificationRequest request, string text, List<string> rules)
    {
        if (ContainsAny(text, "subscription agreement", "recurring service", "notice period", "abonnemang", "avtal", "uppsagningstid", "prenumeration") &&
            ContainsAny(text, "monthly", "quarterly", "yearly", "per month", "per year", "manad", "kvartal", "arsvis", "sek", "eur", "usd"))
        {
            rules.Add("subscription_agreement_terms");
            return Result(EmailClassificationDomains.Finance, EmailClassificationIntents.SupplierSubscriptionAgreement, 0.88m,
                "Recurring supplier agreement terms were detected in a Finance mailbox source.", EmailClassificationActions.RouteToFinanceReview, true, rules);
        }

        var billDetection = request.Summary is null ? null : _billDetection.Detect(request.Summary);
        if (billDetection?.IsCandidate == true)
        {
            rules.AddRange(billDetection.MatchedRules.Select(rule => $"bill:{rule.ToString().ToLowerInvariant()}"));
            var intent = ContainsAny(text, "receipt", "payment received", "paid", "kvitto", "betalning") && !ContainsAny(text, "invoice number", "fakturanummer")
                ? EmailClassificationIntents.Receipt
                : EmailClassificationIntents.Invoice;
            return Result(EmailClassificationDomains.Finance, intent, 0.90m, billDetection.ReasonSummary,
                EmailClassificationActions.RouteToFinanceReview, true, rules);
        }

        if (ContainsAny(text, "invoice", "faktura", "amount due", "payment due", "bankgiro", "ocr"))
        {
            rules.Add("finance_keyword_weak");
            return Result(EmailClassificationDomains.Finance, EmailClassificationIntents.Invoice, 0.62m,
                "Finance keywords were present, but deterministic bill rules were not strong enough.", EmailClassificationActions.HumanReview, true, rules);
        }

        return Result(EmailClassificationDomains.Unknown, EmailClassificationIntents.Unknown, 0.45m,
            "No deterministic Finance email intent was strong enough to act on.", EmailClassificationActions.HumanReview, true, rules);
    }

    private static EmailClassificationResult ClassifySales(string text, List<string> rules)
    {

        var hasBuyingIntent = ContainsAny(text, "demo", "pricing", "quote", "proposal", "interested", "buy", "purchase", "evaluate", "trial", "book a call", "upgrade", "add seats", "license", "contract");
        if (ContainsAny(text, "receipt", "payment received", "order confirmation", "your purchase"))
        {
            rules.Add("sales_ignore_receipt");
            return Result(EmailClassificationDomains.Sales, EmailClassificationIntents.Receipt, 0.94m,
                "The message looks like a receipt or order confirmation, not a sales lead.", EmailClassificationActions.Ignore, false, rules, "receipt");
        }

        if (ContainsAny(text, "invoice", "amount due", "payment due", "faktura") && !hasBuyingIntent)
        {
            rules.Add("sales_ignore_invoice");
            return Result(EmailClassificationDomains.Sales, EmailClassificationIntents.Invoice, 0.94m,
                "The message looks like an invoice or payment request, not a sales lead.", EmailClassificationActions.Ignore, false, rules, "invoice");
        }

        var hasUpsellIntent = ContainsAny(text, "upgrade", "add seats", "more licenses", "expand", "pricing");
        if (ContainsAny(text, "support ticket", "case number", "bug", "not working", "refund") && !hasUpsellIntent)
        {
            rules.Add("sales_ignore_support");
            return Result(EmailClassificationDomains.Sales, EmailClassificationIntents.SupportRequest, 0.91m,
                "The message is a support request without buying or expansion intent.", EmailClassificationActions.Ignore, false, rules, "support_ticket_without_upsell_intent");
        }

        if (hasBuyingIntent)
        {
            rules.Add("sales_buying_signal");
            return Result(EmailClassificationDomains.Sales, EmailClassificationIntents.SalesLead, 0.72m,
                "A buying, evaluation, pricing, demo, or expansion signal was detected and should be checked semantically.", EmailClassificationActions.CreateSalesLeadDraft, true, rules,
                urgency: ResolveUrgency(text), productOrServiceInterest: ExtractProductInterest(text));
        }

        return Result(EmailClassificationDomains.Unknown, EmailClassificationIntents.Unknown, 0.45m,
            "No deterministic Sales lead signal was strong enough to act on.", EmailClassificationActions.HumanReview, true, rules);
    }

    private static EmailClassificationResult ClassifySupport(string text, List<string> rules)
    {
        if (SupportCaseNumber().IsMatch(text))
        {
            rules.Add("support_case_number");
            return Result(EmailClassificationDomains.Support, EmailClassificationIntents.SupportRequest, 0.95m,
                "A support case number was found.", EmailClassificationActions.CreateSupportCase, false, rules);
        }

        if (ContainsAny(text, "support", "help", "not working", "bug", "error", "refund", "case", "issue", "problem", "can't login", "cannot login"))
        {
            rules.Add("support_keyword");
            return Result(EmailClassificationDomains.Support, EmailClassificationIntents.SupportRequest, 0.78m,
                "Support request language was detected in the Support mailbox.", EmailClassificationActions.CreateSupportCase, true, rules);
        }

        return Result(EmailClassificationDomains.Unknown, EmailClassificationIntents.Unknown, 0.45m,
            "No deterministic Support request signal was strong enough to act on.", EmailClassificationActions.HumanReview, true, rules);
    }

    private async Task<EmailClassificationResult?> ClassifyWithAiAsync(EmailClassificationRequest request, EmailClassificationResult deterministic, CancellationToken cancellationToken)
    {
        var department = request.MailboxPurpose.ToString();
        var agent = await _dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.Department == department)
            .OrderBy(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (agent is null)
        {
            _logger.LogInformation("Mailbox AI classification skipped because no {Department} agent is onboarded. CompanyId: {CompanyId}.", department, request.CompanyId);
            return null;
        }

        var sources = BuildSources(request);
        if (sources.Count == 0)
        {
            return null;
        }

        try
        {
            var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(
                request.CompanyId,
                agent.Id,
                ResolveCapability(request.MailboxPurpose),
                "1.0.0",
                "hybrid-email-classification-v1",
                "1.0.0",
                "Classify the newest inbound email for the configured mailbox purpose. Begin the summary exactly with domain=<finance|sales|support|unknown>; intent=<invoice|receipt|supplier_subscription_agreement|sales_lead|support_request|newsletter|non_business|operational|unknown>; action=<ignore|route_to_finance_review|create_sales_lead_draft|create_support_case|human_review>; confidence=<0.00-1.00>; urgency=<low|medium|high|none>; product=<short product or none>; evidence=<short safe rationale>. Respect the mailbox purpose as the owner. Do not recommend sending, paying, posting, approving, or externally executing anything.",
                sources,
                ["recommend"],
                []), cancellationToken);

            if (result.Status == AgentAiRunStatuses.Failed || result.Confidence < 0.55m)
            {
                return null;
            }

            return MapAiResult(result, deterministic);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Mailbox AI classification failed safely. CompanyId: {CompanyId}. Purpose: {Purpose}.", request.CompanyId, request.MailboxPurpose);
            return null;
        }
    }

    private static EmailClassificationResult? MapAiResult(AgentReasoningResult result, EmailClassificationResult deterministic)
    {
        var fields = result.Summary.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0].ToLowerInvariant(), x => x[1], StringComparer.OrdinalIgnoreCase);
        if (!fields.TryGetValue("domain", out var domain) || !fields.TryGetValue("intent", out var intent) || !fields.TryGetValue("action", out var action))
        {
            return null;
        }

        domain = NormalizeDomain(domain);
        intent = NormalizeIntent(intent);
        action = NormalizeAction(action);
        var confidence = fields.TryGetValue("confidence", out var confidenceText) && decimal.TryParse(confidenceText, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0m, 1m)
            : Math.Clamp(result.Confidence, 0m, 1m);
        var evidence = fields.GetValueOrDefault("evidence");
        if (string.IsNullOrWhiteSpace(evidence))
        {
            evidence = result.Summary;
        }

        var urgency = fields.GetValueOrDefault("urgency");
        if (string.Equals(urgency, "none", StringComparison.OrdinalIgnoreCase)) urgency = null;
        var product = fields.GetValueOrDefault("product");
        if (string.Equals(product, "none", StringComparison.OrdinalIgnoreCase)) product = null;
        var requiresReview = confidence < HighConfidence || action is EmailClassificationActions.HumanReview or EmailClassificationActions.CreateSalesLeadDraft or EmailClassificationActions.RouteToFinanceReview;
        return new EmailClassificationResult(
            domain,
            intent,
            confidence,
            evidence.Trim(),
            action,
            requiresReview,
            deterministic.UsedDeterministicRules,
            true,
            deterministic.RuleMatches.Concat(["ai_semantic_classification"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ResolveIgnoreReason(intent, action),
            urgency,
            product);
    }

    private static bool ShouldUseAi(EmailClassificationRequest request, EmailClassificationResult deterministic)
    {
        if (!request.AllowAi || request.Messages.Count == 0)
        {
            return false;
        }

        if (deterministic.RecommendedAction == EmailClassificationActions.Ignore && deterministic.Confidence >= HighConfidence)
        {
            return false;
        }

        if (deterministic.Confidence < ReviewConfidence)
        {
            return true;
        }

        return deterministic.Intent is EmailClassificationIntents.SalesLead or EmailClassificationIntents.SupportRequest or EmailClassificationIntents.SupplierSubscriptionAgreement;
    }

    private static IReadOnlyList<AgentAiSource> BuildSources(EmailClassificationRequest request) =>
        request.Messages.TakeLast(10).Select(message => new AgentAiSource(
            $"mailbox-email:{NormalizeId(message.ProviderMessageId)}",
            "inbound_email",
            string.IsNullOrWhiteSpace(message.Subject) ? "Inbound email" : Bound(message.Subject, 200),
            $"Purpose {request.MailboxPurpose}; From {Bound(message.Sender.DisplayName, 100)} <{Bound(message.Sender.Email, 256)}>; subject {Bound(message.Subject, 300)}; body {Bound(message.PlainTextBody ?? StripHtml(message.HtmlBody), 3000)}",
            message.ReceivedUtc)).ToArray();

    private static string CollectText(EmailClassificationRequest request)
    {
        var summary = request.Summary;
        var latest = request.Messages.LastOrDefault();
        var attachmentText = summary is null ? null : string.Join(" ", summary.AttachmentSummaries.Select(x => $"{x.FileName} {x.MimeType} {x.UntrustedExtractedText}"));
        return string.Join(" ", new[]
        {
            summary?.Subject,
            summary?.Snippet,
            summary?.BodyPreview,
            summary?.FromAddress,
            summary?.FolderDisplayName,
            attachmentText,
            latest?.Subject,
            latest?.Sender.Email,
            latest?.PlainTextBody,
            StripHtml(latest?.HtmlBody)
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static EmailClassificationResult Result(string domain, string intent, decimal confidence, string evidence, string action, bool requiresReview, IReadOnlyList<string> rules, string? ignoreReason = null, string? urgency = null, string? productOrServiceInterest = null) =>
        new(domain, intent, confidence, evidence, action, requiresReview, true, false, rules.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), ignoreReason, urgency, productOrServiceInterest);

    private static string ResolveCapability(MailboxPurpose purpose) => purpose switch
    {
        MailboxPurpose.Finance => AgentCapabilityIds.FinancePayables,
        MailboxPurpose.Sales => AgentCapabilityIds.SalesLeadIntelligence,
        MailboxPurpose.Support => AgentCapabilityIds.SupportTriageAnalysis,
        _ => AgentCapabilityIds.WorkPrioritization
    };

    private static string NormalizeDomain(string value) => value.Trim().ToLowerInvariant() switch
    {
        EmailClassificationDomains.Finance => EmailClassificationDomains.Finance,
        EmailClassificationDomains.Sales => EmailClassificationDomains.Sales,
        EmailClassificationDomains.Support => EmailClassificationDomains.Support,
        _ => EmailClassificationDomains.Unknown
    };

    private static string NormalizeIntent(string value) => value.Trim().ToLowerInvariant() switch
    {
        EmailClassificationIntents.Invoice => EmailClassificationIntents.Invoice,
        EmailClassificationIntents.Receipt => EmailClassificationIntents.Receipt,
        EmailClassificationIntents.SupplierSubscriptionAgreement => EmailClassificationIntents.SupplierSubscriptionAgreement,
        EmailClassificationIntents.SalesLead => EmailClassificationIntents.SalesLead,
        EmailClassificationIntents.SupportRequest => EmailClassificationIntents.SupportRequest,
        EmailClassificationIntents.Newsletter => EmailClassificationIntents.Newsletter,
        EmailClassificationIntents.NonBusiness => EmailClassificationIntents.NonBusiness,
        EmailClassificationIntents.Operational => EmailClassificationIntents.Operational,
        _ => EmailClassificationIntents.Unknown
    };

    private static string NormalizeAction(string value) => value.Trim().ToLowerInvariant() switch
    {
        EmailClassificationActions.Ignore => EmailClassificationActions.Ignore,
        EmailClassificationActions.RouteToFinanceReview => EmailClassificationActions.RouteToFinanceReview,
        EmailClassificationActions.CreateSalesLeadDraft => EmailClassificationActions.CreateSalesLeadDraft,
        EmailClassificationActions.CreateSupportCase => EmailClassificationActions.CreateSupportCase,
        _ => EmailClassificationActions.HumanReview
    };

    private static string? ResolveIgnoreReason(string intent, string action) => action == EmailClassificationActions.Ignore
        ? intent switch
        {
            EmailClassificationIntents.Newsletter => "newsletter",
            EmailClassificationIntents.Receipt => "receipt",
            EmailClassificationIntents.Invoice => "invoice",
            EmailClassificationIntents.SupportRequest => "support_ticket_without_upsell_intent",
            _ => "insufficient_signal"
        }
        : null;

    private static string ResolveUrgency(string text) => ContainsAny(text, "urgent", "as soon as possible", "this week", "today", "tomorrow") ? "high" : ContainsAny(text, "next month", "later", "no rush") ? "low" : "medium";
    private static string? ExtractProductInterest(string text)
    {
        var match = ProductRegex().Match(text);
        return match.Success ? Bound(match.Groups["product"].Value, 120) : null;
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    private static string Normalize(string value) => Whitespace().Replace(value.ToLowerInvariant().Replace('ö', 'o').Replace('ä', 'a').Replace('å', 'a').Replace('é', 'e'), " ").Trim();
    private static string NormalizeId(string? value) => string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : new(value.Where(char.IsLetterOrDigit).Take(80).ToArray());
    private static string Bound(string? value, int max) { var text = Whitespace().Replace(value ?? string.Empty, " ").Trim(); return text.Length <= max ? text : text[..max]; }
    private static string? StripHtml(string? value) => string.IsNullOrWhiteSpace(value) ? null : Html().Replace(value, " ");

    [GeneratedRegex("\\s+")] private static partial Regex Whitespace();
    [GeneratedRegex("<.*?>")] private static partial Regex Html();
    [GeneratedRegex("SUP-[0-9]{8}-[0-9]{4}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex SupportCaseNumber();
    [GeneratedRegex("(?:for|about|regarding) (?<product>[a-z0-9][a-z0-9 \\-]{2,80})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)] private static partial Regex ProductRegex();
}