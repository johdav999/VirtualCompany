namespace VirtualCompany.Web.Services;

public static class InvoiceWorkflowPresentation
{
    public static FinanceInvoiceRecommendationDetailsResponse? ResolveRecommendationDetails(
        FinanceInvoiceReviewDetailResponse? detail)
    {
        if (detail is null)
        {
            return null;
        }

        return NormalizeRecommendation(detail.RecommendationDetails)
            ?? NormalizeRecommendation(new FinanceInvoiceRecommendationDetailsResponse
            {
                Risk = detail.RiskLevel,
                RationaleSummary = detail.RecommendationSummary,
                Confidence = detail.Confidence,
                RecommendedAction = detail.RecommendedAction,
                CurrentWorkflowStatus = detail.RecommendationStatus
            });
    }

    public static FinanceInvoiceRecommendationDetailsResponse? ResolveRecommendationDetails(
        FinanceInvoiceDetailResponse? detail)
    {
        if (detail is null)
        {
            return null;
        }

        return NormalizeRecommendation(detail.RecommendationDetails)
            ?? NormalizeRecommendation(detail.WorkflowContext is null
                ? null
                : new FinanceInvoiceRecommendationDetailsResponse
                {
                    Classification = detail.WorkflowContext.Classification,
                    Risk = detail.WorkflowContext.RiskLevel,
                    RationaleSummary = detail.WorkflowContext.Rationale,
                    Confidence = detail.WorkflowContext.Confidence,
                    RecommendedAction = detail.WorkflowContext.RecommendedAction,
                    CurrentWorkflowStatus = detail.WorkflowContext.ReviewTaskStatus
                });
    }

    public static IReadOnlyList<FinanceInvoiceWorkflowHistoryItemResponse> NormalizeWorkflowHistory(
        IEnumerable<FinanceInvoiceWorkflowHistoryItemResponse>? items)
    {
        if (items is null)
        {
            return [];
        }

        var orderedItems = items
            .Where(item => item is not null)
            .Select((item, index) => new { Item = CloneHistoryItem(item), Index = index })
            .OrderBy(entry => entry.Item.OccurredAtUtc == default ? 1 : 0)
            .ThenByDescending(entry => entry.Item.OccurredAtUtc)
            .ThenByDescending(entry => GetHistoryItemCompleteness(entry.Item))
            .ThenBy(entry => NormalizeText(entry.Item.EventType) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => NormalizeText(entry.Item.ActorOrSourceDisplayName) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToList();

        var uniqueItems = new List<FinanceInvoiceWorkflowHistoryItemResponse>(orderedItems.Count);
        var seenEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in orderedItems)
        {
            var normalizedEventId = NormalizeEventId(item.EventId);
            if (normalizedEventId is not null && !seenEventIds.Add(normalizedEventId))
            {
                continue;
            }

            item.EventId = normalizedEventId ?? string.Empty;
            item.EventType = NormalizeText(item.EventType) ?? "Review";
            item.ActorOrSourceDisplayName = NormalizeText(item.ActorOrSourceDisplayName) ?? "System";
            uniqueItems.Add(item);
        }

        return uniqueItems;
    }

    private static FinanceInvoiceRecommendationDetailsResponse? NormalizeRecommendation(
        FinanceInvoiceRecommendationDetailsResponse? input)
    {
        if (input is null)
        {
            return null;
        }

        var recommendation = new FinanceInvoiceRecommendationDetailsResponse
        {
            Classification = NormalizeText(input.Classification) ?? string.Empty,
            Risk = NormalizeText(input.Risk) ?? string.Empty,
            RationaleSummary = NormalizeText(input.RationaleSummary) ?? string.Empty,
            Confidence = Math.Clamp(input.Confidence, 0m, 1m),
            RecommendedAction = NormalizeText(input.RecommendedAction) ?? string.Empty,
            CurrentWorkflowStatus = NormalizeText(input.CurrentWorkflowStatus) ?? string.Empty
        };

        return string.IsNullOrWhiteSpace(recommendation.Classification) &&
               string.IsNullOrWhiteSpace(recommendation.Risk) &&
               string.IsNullOrWhiteSpace(recommendation.RationaleSummary) &&
               string.IsNullOrWhiteSpace(recommendation.RecommendedAction) &&
               string.IsNullOrWhiteSpace(recommendation.CurrentWorkflowStatus) &&
               recommendation.Confidence <= 0m
            ? null
            : recommendation;
    }

    private static FinanceInvoiceWorkflowHistoryItemResponse CloneHistoryItem(
        FinanceInvoiceWorkflowHistoryItemResponse item) =>
        new()
        {
            EventId = item.EventId,
            EventType = item.EventType,
            ActorOrSourceDisplayName = item.ActorOrSourceDisplayName,
            OccurredAtUtc = item.OccurredAtUtc,
            RelatedAuditId = item.RelatedAuditId,
            RelatedApprovalId = item.RelatedApprovalId
        };

    private static int GetHistoryItemCompleteness(FinanceInvoiceWorkflowHistoryItemResponse item) =>
        (item.OccurredAtUtc == default ? 0 : 1) +
        (NormalizeText(item.EventType) is null ? 0 : 1) +
        (NormalizeText(item.ActorOrSourceDisplayName) is null ? 0 : 1) +
        (item.RelatedAuditId.HasValue ? 1 : 0) +
        (item.RelatedApprovalId.HasValue ? 1 : 0);

    private static string? NormalizeEventId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}