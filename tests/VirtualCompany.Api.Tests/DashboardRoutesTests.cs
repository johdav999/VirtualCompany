using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardRoutesTests
{
    [Fact]
    public void Build_approvals_path_includes_dashboard_context_parameters()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        var route = DashboardRoutes.BuildApprovalsPath(companyId);

        Assert.Equal(
            $"/approvals?companyId={companyId:D}&filter=pending&status=pending&source=dashboard",
            route);
    }

    [Fact]
    public void Ensure_company_context_preserves_task_selection_and_adds_dashboard_source()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var route = DashboardRoutes.EnsureCompanyContext($"/tasks?taskId={taskId:D}", companyId, "/tasks");

        Assert.Contains($"companyId={companyId:D}", route, StringComparison.Ordinal);
        Assert.Contains($"taskId={taskId:D}", route, StringComparison.Ordinal);
        Assert.Contains("source=dashboard", route, StringComparison.Ordinal);
        Assert.DoesNotContain("filter=today", route, StringComparison.Ordinal);
    }

    [Fact]
    public void Ensure_company_context_adds_dashboard_source_to_queue_links()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        var route = DashboardRoutes.EnsureCompanyContext(QueueRoutes.BuildPath(companyId), companyId, QueueRoutes.Home);

        Assert.Equal(
            $"/queue?companyId={companyId:D}&source=dashboard",
            route);
    }

    [Fact]
    public void Ensure_company_context_adds_default_task_context_to_department_links()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        var route = DashboardRoutes.EnsureCompanyContext("/tasks?view=operations", companyId, "/tasks");

        Assert.Equal(
            $"/tasks?view=operations&companyId={companyId:D}&filter=today&status=pending&source=dashboard",
            route);
    }

    [Fact]
    public void Normalize_focus_target_maps_known_dashboard_routes_to_scoped_deep_links()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        var route = DashboardRoutes.NormalizeFocusTarget("/dashboard/briefings", companyId);

        Assert.Equal(
            $"/briefing-preferences?companyId={companyId:D}&source=dashboard",
            route);
    }

    [Fact]
    public void Ensure_company_context_adds_finance_action_context()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");

        var route = DashboardRoutes.EnsureCompanyContext(
            FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
            companyId,
            FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId));

        Assert.Contains($"companyId={companyId:D}", route, StringComparison.Ordinal);
        Assert.Contains("action=view-cash-position", route, StringComparison.Ordinal);
        Assert.Contains("range=this-month", route, StringComparison.Ordinal);
        Assert.Contains("source=dashboard", route, StringComparison.Ordinal);
    }
}