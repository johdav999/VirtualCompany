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
    [InlineData(typeof(TransactionsPage), FinanceRoutes.Activity)]
    [InlineData(typeof(TransactionsPage), FinanceRoutes.Transactions)]
    [InlineData(typeof(PaymentsPage), FinanceRoutes.Payments)]
    [InlineData(typeof(PaymentsPage), FinanceRoutes.PaymentDetail)]
    [InlineData(typeof(TransactionsPage), FinanceRoutes.ActivityDetail)]
    [InlineData(typeof(TransactionsPage), FinanceRoutes.TransactionDetail)]
    [InlineData(typeof(InvoiceReviewsPage), FinanceRoutes.Reviews)]
    [InlineData(typeof(InvoiceReviewDetailPage), FinanceRoutes.ReviewDetail)]
    [InlineData(typeof(InvoicesPage), FinanceRoutes.Invoices)]
    [InlineData(typeof(InvoicesPage), FinanceRoutes.InvoiceDetail)]
    [InlineData(typeof(BillsPage), FinanceRoutes.SupplierBills)]
    [InlineData(typeof(BillsPage), FinanceRoutes.SupplierBillDetail)]
    [InlineData(typeof(BillsPage), FinanceRoutes.Bills)]
    [InlineData(typeof(BillsPage), FinanceRoutes.BillDetail)]
    [InlineData(typeof(BillInboxPage), FinanceRoutes.SupplierBillsReview)]
    [InlineData(typeof(BillInboxPage), FinanceRoutes.BillInbox)]
    [InlineData(typeof(BillInboxDetailPage), FinanceRoutes.SupplierBillsReviewDetail)]
    [InlineData(typeof(BillInboxDetailPage), FinanceRoutes.BillInboxDetail)]
    [InlineData(typeof(BalancesPage), FinanceRoutes.Balances)]
    [InlineData(typeof(SandboxAdminPage), FinanceRoutes.SandboxAdmin)]
    [InlineData(typeof(MonthlySummaryPage), FinanceRoutes.MonthlySummary)]
    [InlineData(typeof(TransparencyEventsPage), FinanceRoutes.TransparencyEvents)]
    [InlineData(typeof(TransparencyEventsPage), FinanceRoutes.TransparencyEventDetail)]
    [InlineData(typeof(TransparencyToolRegistryPage), FinanceRoutes.TransparencyToolRegistry)]
    [InlineData(typeof(TransparencyToolExecutionsPage), FinanceRoutes.TransparencyToolExecutions)]
    [InlineData(typeof(TransparencyToolExecutionsPage), FinanceRoutes.TransparencyToolExecutionDetail)]
    [InlineData(typeof(AnomaliesPage), FinanceRoutes.Issues)]
    [InlineData(typeof(AnomaliesPage), FinanceRoutes.Anomalies)]
    [InlineData(typeof(AnomalyDetailPage), FinanceRoutes.IssueDetail)]
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

        Assert.Equal(
            new[]
            {
                FinanceRoutes.Home,
                FinanceRoutes.Invoices,
                FinanceRoutes.SupplierBills,
                FinanceRoutes.Payments,
                FinanceRoutes.Activity,
                FinanceRoutes.Issues,
                FinanceRoutes.Settings
            },
            routes);
        Assert.DoesNotContain(FinanceRoutes.BillInbox, routes);
        Assert.DoesNotContain(FinanceRoutes.Transactions, routes);
        Assert.DoesNotContain(FinanceRoutes.Anomalies, routes);
        Assert.DoesNotContain(FinanceRoutes.TransparencyEvents, routes);
        Assert.DoesNotContain(FinanceRoutes.TransparencyToolRegistry, routes);
        Assert.DoesNotContain(FinanceRoutes.TransparencyToolExecutions, routes);
        Assert.DoesNotContain(FinanceRoutes.SandboxAdmin, routes);
        Assert.DoesNotContain(FinanceRoutes.MonthlySummary, routes);
    }

    [Fact]
    public void Finance_section_pages_group_old_routes_under_new_navigation_items()
    {
        var supplierBills = Assert.Single(FinanceRoutes.SectionPages, route => route.Path == FinanceRoutes.SupplierBills);
        var activity = Assert.Single(FinanceRoutes.SectionPages, route => route.Path == FinanceRoutes.Activity);
        var issues = Assert.Single(FinanceRoutes.SectionPages, route => route.Path == FinanceRoutes.Issues);

        Assert.Contains(FinanceRoutes.Bills, supplierBills.ActivePathPrefixes!);
        Assert.Contains(FinanceRoutes.BillInbox, supplierBills.ActivePathPrefixes!);
        Assert.Contains(FinanceRoutes.Transactions, activity.ActivePathPrefixes!);
        Assert.Contains(FinanceRoutes.Anomalies, issues.ActivePathPrefixes!);
    }

    [Fact]
    public void Finance_system_and_simulation_navigation_entries_are_separate_and_role_aware()
    {
        var managerSimulationRoutes = FinanceRoutes.SimulationLabPages.Where(route => route.IsVisibleTo("manager")).Select(route => route.Path).ToArray();
        var adminSimulationRoutes = FinanceRoutes.SimulationLabPages.Where(route => route.IsVisibleTo("admin")).Select(route => route.Path).ToArray();
        var testerSystemRoutes = FinanceRoutes.SystemAdminPages.Where(route => route.IsVisibleTo("tester")).Select(route => route.Path).ToArray();

        Assert.DoesNotContain(FinanceRoutes.SandboxAdmin, managerSimulationRoutes);
        Assert.Contains(FinanceRoutes.SandboxAdmin, adminSimulationRoutes);
        Assert.Contains(FinanceRoutes.TransparencyEvents, testerSystemRoutes);
        Assert.Contains(FinanceRoutes.TransparencyToolRegistry, testerSystemRoutes);
        Assert.Contains(FinanceRoutes.TransparencyToolExecutions, testerSystemRoutes);
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
