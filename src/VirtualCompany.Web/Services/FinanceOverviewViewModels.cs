namespace VirtualCompany.Web.Services;

public sealed class FinanceOverviewViewModel
{
    public IReadOnlyList<FinanceKpiViewModel> Kpis { get; init; } = [];
    public FinanceManagerInsightViewModel ManagerInsight { get; init; } = new();
    public IReadOnlyList<FinanceAttentionItemViewModel> AttentionItems { get; init; } = [];
    public FinanceCashPositionOverviewViewModel CashPosition { get; init; } = new();
    public FinanceMonthlySummaryOverviewViewModel MonthlySummary { get; init; } = new();
    public IReadOnlyList<RecentFinanceActivityViewModel> RecentActivity { get; init; } = [];
    public bool HasNoFinanceActivity { get; init; }
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
    public string? Icon { get; init; }
    public string Href { get; init; } = "#";
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

public sealed class FinanceCashPositionOverviewViewModel
{
    public string CurrentBalance { get; init; } = string.Empty;
    public string ComparisonText { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public string Href { get; init; } = "#";
    public FinanceKpiTone Tone { get; init; } = FinanceKpiTone.Neutral;
}

public sealed class FinanceMonthlySummaryOverviewViewModel
{
    public string Period { get; init; } = string.Empty;
    public string TotalIncome { get; init; } = string.Empty;
    public string TotalExpenses { get; init; } = string.Empty;
    public string NetResult { get; init; } = string.Empty;
    public string Href { get; init; } = "#";
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
