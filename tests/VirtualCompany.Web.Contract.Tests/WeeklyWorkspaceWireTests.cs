using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class WeeklyWorkspaceWireTests
{
    [Theory]
    [InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public async Task Real_client_deserializes_authorized_weekly_sources_and_comparison(string lens)
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        var client = new WeeklyWorkspaceApiClient(new CompanyApiTransport(http), false);
        var w = await client.GetAsync(seed.Company, lens, new DateOnly(2026, 9, 30));
        Assert.Equal(seed.Company, w!.CompanyId); Assert.Equal(lens, w.ActiveLens); Assert.Equal(new DateOnly(2026, 9, 28), w.Period.WeekStart);
        Assert.True(w.Period.IsWeekToDate); Assert.Equal(WeeklyWorkspaceFixture.Now, w.Period.ActivityEndUtc);
        Assert.Empty(w.Diagnostics); Assert.NotEmpty(w.Contributions);
        foreach (var activity in w.Contributions.SelectMany(x => x.Metrics).Where(x => x.Kind == "activity"))
        { Assert.Equal(activity.Value, activity.Sources.Count); Assert.Equal(activity.ComparisonValue, activity.ComparisonSources!.Count); }
        await Assert.ThrowsAsync<TodayWorkspaceAccessException>(() => client.GetAsync(seed.Foreign, lens));
    }
}
