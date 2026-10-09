namespace VirtualCompany.Web.Services;

public sealed record CompanyPeriodFigureViewModel(string Label, string Value, string Context, string? DeepLink = null);
public sealed record CompanyPeriodAreaViewModel(string Lens, string Title, IReadOnlyList<CompanyPeriodFigureViewModel> Figures,
    string DeepLink, string Coverage);

// Presentation of the authorized period payload only: no new business calculations or source reads.
public static class CompanyPeriodOverviewPresentation
{
    public static IReadOnlyList<CompanyPeriodAreaViewModel> Weekly(WeeklyWorkspaceViewModel workspace) =>
        workspace.Contributions.Where(x => workspace.AvailableLenses.Any(a => a.Value == x.Lens))
            .OrderBy(x => Order(x.Lens)).Select(area =>
            {
                var origin = DashboardRoutes.BuildWeeklyPath(workspace.CompanyId, workspace.ActiveLens, workspace.Period.WeekStart);
                var figures = area.Metrics.Where(x => x.Key is not ("company.blocker_movement" or "sales.pipeline_movement" or "support.backlog_movement"))
                    .OrderBy(x => x.Key.StartsWith("finance.cash.", StringComparison.Ordinal) ? 0 : 1)
                    .Select(metric => new CompanyPeriodFigureViewModel(metric.Label, metric.DisplayValue,
                        metric.Kind switch { "balance" => "At weekly cutoff", "current_due_schedule" => "Current due schedule", _ => "Selected week" },
                        DashboardRoutes.WithQuery(origin, ("metric", metric.Key)))).ToList();
                if (area.Metrics.Count > 0)
                    figures.Add(new("Recorded current follow-ups", area.Commitments.Count.ToString(), "Included current risks and commitments"));
                return new CompanyPeriodAreaViewModel(area.Lens, Title(area.Lens), figures,
                    DashboardRoutes.BuildWeeklyPath(workspace.CompanyId, area.Lens, workspace.Period.WeekStart),
                    area.Metrics.Count == 0 ? string.Join(" ", area.CoverageGaps) : "Recorded period evidence; current follow-ups are observed at refresh. Open the review for source dates and history coverage.");
            }).ToList();

    public static IReadOnlyList<CompanyPeriodAreaViewModel> Monthly(MonthlyWorkspaceViewModel workspace, Guid? snapshotId) =>
        workspace.Sections.Where(x => workspace.AvailableLenses.Any(a => a.Value == x.Lens))
            .OrderBy(x => Order(x.Lens)).Select(area => new CompanyPeriodAreaViewModel(area.Lens, Title(area.Lens),
                area.Facts.OrderBy(x => FactOrder(x.Label)).Select(fact => new CompanyPeriodFigureViewModel(fact.Label, fact.Value,
                    fact.Label switch
                    {
                        "Cash" or "Receivables" or "Payables" or "Runway" => "Month-end cutoff",
                        "Current pipeline" or "Current customer risks" or "Unresolved blockers" or "Budget/autonomy constraints" => "Current recorded position",
                        "Next-month tasks" => "Next-period commitments",
                        _ => "Selected month"
                    })).ToList(),
                DashboardRoutes.EnsureWorkspaceContext(area.SetupDeepLink ?? area.DeepLink, workspace.CompanyId,
                    DashboardRoutes.BuildMonthlyPath(workspace.CompanyId, workspace.ActiveLens, workspace.Period.Year, workspace.Period.Month, snapshotId)),
                area.CoverageSummary)).ToList();

    private static int Order(string lens) => lens switch { "finance" => 0, "sales" => 1, "marketing" => 2, "customers" => 3, _ => 4 };
    private static int FactOrder(string label) => label switch { "Cash" or "Current pipeline" => 0, "Receivables" => 1, "Payables" => 2, "Net result" => 3, _ => 4 };
    private static string Title(string lens) => lens switch { "finance" => "Finance", "sales" => "Sales", "marketing" => "Marketing", "customers" => "Customers", _ => "Company commitments" };
}
