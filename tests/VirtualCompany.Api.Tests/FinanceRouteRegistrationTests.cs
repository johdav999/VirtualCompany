using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceRouteRegistrationTests
{
    [Theory]
    [InlineData(typeof(FinancePage), FinanceRoutes.Home)]
    [InlineData(typeof(CashPositionPage), FinanceRoutes.CashPosition)]
    [InlineData(typeof(TransactionsPage), FinanceRoutes.Transactions)]
    [InlineData(typeof(TransactionsPage), FinanceRoutes.TransactionDetail)]
    [InlineData(typeof(InvoiceReviewsPage), FinanceRoutes.Reviews)]
    [InlineData(typeof(InvoiceReviewDetailPage), FinanceRoutes.ReviewDetail)]
    [InlineData(typeof(InvoicesPage), FinanceRoutes.Invoices)]
    [InlineData(typeof(InvoicesPage), FinanceRoutes.InvoiceDetail)]
    [InlineData(typeof(BalancesPage), FinanceRoutes.Balances)]
    [InlineData(typeof(SandboxAdminPage), FinanceRoutes.SandboxAdmin)]
    [InlineData(typeof(MonthlySummaryPage), FinanceRoutes.MonthlySummary)]
    [InlineData(typeof(TransparencyEventsPage), FinanceRoutes.TransparencyEvents)]
    [InlineData(typeof(TransparencyEventsPage), FinanceRoutes.TransparencyEventDetail)]
    [InlineData(typeof(TransparencyToolRegistryPage), FinanceRoutes.TransparencyToolRegistry)]
    [InlineData(typeof(TransparencyToolExecutionsPage), FinanceRoutes.TransparencyToolExecutions)]
    [InlineData(typeof(TransparencyToolExecutionsPage), FinanceRoutes.TransparencyToolExecutionDetail)]
    [InlineData(typeof(AnomaliesPage), FinanceRoutes.Anomalies)]
    [InlineData(typeof(AnomalyDetailPage), FinanceRoutes.AnomalyDetail)]
    [InlineData(typeof(LowCashAlertPage), FinanceRoutes.AlertDetail)]
    public void Finance_pages_register_expected_routes(Type componentType, string expectedRoute)
    {
        var routes = componentType.GetCustomAttributes(inherit: false)
            .OfType<RouteAttribute>()
            .Select(attribute => attribute.Template)
            .ToArray();

        Assert.Contains(expectedRoute, routes);
    }

    [Fact]
    public void Finance_landing_page_defines_required_shell_links()
    {
        var routes = FinanceRoutes.SectionPages.Select(route => route.Path).ToArray();

        Assert.Equal(11, routes.Length);
        Assert.Contains(FinanceRoutes.CashPosition, routes);
        Assert.Contains(FinanceRoutes.Transactions, routes);
        Assert.Contains(FinanceRoutes.Reviews, routes);
        Assert.Contains(FinanceRoutes.Invoices, routes);
        Assert.Contains(FinanceRoutes.Balances, routes);
        Assert.Contains(FinanceRoutes.TransparencyEvents, routes);
        Assert.Contains(FinanceRoutes.TransparencyToolRegistry, routes);
        Assert.Contains(FinanceRoutes.TransparencyToolExecutions, routes);
        Assert.Contains(FinanceRoutes.SandboxAdmin, routes);
        Assert.Contains(FinanceRoutes.MonthlySummary, routes);
        Assert.Contains(FinanceRoutes.Anomalies, routes);
    }

    [Fact]
    public void Finance_sandbox_admin_navigation_entry_is_role_aware()
    {
        var managerRoutes = FinanceRoutes.SectionPages.Where(route => route.IsVisibleTo("manager")).Select(route => route.Path).ToArray();
        var adminRoutes = FinanceRoutes.SectionPages.Where(route => route.IsVisibleTo("admin")).Select(route => route.Path).ToArray();
        var testerRoutes = FinanceRoutes.SectionPages.Where(route => route.IsVisibleTo("tester")).Select(route => route.Path).ToArray();

        Assert.DoesNotContain(FinanceRoutes.TransparencyEvents, managerRoutes);
        Assert.DoesNotContain(FinanceRoutes.TransparencyToolRegistry, managerRoutes);
        Assert.DoesNotContain(FinanceRoutes.TransparencyToolExecutions, managerRoutes);
        Assert.DoesNotContain(FinanceRoutes.SandboxAdmin, managerRoutes);
        Assert.Contains(FinanceRoutes.TransparencyEvents, adminRoutes);
        Assert.Contains(FinanceRoutes.TransparencyToolRegistry, adminRoutes);
        Assert.Contains(FinanceRoutes.TransparencyToolExecutions, adminRoutes);
        Assert.Contains(FinanceRoutes.SandboxAdmin, adminRoutes);
        Assert.Contains(FinanceRoutes.TransparencyEvents, testerRoutes);
        Assert.Contains(FinanceRoutes.SandboxAdmin, testerRoutes);
    }

    [Fact]
    public void Finance_alert_detail_routes_preserve_company_context()
    {
        var companyId = Guid.NewGuid();
        var alertId = Guid.NewGuid();

        var route = FinanceRoutes.BuildAlertDetailPath(alertId, companyId);

        Assert.Equal($"/finance/alerts/{alertId:D}?companyId={companyId:D}", route);
    }
}
