using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class OpenAiSalesEmailIntentExtractionService : ISalesEmailIntentExtractionService
{
    public const string ClientName = "sales-email-intent-extraction";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedClassifications = new(StringComparer.OrdinalIgnoreCase)
    {
        SalesEmailIntentClassifications.SalesLead,
        SalesEmailIntentClassifications.Ignore,
        SalesEmailIntentClassifications.Uncertain
    };
    private static readonly HashSet<string> AllowedIgnoreReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        SalesEmailIgnoreReasons.Newsletter,
        SalesEmailIgnoreReasons.Receipt,
        SalesEmailIgnoreReasons.Invoice,
        SalesEmailIgnoreReasons.SupportTicket,
        SalesEmailIgnoreReasons.NonSalesOperational,
        SalesEmailIgnoreReasons.InsufficientSignal
    };
    private static readonly HashSet<string> AllowedUrgencies = new(StringComparer.OrdinalIgnoreCase)
    {
        SalesEmailUrgencies.Low,
        SalesEmailUrgencies.Medium,
        SalesEmailUrgencies.High
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SalesEmailIntentExtractionOptions _options;
    private readonly ILogger<OpenAiSalesEmailIntentExtractionService> _logger;

    public OpenAiSalesEmailIntentExtractionService(
        IHttpClientFactory httpClientFactory,
        IOptions<SalesEmailIntentExtractionOptions> options,
        ILogger<OpenAiSalesEmailIntentExtractionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SalesEmailIntentExtractionResult?> ExtractAsync(
        SalesEmailIntentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanUseOpenAi() || request.Messages.Count == 0)
        {
            return null;
        }

        try
        {
            var response = await RequestStructuredExtractionAsync(request, cancellationToken);
            return TryMap(response, request.Messages.Last(), out var result, out var validationError)
                ? result
                : LogInvalidResult(request, validationError);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(
                ex,
                "Sales email intent extraction fell back to deterministic rules. CompanyId: {CompanyId}, Provider: {Provider}, MailboxConnectionId: {MailboxConnectionId}.",
                request.CompanyId,
                request.Provider,
                request.MailboxConnectionId);
            return null;
        }
    }

    private bool CanUseOpenAi() =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.Model);

    private async Task<StructuredExtractionPayload> RequestStructuredExtractionAsync(
        SalesEmailIntentExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 120));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var chatRequest = new ChatCompletionRequest
        {
            Model = _options.Model,
            Messages =
            [
                new ChatMessage(
                    "system",
                    "You classify inbound mailbox messages for a tenant-scoped sales workflow. Return only valid JSON matching the requested schema. Do not invent facts. Classify as sales_lead only when the sender shows buying, demo, pricing, quote, evaluation, contract, or expansion intent. Classify newsletters, receipts, invoices, support issues without upsell intent, and operational emails as ignore with a concrete ignoreReason. Use confidence from 0.0 to 1.0."),
                new ChatMessage("user", BuildPrompt(request))
            ],
            Temperature = 0m,
            MaxTokens = 500,
            ResponseFormat = new ResponseFormat(
                "json_schema",
                new JsonSchemaFormat(
                    "sales_email_intent",
                    true,
                    new
                    {
                        type = "object",
                        additionalProperties = false,
                        required = new[]
                        {
                            "classification",
                            "senderEmail",
                            "contactName",
                            "companyName",
                            "intent",
                            "productOrServiceInterest",
                            "urgency",
                            "confidence",
                            "ignoreReason",
                            "reasonSummary"
                        },
                        properties = new
                        {
                            classification = new { type = "string", @enum = new[] { "sales_lead", "ignore", "uncertain" } },
                            senderEmail = new { type = new[] { "string", "null" } },
                            contactName = new { type = new[] { "string", "null" } },
                            companyName = new { type = new[] { "string", "null" } },
                            intent = new { type = new[] { "string", "null" } },
                            productOrServiceInterest = new { type = new[] { "string", "null" } },
                            urgency = new { type = new[] { "string", "null" }, @enum = new object?[] { "low", "medium", "high", null } },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                            ignoreReason = new { type = new[] { "string", "null" }, @enum = new object?[] { "newsletter", "receipt", "invoice", "support_ticket_without_upsell_intent", "non_sales_operational", "insufficient_signal", null } },
                            reasonSummary = new { type = "string" }
                        }
                    }))
        };

        using var httpResponse = await client.PostAsJsonAsync("chat/completions", chatRequest, SerializerOptions, cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Sales email extraction provider returned {(int)httpResponse.StatusCode}: {body}",
                null,
                httpResponse.StatusCode);
        }

        var chatResponse = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Sales email extraction provider returned an empty response.");
        var content = chatResponse.Choices
            .Select(choice => choice.Message?.Content?.Trim())
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Sales email extraction provider returned no structured content.");
        }

        return JsonSerializer.Deserialize<StructuredExtractionPayload>(content, SerializerOptions)
            ?? throw new InvalidOperationException("Sales email extraction provider returned empty JSON.");
    }

    private SalesEmailIntentExtractionResult? LogInvalidResult(
        SalesEmailIntentExtractionRequest request,
        string validationError)
    {
        _logger.LogWarning(
            "Sales email intent extraction returned invalid structured output and will use fallback. CompanyId: {CompanyId}, Provider: {Provider}, MailboxConnectionId: {MailboxConnectionId}, ValidationError: {ValidationError}.",
            request.CompanyId,
            request.Provider,
            request.MailboxConnectionId,
            validationError);
        return null;
    }

    private static bool TryMap(
        StructuredExtractionPayload payload,
        MailboxInboundMessage latest,
        out SalesEmailIntentExtractionResult? result,
        out string validationError)
    {
        result = null;
        validationError = string.Empty;

        var classification = NormalizeToken(payload.Classification);
        if (classification is null || !AllowedClassifications.Contains(classification))
        {
            validationError = "classification is missing or unsupported.";
            return false;
        }

        if (payload.Confidence is < 0m or > 1m)
        {
            validationError = "confidence must be between 0 and 1.";
            return false;
        }

        var rationale = NormalizeOptional(payload.ReasonSummary, 1000);
        if (string.IsNullOrWhiteSpace(rationale))
        {
            validationError = "reasonSummary is required.";
            return false;
        }

        if (string.Equals(classification, SalesEmailIntentClassifications.SalesLead, StringComparison.OrdinalIgnoreCase))
        {
            var senderEmail = NormalizeEmail(payload.SenderEmail) ?? NormalizeEmail(latest.Sender.Email);
            var intent = NormalizeOptional(payload.Intent, 120);
            var urgency = NormalizeToken(payload.Urgency) ?? SalesEmailUrgencies.Medium;
            if (senderEmail is null)
            {
                validationError = "senderEmail is required for sales leads.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(intent))
            {
                validationError = "intent is required for sales leads.";
                return false;
            }

            if (!AllowedUrgencies.Contains(urgency))
            {
                validationError = "urgency is unsupported.";
                return false;
            }

            result = new SalesEmailIntentExtractionResult(
                classification,
                new SalesEmailSignalResult(
                    senderEmail,
                    NormalizeOptional(payload.ContactName, 160) ?? NormalizeOptional(latest.Sender.DisplayName, 160),
                    NormalizeOptional(payload.CompanyName, 200),
                    intent,
                    NormalizeOptional(payload.ProductOrServiceInterest, 200),
                    urgency,
                    payload.Confidence),
                null,
                rationale);
            return true;
        }

        var ignoreReason = NormalizeToken(payload.IgnoreReason);
        if (string.IsNullOrWhiteSpace(ignoreReason) || !AllowedIgnoreReasons.Contains(ignoreReason))
        {
            validationError = "ignoreReason is required for ignored or uncertain messages.";
            return false;
        }

        result = new SalesEmailIntentExtractionResult(
            classification,
            null,
            ignoreReason,
            rationale);
        return true;
    }

    private static string BuildPrompt(SalesEmailIntentExtractionRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Return JSON with fields: classification, senderEmail, contactName, companyName, intent, productOrServiceInterest, urgency, confidence, ignoreReason, reasonSummary.");
        builder.AppendLine("Allowed classifications: sales_lead, ignore, uncertain.");
        builder.AppendLine("Allowed ignore reasons: newsletter, receipt, invoice, support_ticket_without_upsell_intent, non_sales_operational, insufficient_signal.");
        builder.AppendLine("Allowed urgency values for sales leads: low, medium, high.");
        builder.AppendLine();
        builder.AppendLine("Messages, oldest to newest:");
        foreach (var message in request.Messages)
        {
            builder.AppendLine("---");
            builder.Append("Message ID: ").AppendLine(message.ProviderMessageId);
            builder.Append("Thread ID: ").AppendLine(message.ProviderThreadId);
            builder.Append("From: ").Append(message.Sender.DisplayName).Append(" <").Append(message.Sender.Email).AppendLine(">");
            builder.Append("Subject: ").AppendLine(NormalizePromptText(message.Subject, 300));
            builder.Append("Received UTC: ").AppendLine(message.ReceivedUtc?.ToString("O") ?? "unknown");
            builder.AppendLine("Body:");
            builder.AppendLine(NormalizePromptText(message.PlainTextBody ?? StripHtml(message.HtmlBody), 4000));
        }

        return builder.ToString().Trim();
    }

    private static string NormalizePromptText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = WhitespaceRegex.Replace(value, " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? StripHtml(string? html) =>
        string.IsNullOrWhiteSpace(html) ? null : Regex.Replace(html, "<.*?>", " ");

    private static string? NormalizeEmail(string? value)
    {
        var normalized = NormalizeOptional(value, 256)?.ToLowerInvariant();
        return normalized is not null && normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }

    private static string? NormalizeToken(string? value) =>
        NormalizeOptional(value, 120)?.ToLowerInvariant();

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex.Replace(value, " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public sealed class SalesEmailIntentExtractionOptions
    {
        public const string SectionName = "SalesEmailIntentExtraction";

        public bool Enabled { get; set; } = true;
        public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4.1-mini";
        public int TimeoutSeconds { get; set; } = 20;
    }

    private sealed class StructuredExtractionPayload
    {
        public string? Classification { get; set; }
        public string? SenderEmail { get; set; }
        public string? ContactName { get; set; }
        public string? CompanyName { get; set; }
        public string? Intent { get; set; }
        public string? ProductOrServiceInterest { get; set; }
        public string? Urgency { get; set; }
        public decimal Confidence { get; set; }
        public string? IgnoreReason { get; set; }
        public string? ReasonSummary { get; set; }
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public decimal Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ResponseFormat(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("json_schema")] JsonSchemaFormat JsonSchema);

    private sealed record JsonSchemaFormat(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("strict")] bool Strict,
        [property: JsonPropertyName("schema")] object Schema);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatChoiceMessage? Message { get; set; }
    }

    private sealed class ChatChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}