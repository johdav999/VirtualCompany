using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAnomalyWorkbenchPageTests
{
    [Fact]
    public void Anomaly_workbench_page_renders_deduplication_metadata_only_for_rows_that_have_it()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var bothWindowStartUtc = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc);
        var bothWindowEndUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        var windowOnlyStartUtc = new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc);
        var windowOnlyEndUtc = new DateTime(2026, 4, 19, 0, 0, 0, DateTimeKind.Utc);

        using var harness = CreateAnomaliesHarness(
            companyId,
            new FinanceAnomalyWorkbenchResponse
            {
                TotalCount = 4,
                Page = 1,
                PageSize = 50,
                Items =
                [
                    CreateWorkbenchItem("TXN-BOTH", "key-both", bothWindowStartUtc, bothWindowEndUtc),
                    CreateWorkbenchItem("TXN-KEY", "key-only", null, null),
                    CreateWorkbenchItem("TXN-WINDOW", null, windowOnlyStartUtc, windowOnlyEndUtc),
                    CreateWorkbenchItem("TXN-NONE", null, null, null)
                ]
            });

        harness.Navigation.NavigateTo($"http://localhost/finance/anomalies?companyId={companyId:D}");
        var cut = RenderAnomaliesPage(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(4, rows.Count);

            Assert.Contains("TXN-BOTH", rows[0].TextContent);
            Assert.Contains("Deduplication key: key-both", rows[0].TextContent);
            Assert.Contains("Window:", rows[0].TextContent);

            Assert.Contains("TXN-KEY", rows[1].TextContent);
            Assert.Contains("Deduplication key: key-only", rows[1].TextContent);
            Assert.DoesNotContain("Window:", rows[1].TextContent);

            Assert.Contains("TXN-WINDOW", rows[2].TextContent);
            Assert.DoesNotContain("Deduplication key:", rows[2].TextContent);
            Assert.Contains("Window:", rows[2].TextContent);

            Assert.Contains("TXN-NONE", rows[3].TextContent);
            Assert.DoesNotContain("Deduplication key:", rows[3].TextContent);
            Assert.DoesNotContain("Window:", rows[3].TextContent);
            Assert.DoesNotContain("null", rows[3].TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Anomaly_detail_page_renders_deduplication_section_when_metadata_exists()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var anomalyId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");
        var detail = CreateAnomalyDetail(
            anomalyId,
            new FinanceAnomalyDeduplicationResponse
            {
                Key = "finance-transaction-anomaly:dedupe",
                WindowStartUtc = new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                WindowEndUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc)
            });

        using var harness = CreateAnomalyDetailHarness(companyId, anomalyId, detail);
        harness.Navigation.NavigateTo($"http://localhost/finance/anomalies/{anomalyId:D}?companyId={companyId:D}&status=awaiting_approval&supplier=Contoso%20Supplies");

        var cut = RenderAnomalyDetailPage(
            harness,
            companyId,
            anomalyId,
            status: "awaiting_approval",
            supplier: "Contoso Supplies");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deduplication metadata", cut.Markup);
            Assert.Contains("finance-transaction-anomaly:dedupe", cut.Markup);
            Assert.Contains("Window", cut.Markup);
            Assert.Contains("/finance/issues?companyId=4c5cfd22-87fd-4214-b579-fc9e7554ab72&amp;status=awaiting_approval&amp;supplier=Contoso%20Supplies", cut.Markup);
        });
    }

    [Fact]
    public void Anomaly_detail_page_hides_deduplication_section_when_metadata_is_absent()
    {
        var companyId = Guid.Parse("4c5cfd22-87fd-4214-b579-fc9e7554ab72");
        var anomalyId = Guid.Parse("89d7fe3e-3f44-43cf-b383-8f9b4f24cf4e");
        var detail = CreateAnomalyDetail(
            anomalyId,
            new FinanceAnomalyDeduplicationResponse());

        using var harness = CreateAnomalyDetailHarness(companyId, anomalyId, detail);
        harness.Navigation.NavigateTo($"http://localhost/finance/anomalies/{anomalyId:D}?companyId={companyId:D}");

        var cut = RenderAnomalyDetailPage(harness, companyId, anomalyId);

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Deduplication metadata", cut.Markup);
            Assert.DoesNotContain(">Key<", cut.Markup);
            Assert.DoesNotContain(">Window<", cut.Markup);
            Assert.DoesNotContain("null", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IRenderedComponent<AnomaliesPage> RenderAnomaliesPage(
        AnomaliesHarness harness,
        Guid companyId,
        string? type = null,
        string? status = null,
        decimal? confidenceMin = null,
        decimal? confidenceMax = null,
        string? supplier = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? page = null,
        int? pageSize = null) =>
        RenderAnomaliesPageCore(harness, companyId, type, status, confidenceMin, confidenceMax, supplier, dateFrom, dateTo, page, pageSize);

    private static IRenderedComponent<AnomalyDetailPage> RenderAnomalyDetailPage(
        AnomalyDetailHarness harness,
        Guid companyId,
        Guid anomalyId,
        string? type = null,
        string? status = null,
        decimal? confidenceMin = null,
        decimal? confidenceMax = null,
        string? supplier = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        int? page = null,
        int? pageSize = null) =>
        RenderAnomalyDetailPageCore(harness, companyId, anomalyId, type, status, confidenceMin, confidenceMax, supplier, dateFrom, dateTo, page, pageSize);

    private static IRenderedComponent<AnomaliesPage> RenderAnomaliesPageCore(
        AnomaliesHarness harness, Guid companyId, string? type, string? status, decimal? confidenceMin, decimal? confidenceMax,
        string? supplier, DateTime? dateFrom, DateTime? dateTo, int? page, int? pageSize)
    {
        harness.Navigation.NavigateTo(BuildFilteredPath("/finance/anomalies", companyId, type, status, confidenceMin, confidenceMax, supplier, dateFrom, dateTo, page, pageSize));
        return harness.Context.RenderComponent<AnomaliesPage>();
    }

    private static IRenderedComponent<AnomalyDetailPage> RenderAnomalyDetailPageCore(
        AnomalyDetailHarness harness, Guid companyId, Guid anomalyId, string? type, string? status, decimal? confidenceMin, decimal? confidenceMax,
        string? supplier, DateTime? dateFrom, DateTime? dateTo, int? page, int? pageSize)
    {
        harness.Navigation.NavigateTo(BuildFilteredPath($"/finance/anomalies/{anomalyId:D}", companyId, type, status, confidenceMin, confidenceMax, supplier, dateFrom, dateTo, page, pageSize));
        return harness.Context.RenderComponent<AnomalyDetailPage>(parameters => parameters.Add(x => x.AnomalyId, anomalyId));
    }

    private static string BuildFilteredPath(
        string path, Guid companyId, string? type, string? status, decimal? confidenceMin, decimal? confidenceMax,
        string? supplier, DateTime? dateFrom, DateTime? dateTo, int? page, int? pageSize)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("companyId", companyId.ToString("D")), new("type", type), new("status", status),
            new("confidenceMin", confidenceMin?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("confidenceMax", confidenceMax?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("supplier", supplier), new("dateFrom", dateFrom?.ToString("yyyy-MM-dd")),
            new("dateTo", dateTo?.ToString("yyyy-MM-dd")), new("page", page?.ToString()), new("pageSize", pageSize?.ToString())
        };

        return $"{path}?{string.Join("&", values.Where(value => !string.IsNullOrWhiteSpace(value.Value)).Select(value => $"{value.Key}={Uri.EscapeDataString(value.Value!)}"))}";
    }

    private static AnomaliesHarness CreateAnomaliesHarness(
        Guid companyId,
        FinanceAnomalyWorkbenchResponse workbench,
        string membershipRole = "owner")
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var financeRequests = new List<Uri>();

        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUserContext(companyId, membershipRole)),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/anomalies/workbench")
            {
                financeRequests.Add(request.RequestUri);
                return Task.FromResult(CreateJsonResponse(workbench));
            }

            return Task.FromResult(CreateNotFoundResponse());
        })) { BaseAddress = new Uri("http://localhost/") }));

        return new AnomaliesHarness(context, context.Services.GetRequiredService<FakeNavigationManager>(), financeRequests);
    }

    private static AnomalyDetailHarness CreateAnomalyDetailHarness(
        Guid companyId,
        Guid anomalyId,
        FinanceAnomalyDetailResponse detail,
        string membershipRole = "owner")
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();

        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUserContext(companyId, membershipRole)),
                _ => CreateNotFoundResponse()
            });
        })) { BaseAddress = new Uri("http://localhost/") }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/anomalies/workbench/{anomalyId:D}")
            {
                return Task.FromResult(CreateJsonResponse(detail));
            }

            return Task.FromResult(CreateNotFoundResponse());
        })) { BaseAddress = new Uri("http://localhost/") }));

        return new AnomalyDetailHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
    }

    private static FinanceAnomalyWorkbenchItemResponse CreateWorkbenchItem(
        string affectedRecordReference,
        string? deduplicationKey,
        DateTime? windowStartUtc,
        DateTime? windowEndUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            AnomalyType = "threshold_breach",
            Status = "awaiting_approval",
            Confidence = 0.94m,
            SupplierName = "Contoso Supplies",
            AffectedRecordReference = affectedRecordReference,
            ExplanationSummary = "Transaction exceeded the configured threshold.",
            RecommendedAction = "Review before payment.",
            DetectedAtUtc = new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc),
            Deduplication = deduplicationKey is null && !windowStartUtc.HasValue && !windowEndUtc.HasValue
                ? new FinanceAnomalyDeduplicationResponse()
                : new FinanceAnomalyDeduplicationResponse
                {
                    Key = deduplicationKey,
                    WindowStartUtc = windowStartUtc,
                    WindowEndUtc = windowEndUtc
                }
        };

    private static FinanceAnomalyDetailResponse CreateAnomalyDetail(Guid anomalyId, FinanceAnomalyDeduplicationResponse? deduplication) =>
        new()
        {
            Id = anomalyId,
            AnomalyType = "threshold_breach",
            Status = "awaiting_approval",
            Confidence = 0.94m,
            SupplierName = "Contoso Supplies",
            Explanation = "Investigate the transaction before releasing payment.",
            RecommendedAction = "Review before payment.",
            DetectedAtUtc = new DateTime(2026, 4, 16, 12, 0, 0, DateTimeKind.Utc),
            Deduplication = deduplication,
            RelatedRecordLinks = [],
            FollowUpTasks = []
        };

    private static CurrentUserContextViewModel CreateCurrentUserContext(Guid companyId, string membershipRole) =>
        new()
        {
            Memberships =
            [
                new CompanyMembershipViewModel
                {
                    MembershipId = Guid.NewGuid(),
                    CompanyId = companyId,
                    CompanyName = "Contoso Finance",
                    MembershipRole = membershipRole,
                    Status = "active"
                }
            ],
            ActiveCompany = new ResolvedCompanyContextViewModel
            {
                MembershipId = Guid.NewGuid(),
                CompanyId = companyId,
                CompanyName = "Contoso Finance",
                MembershipRole = membershipRole,
                Status = "active"
            }
        };

    private static HttpResponseMessage CreateJsonResponse<T>(T payload) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not found", detail = "Not found." })
        };

    private sealed class AnomaliesHarness : IDisposable
    {
        public AnomaliesHarness(TestContext context, FakeNavigationManager navigation, IReadOnlyList<Uri> financeRequests)
        {
            Context = context;
            Navigation = navigation;
            FinanceRequests = financeRequests;
        }

        public TestContext Context { get; }
        public FakeNavigationManager Navigation { get; }
        public IReadOnlyList<Uri> FinanceRequests { get; }

        public void Dispose() => Context.Dispose();
    }

    private sealed class AnomalyDetailHarness : IDisposable
    {
        public AnomalyDetailHarness(TestContext context, FakeNavigationManager navigation)
        {
            Context = context;
            Navigation = navigation;
        }

        public TestContext Context { get; }
        public FakeNavigationManager Navigation { get; }

        public void Dispose() => Context.Dispose();
    }

    private sealed class AsyncStubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
