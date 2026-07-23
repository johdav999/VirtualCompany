using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Common;
using VirtualCompany.Application.Focus;
using VirtualCompany.Application.Insights;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyDashboardBriefingSummaryService : IDashboardBriefingSummaryService
{
    public const string ClientName = "dashboard-briefing-summary";

    private readonly IFocusEngine _focusEngine;
    private readonly IActionInsightService _actionInsightService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DashboardBriefingSummaryOptions _options;
    private readonly ILogger<CompanyDashboardBriefingSummaryService> _logger;

    public CompanyDashboardBriefingSummaryService(
        IFocusEngine focusEngine,
        IActionInsightService actionInsightService,
        IHttpClientFactory httpClientFactory,
        IOptions<DashboardBriefingSummaryOptions> options,
        ILogger<CompanyDashboardBriefingSummaryService> logger)
    {
        _focusEngine = focusEngine;
        _actionInsightService = actionInsightService;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DashboardBriefingSummaryDto> GenerateAsync(
        GenerateDashboardBriefingSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var focusItems = await _focusEngine.GetFocusAsync(
            new GetDashboardFocusQuery(query.CompanyId, query.UserId),
            cancellationToken);
        var actionItems = await _actionInsightService.GetTopActionsAsync(
            query.CompanyId,
            Math.Max(1, query.ActionItemCount),
            cancellationToken);

        var trimmedFocusItems = focusItems
            .Select(DisplayTextMapper.MapFocusItem)
            .Take(Math.Max(1, query.FocusItemCount))
            .ToArray();
        var trimmedActionItems = actionItems
            .Select(DisplayTextMapper.MapActionItem)
            .GroupBy(item => $"{item.Title}|{item.Reason}|{item.Priority}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(Math.Max(1, query.ActionItemCount))
            .ToArray();

        if (CanUseOpenAi())
        {
            try
            {
                var summary = await GenerateWithOpenAiAsync(trimmedFocusItems, trimmedActionItems, cancellationToken);
                return new DashboardBriefingSummaryDto(
                    summary,
                    DateTime.UtcNow,
                    true,
                    _options.Model);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogWarning(
                    ex,
                    "Dashboard briefing summary generation fell back to local rendering for company {CompanyId}.",
                    query.CompanyId);
            }
        }

        return new DashboardBriefingSummaryDto(
            BuildFallbackSummary(trimmedFocusItems, trimmedActionItems),
            DateTime.UtcNow,
            false,
            null);
    }

    private bool CanUseOpenAi() =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_options.Model);

    private async Task<string> GenerateWithOpenAiAsync(
        IReadOnlyList<FocusItemDto> focusItems,
        IReadOnlyList<ActionQueueItemDto> actionItems,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        client.BaseAddress = new Uri(_options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        var request = new ChatCompletionRequest
        {
            Model = _options.Model,
            Messages =
            [
                new ChatMessage(
                    "system",
                    "You write short dashboard summaries in clear plain English for small business users. Use only the supplied focus items and action queue items. Produce exactly 4 or 5 simple sentences in plain text. Explain the main issues, why they matter, and what themes are driving today. Do not use technical labels, raw IDs, or code-like terms, and do not invent facts."),
                new ChatMessage("user", BuildPrompt(focusItems, actionItems))
            ],
            Temperature = 0.3m,
            MaxTokens = 220
        };

        using var response = await client.PostAsJsonAsync("chat/completions", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Dashboard summary provider returned {(int)response.StatusCode}: {body}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Dashboard summary provider returned an empty response.");
        var content = payload.Choices
            .Select(choice => choice.Message?.Content?.Trim())
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Dashboard summary provider returned no summary content.");
        }

        return NormalizeSummary(content);
    }

    private static string BuildPrompt(
        IReadOnlyList<FocusItemDto> focusItems,
        IReadOnlyList<ActionQueueItemDto> actionItems)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Today's focus items:");
        if (focusItems.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var item in focusItems)
            {
                builder.Append("- ");
                builder.Append(item.Title);
                builder.Append(": ");
                builder.AppendLine(item.Description);
            }
        }

        builder.AppendLine();
        builder.AppendLine("What to do next items:");
        if (actionItems.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (var item in actionItems)
            {
                builder.Append("- ");
                builder.Append(item.Title);
                builder.Append(" | Priority ");
                builder.Append(item.Priority);
                builder.Append(" | Reason: ");
                builder.Append(item.Reason);
                builder.Append(" | Owner: ");
                builder.AppendLine(item.Owner);
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildFallbackSummary(
        IReadOnlyList<FocusItemDto> focusItems,
        IReadOnlyList<ActionQueueItemDto> actionItems)
    {
        var firstFocus = focusItems.FirstOrDefault();
        var secondFocus = focusItems.Skip(1).FirstOrDefault();
        var firstAction = actionItems.FirstOrDefault();
        var secondAction = actionItems.Skip(1).FirstOrDefault();

        var sentences = new List<string>(5);
        if (firstFocus is not null)
        {
            sentences.Add($"{firstFocus.Title} is the main issue to watch today.");
        }
        else
        {
            sentences.Add("There is no single major issue on the dashboard right now.");
        }

        if (secondFocus is not null)
        {
            sentences.Add($"{secondFocus.Title} is another important issue that needs attention.");
        }
        else if (firstAction is not null)
        {
            sentences.Add($"{firstAction.Title} is the clearest next step for the team.");
        }

        if (firstAction is not null)
        {
            sentences.Add($"{firstAction.Title} is the most urgent action right now. {ToSentence(firstAction.Reason)}");
        }

        if (secondAction is not null)
        {
            sentences.Add($"{secondAction.Title} is also near the top of the list, so today's work is centered on a small number of urgent decisions.");
        }
        else
        {
            sentences.Add("The action list is short, so attention can stay on a few concrete tasks.");
        }

        sentences.Add("Overall, today's priority is to review key items quickly and keep work moving.");
        return NormalizeSummary(string.Join(" ", sentences));
    }

    private static string NormalizeSummary(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Trim();

    private static string ToSentence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "It needs attention.";
        }

        var normalized = value.Trim();
        normalized = char.ToUpperInvariant(normalized[0]) + normalized[1..];
        return normalized.EndsWith(".", StringComparison.Ordinal) ? normalized : $"{normalized}.";
    }

    public sealed class DashboardBriefingSummaryOptions
    {
        public const string SectionName = "DashboardBriefingSummary";

        public bool Enabled { get; set; } = true;
        public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4.1-mini";
        public int TimeoutSeconds { get; set; } = 30;
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
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

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
