namespace VirtualCompany.Web.Services;

// Presentation only: every value is supplied by the existing authorized department projection.
public static class CompanyHealthPresentation
{
    public sealed record DepartmentIndicator(string Label, int? Count = null, decimal? Amount = null, string? Currency = null);

    public static IReadOnlyList<DepartmentIndicator> Indicators(TodayWorkspaceViewModel w, string lens) => lens switch
    {
        "finance" when w.Finance is { } finance => [
            new("HealthOverdueReceivables", finance.OverdueReceivables),
            new("HealthDuePayables", finance.DuePayables),
            new("HealthReconciliationExceptions", finance.ReconciliationExceptions)],
        "sales" when w.Sales is { } sales => [
            new("TodayPipeline", Amount: sales.PipelineValue, Currency: sales.Currency),
            new("TodayDealsAttention", sales.DealsNeedingAttention), new("TodayHotLeads", sales.HotLeads)],
        "marketing" when w.Marketing is { } marketing => [
            new("HealthDueLaunches", marketing.DueLaunches), new("TodayContentDue", marketing.DueContentItems),
            new("HealthSpendExceptions", marketing.SpendExceptions), new("HealthAttributionGaps", marketing.AttributionGaps)],
        "customers" when w.Support is { } support => [
            new("TodayOpenCases", support.OpenCases), new("TodaySlaBreached", support.SlaBreached),
            new("TodaySlaRisk", support.SlaAtRisk), new("HealthWaitingCases", support.WaitingCases),
            new("TodayAwaitingApproval", support.AwaitingApproval)],
        _ => []
    };

    public static TodayWorkspacePriorityViewModel? DepartmentPriority(TodayWorkspaceViewModel w, string lens) =>
        w.Priorities.Concat(w.CompanyRisks ?? []).Where(x => x.Lens == lens).OrderBy(x => x.Rank).FirstOrDefault();

    public static TodayWorkspacePriorityViewModel? FindRisk(TodayWorkspaceViewModel workspace, string? key) =>
        workspace.Priorities.Concat(workspace.ActiveLens == "company" ? workspace.CompanyRisks ?? [] : [])
            .FirstOrDefault(x => x.Key == key);
    public static string DepartmentTitle(string lens) => lens switch
    { "finance" => "TodayFinance", "sales" => "TodaySales", "marketing" => "TodayMarketing", "customers" => "TodayCustomerSupport", _ => "TodayCompany" };
    public static bool Available(TodayWorkspaceViewModel w, string lens) => lens switch
    { "finance" => w.Finance?.IsAvailable == true, "sales" => w.Sales?.IsAvailable == true,
      "marketing" => w.Marketing?.IsAvailable == true, "customers" => w.Support?.IsAvailable == true, _ => false };
    public static DateTime Observed(TodayWorkspaceViewModel w, string lens) => lens switch
    { "finance" => w.Finance?.ObservedAtUtc ?? DateTime.MinValue, "sales" => w.Sales?.ObservedAtUtc ?? DateTime.MinValue,
      "marketing" => w.Marketing?.ObservedAtUtc ?? DateTime.MinValue, "customers" => w.Support?.ObservedAtUtc ?? DateTime.MinValue, _ => DateTime.MinValue };
    public static string Route(Guid company, string lens) => DashboardRoutes.WithQuery(lens switch
    { "finance" => "/finance", "sales" => "/app/sales", "marketing" => "/marketing", "customers" => "/support", _ => "/dashboard" }, ("companyId", company.ToString("D")));
    public static bool Stale(TodayWorkspaceViewModel w, DateTime observed) => observed == DateTime.MinValue || observed > w.GeneratedAtUtc || observed < w.GeneratedAtUtc.AddMinutes(-15);
}
