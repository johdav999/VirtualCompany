using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitDashboardPageTests
{
    private static readonly DateTime DashboardGeneratedUtc = new(2026, 4, 16, 8, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime CashLastRefreshedUtc = new(2026, 4, 16, 8, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void Executive_cockpit_finance_widget_renders_snapshot_alert_links_and_actions()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var alertId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");
        var invoiceId = Guid.Parse("7edb1408-42d4-4a79-9aa2-3a2d2c84da3f");
        var transactionId = Guid.Parse("f749fce1-6277-4ca3-b4ee-fae9016f2cc8");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId, alertId, invoiceId, transactionId, "critical", "Critical", "22 days"));

        using var harness = CreateDashboardHarness(companyId, dashboard);
        harness.Navigation.NavigateTo($"http://localhost/executive-cockpit?companyId={companyId:D}");

        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("USD 125,400.25", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent);
            Assert.Contains("USD 18,400.00", cut.Find("[data-testid='executive-cockpit-expected-incoming-cash']").TextContent);
            Assert.Contains("USD 12,650.00", cut.Find("[data-testid='executive-cockpit-expected-outgoing-cash']").TextContent);
            Assert.Contains("USD 7,200.00", cut.Find("[data-testid='executive-cockpit-overdue-receivables']").TextContent);
            Assert.Contains("USD 24,300.00", cut.Find("[data-testid='executive-cockpit-upcoming-payables']").TextContent);
            Assert.DoesNotContain("Connect accounting", cut.Markup);

            Assert.Contains("Cash runway dropped below the critical threshold.", cut.Find("[data-testid='executive-cockpit-low-cash-alert']").TextContent);
            Assert.Contains("Average monthly burn is USD 18,500.00.", cut.Find("[data-testid='executive-cockpit-low-cash-alert']").TextContent);

            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext(FinanceRoutes.BuildAlertDetailPath(alertId, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
                cut.Find("[data-testid='executive-cockpit-low-cash-alert-open-detail']").GetAttribute("href"));

            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
                cut.Find("[data-testid='finance-deep-link-finance-workspace']").GetAttribute("href"));
            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.Anomalies, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
                cut.Find("[data-testid='finance-deep-link-anomaly-workbench']").GetAttribute("href"));
            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
                cut.Find("[data-testid='finance-deep-link-cash-detail']").GetAttribute("href"));

            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId)),
                cut.Find("[data-testid='finance-action-open-finance-summary']").GetAttribute("href"));
            Assert.Equal("Review invoice", cut.Find("[data-testid='finance-action-review-invoice']").TextContent.Trim());
            Assert.Equal("Inspect anomaly", cut.Find("[data-testid='finance-action-inspect-anomaly']").TextContent.Trim());
            Assert.Equal("View cash position", cut.Find("[data-testid='finance-action-view-cash-position']").TextContent.Trim());
        });
    }

    [Theory]
    [InlineData("healthy", "Healthy", "180 days", "finance-risk-pill--healthy", true)]
    [InlineData("warning", "Warning", "65 days", "finance-risk-pill--warning", true)]
    [InlineData("critical", "Critical", "22 days", "finance-risk-pill--critical", true)]
    [InlineData("missing", "Missing", "Unavailable", "finance-risk-pill--missing", false)]
    public void Executive_cockpit_finance_snapshot_renders_cash_metrics_or_empty_state(
        string runwayStatus,
        string runwayLabel,
        string runwayDisplay,
        string expectedClass,
        bool hasFinanceData)
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId,
                Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e"),
                Guid.Parse("7edb1408-42d4-4a79-9aa2-3a2d2c84da3f"),
                Guid.Parse("f749fce1-6277-4ca3-b4ee-fae9016f2cc8"),
                runwayStatus,
                runwayLabel,
                runwayDisplay));

        using var harness = CreateDashboardHarness(companyId, dashboard);
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() =>
        {
            if (hasFinanceData)
            {
                Assert.Contains("USD 125,400.25", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent);
                Assert.Contains("USD 18,400.00", cut.Find("[data-testid='executive-cockpit-expected-incoming-cash']").TextContent);
                Assert.Contains("USD 12,650.00", cut.Find("[data-testid='executive-cockpit-expected-outgoing-cash']").TextContent);
                Assert.Contains("USD 24,300.00", cut.Find("[data-testid='executive-cockpit-upcoming-payables']").TextContent);
            }
            else
            {
                Assert.NotNull(cut.Find("[data-testid='connect-accounting-cta']"));
            }
        });
    }

    [Fact]
    public void Executive_cockpit_finance_snapshot_refreshes_widget_values_from_live_api_responses()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(
            companyId,
            Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e"),
            Guid.Parse("7edb1408-42d4-4a79-9aa2-3a2d2c84da3f"),
            Guid.Parse("f749fce1-6277-4ca3-b4ee-fae9016f2cc8"),
            "critical",
            "Critical",
            "22 days"));

        var snapshotRequestCount = 0;
        using var harness = CreateDashboardHarness(
            companyId,
            dashboard,
            financeSnapshotFactory: () => snapshotRequestCount++ == 0
                ? CreateFinanceSnapshot(companyId, "critical")
                : new DashboardFinanceSnapshotViewModel
                {
                    CompanyId = companyId,
                    CurrentCashBalance = 1150m,
                    ExpectedIncomingCash = 1500m,
                    ExpectedOutgoingCash = 725m,
                    OverdueReceivables = 300m,
                    UpcomingPayables = 350m,
                    Currency = "USD",
                    AsOfUtc = CashLastRefreshedUtc.AddMinutes(10),
                    UpcomingWindowDays = 30,
                    Cash = 1150m,
                    BurnRate = 0m,
                    RunwayDays = null,
                    RiskLevel = "healthy",
                    HasFinanceData = true
                });
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() => Assert.Contains("USD 125,400.25", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Refresh snapshot").Click();
        cut.WaitForAssertion(() => { Assert.Equal(2, snapshotRequestCount); Assert.Contains("USD 1,150.00", cut.Find("[data-testid='executive-cockpit-cash-position']").TextContent); Assert.Contains("USD 1,500.00", cut.Find("[data-testid='executive-cockpit-expected-incoming-cash']").TextContent); Assert.Contains("USD 725.00", cut.Find("[data-testid='executive-cockpit-expected-outgoing-cash']").TextContent); Assert.Contains("USD 300.00", cut.Find("[data-testid='executive-cockpit-overdue-receivables']").TextContent); Assert.Contains("USD 350.00", cut.Find("[data-testid='executive-cockpit-upcoming-payables']").TextContent); });
    }

    [Fact]
    public void Executive_cockpit_renders_business_signals_panel_instead_of_legacy_summary_kpis()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "warning", "Warning", "65 days"));
        using var harness = CreateDashboardHarness(companyId, dashboard);
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='business-signals-panel']")));
    }

    [Fact]
    public void Executive_cockpit_renders_financial_health_top_actions_and_grouped_finance_feed()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "warning", "Warning", "65 days"));

        dashboard.Finance!.TopActions[0].OccurrenceCount = 2;
        dashboard.Finance.InsightsFeed[0].OccurrenceCount = 3;

        using var harness = CreateDashboardHarness(companyId, dashboard);
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("[data-testid='executive-cockpit-financial-health']"));
            Assert.NotNull(cut.Find("[data-testid='executive-cockpit-top-finance-actions']"));
            Assert.NotNull(cut.Find("[data-testid='executive-cockpit-finance-insights-feed']"));
            Assert.Contains("2 occurrences", cut.Find("[data-testid='executive-cockpit-top-finance-actions']").TextContent);
            Assert.Contains("3 occurrences", cut.Find("[data-testid='executive-cockpit-finance-insights-feed']").TextContent);
            Assert.Contains("Financial health", cut.Markup);
            Assert.Contains("Top finance actions", cut.Markup);
        });
    }

    [Fact]
    public void Executive_cockpit_finance_actions_call_backend_orchestration_endpoints_and_follow_routes()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var alertId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");
        var invoiceId = Guid.Parse("7edb1408-42d4-4a79-9aa2-3a2d2c84da3f");
        var transactionId = Guid.Parse("f749fce1-6277-4ca3-b4ee-fae9016f2cc8");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId, alertId, invoiceId, transactionId, "critical", "Critical", "22 days"));

        using var harness = CreateDashboardHarness(
            companyId,
            dashboard,
            (request, _) => Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                var path when request.Method == HttpMethod.Post &&
                               path == $"/internal/companies/{companyId:D}/finance/invoices/{invoiceId:D}/review-workflow" =>
                    CreateJsonResponse(new { workflowInstanceId = Guid.NewGuid() }),
                var path when request.Method == HttpMethod.Post &&
                               path == $"/internal/companies/{companyId:D}/finance/transactions/{transactionId:D}/anomaly-evaluation" =>
                    CreateJsonResponse(new { evaluationId = Guid.NewGuid() }),
                var path when request.Method == HttpMethod.Post &&
                               path == $"/internal/companies/{companyId:D}/finance/cash-position/evaluation" =>
                    CreateJsonResponse(new FinanceCashPositionResponse
                    {
                        CompanyId = companyId,
                        AsOfUtc = CashLastRefreshedUtc,
                        AvailableBalance = 125400.25m,
                        Currency = "USD",
                        AverageMonthlyBurn = 18500m,
                        EstimatedRunwayDays = 22,
                        Classification = "cash_position",
                        RiskLevel = "critical",
                        RecommendedAction = "Escalate",
                        Rationale = "Runway below policy threshold.",
                        Confidence = 0.94m,
                        SourceWorkflow = "cash-position-evaluation"
                    }),
                _ => CreateNotFoundResponse()
            }));

        harness.Navigation.NavigateTo($"http://localhost/executive-cockpit?companyId={companyId:D}");
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() => Assert.Equal(0, harness.FinanceRequests.Count));

        cut.Find("[data-testid='finance-action-review-invoice']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(harness.FinanceRequests, request => request.Path == $"/internal/companies/{companyId:D}/finance/invoices/{invoiceId:D}/review-workflow");
            Assert.Equal(companyId.ToString(), harness.FinanceRequests.Last().CompanyHeaderValue);
            Assert.Equal($"http://localhost{DashboardRoutes.EnsureCompanyContext(FinanceRoutes.BuildInvoiceReviewDetailPath(invoiceId, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId))}", harness.Navigation.Uri);
        });

        cut.Find("[data-testid='finance-action-inspect-anomaly']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(harness.FinanceRequests, request => request.Path == $"/internal/companies/{companyId:D}/finance/transactions/{transactionId:D}/anomaly-evaluation");
            Assert.Equal($"http://localhost{DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.Anomalies, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId))}", harness.Navigation.Uri);
        });

        cut.Find("[data-testid='finance-action-view-cash-position']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(harness.FinanceRequests, request => request.Path == $"/internal/companies/{companyId:D}/finance/cash-position/evaluation");
            Assert.Equal($"http://localhost{DashboardRoutes.EnsureCompanyContext(FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId), companyId, FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, companyId))}", harness.Navigation.Uri);
            Assert.Contains("Cash position refreshed.", cut.Markup);
        });
    }

    [Fact]
    public void Executive_cockpit_department_cards_limit_visible_signals_to_three_and_hide_zero_values()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, CreateFinanceSection(companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "warning", "Warning", "65 days"));
        dashboard.DepartmentSections =
        [
            new DepartmentDashboardSectionViewModel
            {
                Id = Guid.NewGuid(),
                DepartmentKey = "operations",
                DisplayName = "Operations",
                DisplayOrder = 0,
                IsVisible = true,
                SummaryCounts = new Dictionary<string, int>
                {
                    ["open_tasks"] = 12,
                    ["blocked_workflows"] = 3,
                    ["pending_approvals"] = 2,
                    ["zero_metric"] = 0,
                    ["overflow_metric"] = 99
                },
                Navigation = new DepartmentDashboardNavigationViewModel
                {
                    Label = "Open operations",
                    Route = $"/tasks?companyId={companyId:D}&view=operations"
                },
                EmptyState = new DepartmentDashboardEmptyStateViewModel
                {
                    Title = "No department data yet",
                    Message = "Department metrics will appear after activity is generated."
                }
            }
        ];

        using var harness = CreateDashboardHarness(companyId, dashboard);
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);
        cut.WaitForAssertion(() =>
        {
            Assert.Equal(3, cut.FindAll(".department-section-card__metrics div").Count);
            Assert.Equal(
                new[] { "Blocked Workflows", "Pending Approvals", "Open Tasks" },
                cut.FindAll(".department-section-card__metrics dt").Select(node => node.TextContent.Trim()).ToArray());
            Assert.Equal(
                DashboardRoutes.EnsureCompanyContext($"/tasks?companyId={companyId:D}&view=operations", companyId, "/agents"),
                cut.Find(".department-section-card__link").GetAttribute("href"));
        });

        Assert.DoesNotContain("Zero Metric", cut.Markup);
        Assert.DoesNotContain("Overflow Metric", cut.Markup);
    }

    [Fact]
    public void Executive_cockpit_hides_finance_section_when_backend_omits_finance_payload()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var dashboard = CreateDashboard(companyId, finance: null);

        using var harness = CreateDashboardHarness(companyId, dashboard);
        var cut = RenderDashboard(harness, companyId, dashboard.CompanyName);

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='executive-cockpit-finance-widget']"));
            Assert.DoesNotContain("Review invoice", cut.Markup);
            Assert.DoesNotContain("Inspect anomaly", cut.Markup);
            Assert.DoesNotContain("View cash position", cut.Markup);
            Assert.DoesNotContain("Open finance summary", cut.Markup);
        });
    }

    private static IRenderedComponent<ExecutiveCockpitDashboard> RenderDashboard(
        DashboardHarness harness,
        Guid companyId,
        string companyName) =>
        harness.Context.RenderComponent<ExecutiveCockpitDashboard>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.CompanyName, companyName));

    private static DashboardHarness CreateDashboardHarness(
        Guid companyId,
        ExecutiveCockpitDashboardViewModel dashboard,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? financeHandler = null,
        Func<DashboardFinanceSnapshotViewModel>? financeSnapshotFactory = null)
    {
        var context = new TestContext();
        var financeRequests = new List<CapturedFinanceRequest>();
        context.Services.AddLogging();

        context.Services.AddSingleton(new ExecutiveCockpitApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                var path when request.Method == HttpMethod.Get &&
                               path == $"/api/companies/{companyId:D}/executive-cockpit" =>
                    CreateJsonResponse(dashboard),
                var path when request.Method == HttpMethod.Get &&
                               path == $"/api/companies/{companyId:D}/executive-cockpit/kpis" =>
                    CreateJsonResponse(new ExecutiveCockpitKpiDashboardViewModel
                    {
                        CompanyId = companyId,
                        GeneratedAtUtc = DashboardGeneratedUtc,
                        StartUtc = DashboardGeneratedUtc.AddDays(-7),
                        EndUtc = DashboardGeneratedUtc
                    }),
                var path when request.Method == HttpMethod.Get &&
                               path == $"/api/companies/{companyId:D}/executive-cockpit/widgets/finance" =>
                    CreateJsonResponse(new ExecutiveCockpitWidgetPayloadViewModel<ExecutiveCockpitFinanceViewModel>
                    {
                        CompanyId = companyId,
                        WidgetKey = "finance",
                        GeneratedAtUtc = DashboardGeneratedUtc,
                        Payload = dashboard.Finance
                    }),
                var path when request.Method == HttpMethod.Get &&
                               path == "/api/dashboard/finance-snapshot" =>
                    CreateJsonResponse((financeSnapshotFactory ?? (() => CreateFinanceSnapshot(companyId, dashboard.Finance?.Runway.Status ?? "critical")))()),
                var path when request.Method == HttpMethod.Get &&
                               path == $"/api/companies/{companyId:D}/executive-cockpit/widgets/business-signals" =>
                    CreateJsonResponse(new ExecutiveCockpitWidgetPayloadViewModel<List<BusinessSignalViewModel>>
                    {
                        CompanyId = companyId,
                        WidgetKey = "business-signals",
                        GeneratedAtUtc = DashboardGeneratedUtc,
                        Payload = dashboard.BusinessSignals
                    }),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, cancellationToken) =>
        {
            financeRequests.Add(new CapturedFinanceRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-Company-Id", out var values) ? values.Single() : null));

            return financeHandler is null
                ? Task.FromResult(CreateNotFoundResponse())
                : financeHandler(request, cancellationToken);
        })) { BaseAddress = new Uri("http://localhost/") }));

        return new DashboardHarness(context, context.Services.GetRequiredService<FakeNavigationManager>(), financeRequests);
    }

    private static ExecutiveCockpitDashboardViewModel CreateDashboard(Guid companyId, ExecutiveCockpitFinanceViewModel? finance) =>
        new()
        {
            CompanyId = companyId,
            CompanyName = "Contoso Labs",
            GeneratedAtUtc = DashboardGeneratedUtc,
            BusinessSignals =
            [
                new BusinessSignalViewModel
                {
                    Type = "operationalLoad",
                    Severity = "warning",
                    Title = "Operational load is building",
                    Summary = "Open work is rising faster than available capacity.",
                    ActionLabel = "Open tasks",
                    ActionUrl = $"/tasks?companyId={companyId:D}",
                    DetectedAtUtc = DashboardGeneratedUtc
                }
            ],
            Finance = finance,
            SummaryKpis = [],
            PendingApprovals = new ExecutiveCockpitPendingApprovalsViewModel
            {
                TotalCount = 0,
                Route = $"/approvals?companyId={companyId:D}"
            },
            Alerts = [],
            DepartmentKpis = [],
            DepartmentSections = [],
            RecentActivity = [],
            SetupState = new ExecutiveCockpitSetupStateViewModel
            {
                IsInitialSetupEmpty = true
            },
            EmptyStateFlags = new ExecutiveCockpitEmptyStateFlagsViewModel
            {
                NoAgents = true,
                NoWorkflows = true,
                NoKnowledge = true,
                NoRecentActivity = true,
                NoPendingApprovals = true,
                NoAlerts = finance?.LowCashAlert is null
            }
        };

    private static DashboardFinanceSnapshotViewModel CreateFinanceSnapshot(Guid companyId, string runwayStatus) =>
        runwayStatus == "missing"
            ? new DashboardFinanceSnapshotViewModel
            {
                CompanyId = companyId,
                CurrentCashBalance = 0m,
                ExpectedIncomingCash = 0m,
                ExpectedOutgoingCash = 0m,
                OverdueReceivables = 0m,
                UpcomingPayables = 0m,
                Currency = "USD",
                AsOfUtc = CashLastRefreshedUtc,
                UpcomingWindowDays = 30,
                Cash = 0m,
                BurnRate = 0m,
                HasFinanceData = false,
                RunwayDays = null,
                RiskLevel = "missing"
            }
            : new DashboardFinanceSnapshotViewModel
            {
                CompanyId = companyId,
                CurrentCashBalance = 125400.25m,
                ExpectedIncomingCash = 18400m,
                ExpectedOutgoingCash = 12650m,
                OverdueReceivables = 7200m,
                UpcomingPayables = 24300m,
                Currency = "USD",
                AsOfUtc = CashLastRefreshedUtc,
                UpcomingWindowDays = 30,
                Cash = 125400.25m,
                BurnRate = 18500m / 30m,
                RunwayDays = runwayStatus == "healthy" ? 180 : runwayStatus == "warning" ? 65 : 22,
                RiskLevel = runwayStatus,
                HasFinanceData = true
            };
}