using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class SharedSalesEmailIntentExtractionService(
    VirtualCompanyDbContext db,
    IAgentReasoningGateway reasoning,
    ILogger<SharedSalesEmailIntentExtractionService> logger) : ISalesEmailIntentExtractionService
{
    public async Task<SalesEmailIntentExtractionResult?> ExtractAsync(
        SalesEmailIntentExtractionRequest request, CancellationToken cancellationToken)
    {
        if (request.Messages.Count == 0) return null;
        var agentId = await db.Agents.AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.Department == "Sales")
            .OrderBy(x => x.CreatedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!agentId.HasValue)
        {
            logger.LogWarning("Sales email intent AI skipped because no Sales agent is onboarded. CompanyId: {CompanyId}.", request.CompanyId);
            return null;
        }

        var sources = request.Messages.TakeLast(10).Select(message => new AgentAiSource(
            $"sales-email:{NormalizeId(message.ProviderMessageId)}", "inbound_sales_email",
            string.IsNullOrWhiteSpace(message.Subject) ? "Inbound sales email" : Normalize(message.Subject, 200),
            $"From {Normalize(message.Sender.DisplayName, 100)} <{Normalize(message.Sender.Email, 256)}>; subject {Normalize(message.Subject, 300)}; body {Normalize(message.PlainTextBody ?? StripHtml(message.HtmlBody), 3000)}",
            message.ReceivedUtc)).ToArray();
        try
        {
            var result = await reasoning.ReasonAsync(new AgentReasoningRequest(request.CompanyId, agentId.Value,
                AgentCapabilityIds.SalesLeadIntelligence, "1.0.0", "sales-email-intent-v2", "1.0.0",
                "Classify the newest inbound email as sales_lead, ignore, or uncertain. Begin the summary exactly with classification=<value>; ignore_reason=<allowed value or none>; urgency=<low|medium|high>; intent=<short intent or none>. Use sales_lead only for explicit buying, demo, pricing, quote, evaluation, contract, or expansion intent. Allowed ignore reasons are newsletter, receipt, invoice, support_ticket_without_upsell_intent, non_sales_operational, insufficient_signal. Do not infer identity beyond the supplied sender.",
                sources, ["recommend"], []), cancellationToken);
            if (result.Status == AgentAiRunStatuses.Failed || result.Confidence < .55m) return null;
            return Map(result, request.Messages.Last());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Shared Sales email intent analysis failed safely. CompanyId: {CompanyId}.", request.CompanyId);
            return null;
        }
    }

    private static SalesEmailIntentExtractionResult? Map(AgentReasoningResult result, MailboxInboundMessage latest)
    {
        var fields = result.Summary.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0].ToLowerInvariant(), x => x[1].ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
        if (!fields.TryGetValue("classification", out var classification) || classification is not ("sales_lead" or "ignore" or "uncertain")) return null;
        if (classification != SalesEmailIntentClassifications.SalesLead)
        {
            var reason = fields.GetValueOrDefault("ignore_reason");
            var allowedReason = reason is "newsletter" or "receipt" or "invoice" or "support_ticket_without_upsell_intent" or "non_sales_operational" or "insufficient_signal"
                ? reason : SalesEmailIgnoreReasons.InsufficientSignal;
            return new SalesEmailIntentExtractionResult(classification, null, allowedReason, result.Summary);
        }

        var email = latest.Sender.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) return null;
        var urgency = fields.GetValueOrDefault("urgency") is "low" or "high" ? fields["urgency"] : SalesEmailUrgencies.Medium;
        var intent = fields.GetValueOrDefault("intent");
        if (string.IsNullOrWhiteSpace(intent) || intent == "none") intent = SalesEmailIntents.BuyingInterest;
        return new SalesEmailIntentExtractionResult(classification,
            new SalesEmailSignalResult(email, latest.Sender.DisplayName, null, intent, null, urgency, result.Confidence),
            null, result.Summary);
    }

    private static string NormalizeId(string? value) => string.IsNullOrWhiteSpace(value)
        ? Guid.NewGuid().ToString("N") : new(value.Where(char.IsLetterOrDigit).Take(80).ToArray());
    private static string Normalize(string? value, int max) { var text = Whitespace().Replace(value ?? string.Empty, " ").Trim(); return text.Length <= max ? text : text[..max]; }
    private static string? StripHtml(string? value) => string.IsNullOrWhiteSpace(value) ? null : Html().Replace(value, " ");
    [GeneratedRegex("\\s+")] private static partial Regex Whitespace();
    [GeneratedRegex("<.*?>")] private static partial Regex Html();
}
