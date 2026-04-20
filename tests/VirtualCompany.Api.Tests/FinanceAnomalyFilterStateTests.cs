using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAnomalyFilterStateTests
{
    [Fact]
    public void Finance_anomaly_filter_state_normalizes_values_for_query_string_navigation()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var state = new FinanceAnomalyFilterState
        {
            AnomalyType = " Threshold Breach ",
            Status = "Awaiting Approval",
            ConfidenceMin = 0.92m,
            ConfidenceMax = 0.27m,
            Supplier = " Contoso Supplies ",
            DateFrom = new DateTime(2026, 4, 1),
            DateTo = new DateTime(2026, 4, 10),
            Page = 0,
            PageSize = 999
        }.Normalize();

        Assert.Equal("threshold_breach", state.AnomalyType);
        Assert.Equal("awaiting_approval", state.Status);
        Assert.Equal(0.27m, state.ConfidenceMin);
        Assert.Equal(0.92m, state.ConfidenceMax);
        Assert.Equal("Contoso Supplies", state.Supplier);
        Assert.Equal(1, state.Page);
        Assert.Equal(50, state.PageSize);

        var query = state.ToQueryString(companyId);
        Assert.Contains($"companyId={companyId:D}", query);
        Assert.Contains("type=threshold_breach", query);
        Assert.Contains("status=awaiting_approval", query);
        Assert.Contains("confidenceMin=0.27", query);
        Assert.Contains("confidenceMax=0.92", query);
        Assert.Contains("supplier=Contoso%20Supplies", query);
        Assert.Contains("dateFrom=2026-04-01", query);
        Assert.Contains("dateTo=2026-04-10", query);
    }

    [Fact]
    public void Finance_anomaly_deduplication_metadata_is_only_rendered_when_values_exist()
    {
        Assert.False(FinanceAnomalyPresentation.HasDeduplicationMetadata(null));
        Assert.False(FinanceAnomalyPresentation.HasDeduplicationMetadata(new FinanceAnomalyDeduplicationResponse()));
        Assert.True(FinanceAnomalyPresentation.HasDeduplicationMetadata(new FinanceAnomalyDeduplicationResponse
        {
            Key = "finance-transaction-anomaly:dedupe"
        }));
    }

    [Fact]
    public void Finance_workbench_return_url_only_preserves_local_navigation_targets()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var taskId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var returnUrl = $"/finance/anomalies?companyId={companyId:D}&status=awaiting_approval&page=2";

        Assert.Equal(returnUrl, ReturnUrlNavigation.NormalizeLocalReturnUrl(returnUrl));
        Assert.Null(ReturnUrlNavigation.NormalizeLocalReturnUrl("https://example.com/finance/anomalies"));
        Assert.Null(ReturnUrlNavigation.NormalizeLocalReturnUrl("//example.com/finance/anomalies"));

        var href = ReturnUrlNavigation.AppendReturnUrl($"/tasks?companyId={companyId:D}&taskId={taskId:D}", returnUrl);
        Assert.Contains($"returnUrl={Uri.EscapeDataString(returnUrl)}", href);

        Assert.Equal($"/tasks?companyId={companyId:D}&taskId={taskId:D}", ReturnUrlNavigation.AppendReturnUrl($"/tasks?companyId={companyId:D}&taskId={taskId:D}", "https://example.com/finance/anomalies"));
    }
}