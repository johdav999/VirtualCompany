using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Features.Marketing;

public static class MarketingEvidencePresentation
{
    public static string CalendarKind(string kind) => kind switch
    {
        "plan_span" => "Plan period", "campaign_planning" => "Campaign planning", "campaign_launch" => "Campaign launch",
        "campaign_review" => "Campaign review", "campaign_end" => "Campaign end", "activity" => "Campaign activity",
        "content_due" => "Content due", _ => Friendly(kind)
    };
    public static string StrategyWorkshopUrl(GuidedWorkSessionViewModel draft) => $"/agents/{draft.AgentId:D}/workshops/marketing_strategy?companyId={draft.CompanyId:D}&sessionId={draft.Id:D}";
    public static string SegmentWorkshopUrl(GuidedWorkSessionViewModel draft) => $"/agents/{draft.AgentId:D}/workshops/marketing_segment?companyId={draft.CompanyId:D}&sessionId={draft.Id:D}";
    public static string PlanWorkshopUrl(GuidedWorkSessionViewModel draft) => $"/agents/{draft.AgentId:D}/workshops/marketing_plan?companyId={draft.CompanyId:D}&sessionId={draft.Id:D}";
    public static bool ShowDraftField(GuidedDraftFieldViewModel field) => field.Value is not null && !field.Path.Equals("workshop_insights", StringComparison.OrdinalIgnoreCase);
    public static string DisplayDraftValue(System.Text.Json.Nodes.JsonNode? value) => value is System.Text.Json.Nodes.JsonValue scalar && scalar.TryGetValue<string>(out var text) ? text : value?.ToJsonString() ?? "Not yet recorded";
    public static string DraftSummary(System.Text.Json.Nodes.JsonNode? value)
    {
        var text = DisplayDraftValue(value).ReplaceLineEndings(" ");
        return text.Length > 120 ? text[..120] + "…" : text;
    }
    public static IReadOnlyList<string> ParseStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
    public static string FriendlyMetric(string value) => value.Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal);
    public static string MetricValue(MarketingMetricViewModel metric) => metric.Value is decimal value ? $"{value:N1} {metric.Unit}".Trim() : "No data";
    public static string Friendly(string value) => value.Replace("_", " ", StringComparison.Ordinal).Replace('-', ' ');
    public static string JsonSummary(string value, string fallback = "Not yet recorded")
    { if (string.IsNullOrWhiteSpace(value) || value is "{}" or "[]") return fallback; var clean = value.Trim().Trim('{', '}', '[', ']').Replace('"', ' ').Replace(':', ' '); return clean.Length > 140 ? clean[..140] + "…" : clean; }
    public static string Size(MarketingSegmentVersionViewModel value) => value.SizeLow.HasValue || value.SizeHigh.HasValue ? $"{value.SizeLow?.ToString("N0") ?? "?"}–{value.SizeHigh?.ToString("N0") ?? "?"} ({Friendly(value.SizeMethod)})" : "Not estimated";

}
