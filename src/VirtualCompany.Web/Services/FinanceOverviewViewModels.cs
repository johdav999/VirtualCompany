namespace VirtualCompany.Web.Services;

public sealed class FinanceOverviewViewModel
{
    public FinanceCashRiskAlertViewModel? CashRiskAlert { get; init; }
    public IReadOnlyList<FinanceKpiViewModel> Kpis { get; init; } = [];
    public FinanceManagerInsightViewModel ManagerInsight { get; init; } = new();
    public FinanceAttentionSummaryViewModel AttentionSummary { get; init; } = new();
    public IReadOnlyList<FinanceAttentionItemViewModel> AttentionItems { get; init; } = [];
    public FinanceCashPositionOverviewViewModel CashPosition { get; init; } = new();
    public FinanceMonthlySummaryOverviewViewModel MonthlySummary { get; init; } = new();
    public IReadOnlyList<RecentFinanceActivityViewModel> RecentActivity { get; init; } = [];
    public bool HasNoFinanceActivity { get; init; }
}

public sealed class FinanceCashRiskAlertViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string BalanceLabel { get; init; } = "Cash balance";
    public string BalanceValue { get; init; } = string.Empty;
    public string RunwayLabel { get; init; } = "Runway";
    public string RunwayValue { get; init; } = string.Empty;
    public string ReasonLabel { get; init; } = "Why it matters";
    public string Reason { get; init; } = string.Empty;
    public string? SupportingText { get; init; }
    public string ActionLabel { get; init; } = string.Empty;
    public string Href { get; init; } = "#";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Warning;
}

public enum FinanceKpiTone
{
    Neutral,
    Positive,
    Warning,
    Danger
}

public sealed class FinanceKpiViewModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? ComparisonText { get; init; }
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
    public FinanceKpiEmphasis Emphasis { get; init; } = FinanceKpiEmphasis.Standard;
    public string? Icon { get; init; }
    public string Href { get; init; } = "#";
}

public enum FinanceKpiEmphasis
{
    Standard,
    Primary,
    Subdued
}

public sealed class FinanceManagerInsightViewModel
{
    public string Name { get; init; } = "Laura";
    public string Role { get; init; } = "Finance Manager";
    public string Status { get; init; } = "Active";
    public string? AvatarUrl { get; init; } = "/images/laura.png";
    public IReadOnlyList<FinanceInsightItemViewModel> Insights { get; init; } = [];
}

public sealed class FinanceInsightItemViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Explanation { get; init; } = string.Empty;
    public string? ActionLabel { get; init; }
    public string? Href { get; init; }
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
    public string? Icon { get; init; }
}

public sealed class FinanceAttentionItemViewModel
{
    public string Label { get; init; } = string.Empty;
    public int Count { get; init; }
    public string? Amount { get; init; }
    public string Href { get; init; } = "#";
    public string CtaLabel { get; init; } = "Review";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
    public string? Icon { get; init; }
}

public sealed class FinanceAttentionSummaryViewModel
{
    public string Title { get; init; } = "Today's finance queue";
    public string Message { get; init; } = "No finance actions need attention right now.";
    public string? Amount { get; init; }
    public string ActionLabel { get; init; } = "Review queue";
    public string Href { get; init; } = "#";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Positive;
}

public sealed class FinanceCashPositionOverviewViewModel
{
    public string Title { get; init; } = "Cash plan snapshot";
    public string CurrentBalance { get; init; } = string.Empty;
    public string ComparisonText { get; init; } = string.Empty;
    public string ContextTitle { get; init; } = "Planning context";
    public string ContextText { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string Href { get; init; } = "#";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
}

public sealed class FinanceMonthlySummaryOverviewViewModel
{
    public bool IsAvailable { get; init; } = true;
    public string Period { get; init; } = string.Empty;
    public string EmptyTitle { get; init; } = "No monthly report available yet.";
    public string EmptyMessage { get; init; } = "A valid reporting period is not available yet.";
    public string TotalIncome { get; init; } = string.Empty;
    public string TotalExpenses { get; init; } = string.Empty;
    public string NetResult { get; init; } = string.Empty;
    public string? CurrencyNote { get; init; }
    public string Href { get; init; } = "#";
    public string ActionLabel { get; init; } = "View report";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
}

public sealed class RecentFinanceActivityViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Amount { get; init; } = string.Empty;
    public string DateText { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string Href { get; init; } = "#";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
    public string? Icon { get; init; }
    public DateTime SortDateUtc { get; init; }
}
