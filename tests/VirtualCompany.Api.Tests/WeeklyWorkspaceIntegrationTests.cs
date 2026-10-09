using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class WeeklyWorkspaceIntegrationTests
{
    [Theory]
    [InlineData("company")][InlineData("sales")][InlineData("marketing")][InlineData("finance")][InlineData("customers")]
    public async Task Every_role_has_real_weekly_sources_and_context(string lens)
    {
        using var factory = new LoggedWeeklyFactory();
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens={lens}&week=2026-09-30");
        Assert.Equal(lens, w!.ActiveLens); Assert.Equal(new DateOnly(2026, 9, 28), w.Period.WeekStart);
        Assert.Equal(WeeklyWorkspaceFixture.Now, w.Period.ActivityEndUtc);
        Assert.True(w.Diagnostics.Count == 0, string.Join("\n", factory.Log.Errors));
        Assert.Contains(w.Contributions, x => x.Lens == lens && x.Metrics.Count > 0);
        Assert.All(w.Contributions, c => Assert.Contains(w.AvailableLenses, x => x.Value == c.Lens));
        foreach (var m in w.Contributions.SelectMany(x => x.Metrics).Where(x => x.Kind == "activity"))
        {
            Assert.Equal(m.Value, m.Sources.Count); Assert.Equal(m.ComparisonValue, m.ComparisonSources!.Count);
            Assert.All(m.Sources, x => Assert.InRange(x.OccurredUtc, w.Period.StartUtc, w.Period.ActivityEndUtc.AddTicks(-1)));
            Assert.All(m.ComparisonSources!, x => Assert.InRange(x.OccurredUtc, w.Period.ComparisonStartUtc, w.Period.ComparisonEndUtc.AddTicks(-1)));
        }
        Assert.DoesNotContain("Foreign secret", System.Text.Json.JsonSerializer.Serialize(w));
    }
    [Fact]
    public async Task Activity_boundaries_SLA_denominator_and_balance_cutoffs_reconcile()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=company");
        var metrics = w!.Contributions.SelectMany(x => x.Metrics).ToDictionary(x => x.Key);
        Assert.Equal(2, metrics["sales.stage_changes"].Value); Assert.Equal(1, metrics["sales.stage_changes"].ComparisonValue);
        Assert.Equal(2, metrics["support.resolved"].Value); Assert.Equal(1, metrics["support.reopened"].Value);
        Assert.Equal(50, metrics["support.sla"].Value); Assert.Equal(2, metrics["support.sla"].Sources.Count);
        Assert.Equal(500, metrics["finance.cash.SEK"].Value); Assert.Equal(200, metrics["finance.cash.SEK"].ComparisonValue);
        Assert.Equal(130, metrics["finance.due.invoice.SEK"].Value);
        Assert.Equal(seed.Invoice.ToString("D"), metrics["finance.due.invoice.SEK"].Sources.Single().Id);
        Assert.Equal(1, metrics["marketing.launches"].Value); Assert.Equal(1, metrics["marketing.delivery"].Value);
        Assert.Equal(1, metrics["marketing.content_due"].Value); Assert.Equal(1, metrics["sales.follow_ups"].Value);
        Assert.Contains("draft prepared", metrics["sales.follow_ups"].Sources.Single().Title);
        Assert.Contains(metrics["company.completed"].Sources, x => x.Id == $"task:{seed.TaskId:D}");
        Assert.Null(metrics["sales.pipeline_movement"].Value); Assert.Null(metrics["support.backlog_movement"].Value);
        if (Environment.GetEnvironmentVariable("VIRTUALCOMPANY_P19_EVIDENCE_DIRECTORY") is { Length: > 0 } evidenceDirectory)
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "source-reconciliation.json"),
                System.Text.Json.JsonSerializer.Serialize(new { profile = "GUID-isolated native records; fixed test clock; authenticated composed GET", seed,
                    request = $"/api/companies/{seed.Company}/workspace/weekly?lens=company", observed = w },
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
        }
    }
    [Fact]
    public async Task Missing_prior_cash_is_unavailable_and_current_balance_is_retained()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory, false); using var http = WeeklyWorkspaceFixture.Client(factory);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=finance");
        var cash = w!.Contributions.Single().Metrics.Single(x => x.Key == "finance.cash.SEK");
        Assert.Equal(500, cash.Value); Assert.Null(cash.ComparisonValue); Assert.False(cash.IsComplete);
        Assert.True(w.IsPartial); Assert.Empty(cash.ComparisonSources!);
    }
    [Fact]
    public async Task Restricted_responsibility_foreign_company_and_invalid_period_do_not_leak()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory, seed.ManagerSubject);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=finance");
        Assert.Equal("sales", w!.ActiveLens); Assert.Single(w.AvailableLenses); Assert.Single(w.Contributions);
        Assert.DoesNotContain("finance", System.Text.Json.JsonSerializer.Serialize(w.Contributions));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/companies/{seed.Foreign}/workspace/weekly?lens=company")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/weekly?week=2026-10-12")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/weekly?week=wrong")).StatusCode);
        await factory.SeedAsync(async db => { var membership = await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.Company && x.UserId == seed.Manager);
            membership.UpdateStatus(VirtualCompany.Domain.Enums.CompanyMembershipStatus.Revoked); });
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/weekly?lens=sales")).StatusCode);
    }
    [Fact]
    public async Task Refresh_reads_current_records_and_preserves_Today_and_Monthly()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        var path = $"/api/companies/{seed.Company}/workspace/weekly?lens=sales&week=2026-09-28";
        var before = await http.GetFromJsonAsync<WeeklyWorkspaceDto>(path);
        await factory.SeedAsync(db => { db.SalesActivities.Add(new(Guid.NewGuid(), seed.Company, "stage change", "Fresh stage receipt", WeeklyWorkspaceFixture.Now.AddMinutes(-1), dealId: seed.Deal)); return Task.CompletedTask; });
        var after = await http.GetFromJsonAsync<WeeklyWorkspaceDto>(path);
        Assert.Equal(before!.Contributions[0].Metrics[0].Value + 1, after!.Contributions[0].Metrics[0].Value);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/today?lens=sales")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/monthly?lens=sales&year=2026&month=9")).StatusCode);
    }
    [Fact]
    public async Task Company_week_configuration_is_honored_independently_of_fiscal_calendar()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        await factory.SeedAsync(async db => { var c = await db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.Company); c.Settings.Extensions["weekStartsOn"] = System.Text.Json.Nodes.JsonValue.Create("Sunday"); });
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=sales");
        Assert.Equal(DayOfWeek.Sunday, w!.Period.WeekStartsOn); Assert.Equal(new DateOnly(2026, 9, 27), w.Period.WeekStart);
        Assert.Contains("independent of fiscal-year", w.CalendarMeaning);
    }
    [Fact]
    public async Task Invalid_company_timezone_is_disclosed_and_invalid_week_configuration_is_rejected()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        await factory.SeedAsync(async db => { var c = await db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.Company);
            c.UpdateWorkspaceProfile(c.Name, null, null, "Invalid/P19-zone", "SEK", "en", "SE"); });
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=sales");
        Assert.Equal("UTC", w!.Period.Timezone); Assert.True(w.IsPartial);
        Assert.Contains(w.Diagnostics, x => x.Code == "weekly_timezone_fallback");
        await factory.SeedAsync(async db => { var c = await db.Companies.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.Company);
            c.Settings.Extensions["weekStartsOn"] = System.Text.Json.Nodes.JsonValue.Create("Funday"); });
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync($"/api/companies/{seed.Company}/workspace/weekly?lens=sales")).StatusCode);
    }
    [Fact]
    public async Task Completed_week_uses_the_full_interval_and_business_sources_are_read_only()
    {
        var guard = new ReadOnlyGuard();
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock(), null, dbInterceptors: [guard]);
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        guard.Armed = true;
        var response = await http.GetAsync($"/api/companies/{seed.Company}/workspace/weekly?lens=company&week=2026-09-23");
        Assert.True(response.IsSuccessStatusCode, string.Join(", ", guard.Entities));
        var w = await response.Content.ReadFromJsonAsync<WeeklyWorkspaceDto>();
        Assert.False(w!.Period.IsWeekToDate); Assert.Equal(w.Period.EndUtc, w.Period.ActivityEndUtc);
        Assert.Equal(3, w.Contributions.Single(x => x.Lens == "sales").Metrics.Single(x => x.Key == "sales.stage_changes").Value);
        Assert.Empty(w.Diagnostics); Assert.Equal(0, guard.WriteAttempts);
    }
    [Fact]
    public async Task Bounded_activity_window_discloses_partial_counts_and_reconciles_included_rows()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        await factory.SeedAsync(db => { db.SalesActivities.AddRange(Enumerable.Range(0, WeeklyWorkspaceMeasures.SourceLimit + 1).Select(i =>
            new VirtualCompany.Domain.Entities.SalesActivity(Guid.NewGuid(), seed.Company, "stage change", "Bounded source " + i,
                WeeklyWorkspaceFixture.Now.AddSeconds(-i - 1), dealId: seed.Deal))); return Task.CompletedTask; });
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=sales");
        var metric = w!.Contributions.Single().Metrics.Single(x => x.Key == "sales.stage_changes");
        Assert.True(w.IsPartial); Assert.False(metric.IsComplete); Assert.Equal(metric.Sources.Count, metric.Value);
        Assert.True(metric.Sources.Count + metric.ComparisonSources!.Count <= WeeklyWorkspaceMeasures.SourceLimit);
        Assert.Contains(w.Contributions.Single().CoverageGaps, x => x.Contains("exceeds 2,000"));
    }
    [Fact]
    public async Task Cash_currencies_stay_separate_and_no_SLA_cohort_is_unavailable()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory); using var http = WeeklyWorkspaceFixture.Client(factory);
        await factory.SeedAsync(db => { var id = Guid.NewGuid();
            db.Add(new VirtualCompany.Domain.Entities.FinanceAccount(id, seed.Company, "1931", "Bank USD", "asset", "USD", 5, WeeklyWorkspaceFixture.Start.AddDays(-30)));
            db.Add(new VirtualCompany.Domain.Entities.FinanceBalance(Guid.NewGuid(), seed.Company, id, WeeklyWorkspaceFixture.Now.AddHours(-1), 50, "USD"));
            db.Add(new VirtualCompany.Domain.Entities.FinanceBalance(Guid.NewGuid(), seed.Company, id, WeeklyWorkspaceFixture.Start.AddDays(-8), 20, "USD"));
            return Task.CompletedTask; });
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=finance");
        var cash = w!.Contributions.Single().Metrics.Where(x => x.Key.StartsWith("finance.cash.")).ToDictionary(x => x.Key);
        Assert.Equal(500, cash["finance.cash.SEK"].Value); Assert.Equal(50, cash["finance.cash.USD"].Value);
        Assert.Equal(20, cash["finance.cash.USD"].ComparisonValue); Assert.DoesNotContain("finance.cash.MIXED", cash.Keys);
        var support = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=customers&week=2026-08-10");
        var sla = support!.Contributions.Single().Metrics.Single(x => x.Key == "support.sla");
        Assert.Null(sla.Value); Assert.Empty(sla.Sources); Assert.False(sla.IsComplete);
    }
    [Fact]
    public async Task Projection_failure_is_observable_partial_and_never_a_measured_zero()
    {
        using var factory = new ThrowingWeeklyFactory(); var seed = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = WeeklyWorkspaceFixture.Client(factory);
        var w = await http.GetFromJsonAsync<WeeklyWorkspaceDto>($"/api/companies/{seed.Company}/workspace/weekly?lens=sales");
        Assert.True(w!.IsPartial); Assert.Contains(w.Diagnostics, x => x.Code == "weekly_projection_failed");
        Assert.Empty(w.Contributions.Single().Metrics); Assert.NotEmpty(w.Contributions.Single().CoverageGaps);
    }
    private sealed class ThrowingWeeklyFactory() : TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock())
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder); builder.ConfigureServices(s => { s.RemoveAll<IWeeklyWorkspaceContributor>(); s.AddScoped<IWeeklyWorkspaceContributor, ThrowingContributor>(); });
        }
    }
    private sealed class LoggedWeeklyFactory() : TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock())
    {
        public ProjectionLog Log { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILogger<CompanyWeeklyWorkspaceQueryService>>(Log));
        }
    }
    private sealed class ProjectionLog : ILogger<CompanyWeeklyWorkspaceQueryService>
    {
        public List<string> Errors { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level >= LogLevel.Error) Errors.Add(formatter(state, exception) + "\n" + exception); }
    }
    private sealed class ThrowingContributor : IWeeklyWorkspaceContributor
    {
        public string Lens => "sales";
        public Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token) => throw new InvalidOperationException("Test projection failure");
    }
    private sealed class ReadOnlyGuard : SaveChangesInterceptor
    {
        public bool Armed; public int WriteAttempts; public List<string> Entities { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Armed)
            {
                // Existing request authentication refreshes the User profile. The weekly read
                // must not persist company/business records, decisions, execution or history.
                var changes = data.Context!.ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                    .Where(x => x.Entity is not VirtualCompany.Domain.Entities.User).ToList();
                if (changes.Count > 0) { WriteAttempts++; Entities.AddRange(changes.Select(x => x.Entity.GetType().Name)); throw new InvalidOperationException("A weekly GET attempted to write business evidence."); }
            }
            return ValueTask.FromResult(result);
        }
    }
}
