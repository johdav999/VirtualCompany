using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesOperationalReportTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly ReportFactory factory = new();
    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Forecast_filters_boundaries_currency_evidence_and_read_only_state_reconcile()
    {
        var seed = await Seed(); using var client = Client(seed.Company);
        var before = await factory.ExecuteDbContextAsync(db => db.RevenueForecastSnapshots.IgnoreQueryFilters().CountAsync());
        var report = await client.GetFromJsonAsync<SalesOpportunityReport>("api/sales/operational/opportunities?forecast=true&days=30&currency=SEK");
        Assert.NotNull(report); Assert.Equal(Now, report.AsOfUtc); Assert.Equal(2, report.Rows.Count);
        Assert.Equal(8100m, report.Totals.Single().ExpectedAmount);
        Assert.Equal(report.Rows.Sum(x => x.ExpectedAmount), report.Totals.Single().ExpectedAmount);
        Assert.Contains(report.Rows, x => x.DealId == seed.Boundary && x.RiskCalculatedUtc == null && x.Risk == .50m);
        Assert.DoesNotContain(report.Rows, x => x.DealId == seed.Foreign || x.Title == "Outside horizon" || x.Title == "No close time" || x.Title == "Closed");
        var all = await client.GetFromJsonAsync<SalesOpportunityReport>("api/sales/operational/opportunities?forecast=true&days=90");
        Assert.Equal(2, all!.Totals.Count); Assert.Equal(new[] { "SEK", "USD" }, all.AvailableCurrencies);
        Assert.All(all.Totals, total => Assert.Equal(all.Rows.Where(x => x.Currency == total.Currency).Sum(x => x.ExpectedAmount), total.ExpectedAmount));
        Assert.Equal(before, await factory.ExecuteDbContextAsync(db => db.RevenueForecastSnapshots.IgnoreQueryFilters().CountAsync()));
        var legacy = await client.GetFromJsonAsync<RevenueForecastSnapshotDto>("api/sales/forecast");
        Assert.Equal(Guid.Empty, legacy!.Id);
        Assert.Equal("SEK", legacy.Currency);
        Assert.Equal(before, await factory.ExecuteDbContextAsync(db => db.RevenueForecastSnapshots.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Pipeline_stage_filters_reconcile_without_currency_conversion()
    {
        var seed = await Seed(); using var client = Client(seed.Company);
        var report = await client.GetFromJsonAsync<SalesOpportunityReport>($"api/sales/operational/opportunities?stageId={SalesPipelineStage.QualifiedStageId}");
        Assert.All(report!.Rows, x => Assert.Equal(SalesPipelineStage.QualifiedStageId, x.StageId));
        Assert.All(report.Totals, x => Assert.Equal(report.Rows.Where(r => r.Currency == x.Currency).Sum(r => r.Amount), x.GrossAmount));
        Assert.All(report.Rows, x => Assert.Equal(Math.Round(x.Amount * .45m, 2), x.ExpectedAmount));
        var summary = await client.GetFromJsonAsync<SalesDashboardResponse>("api/sales/dashboard");
        Assert.Equal("SEK", summary!.Currency);
        Assert.Equal(report.Rows.Where(x => x.Currency == "SEK").Sum(x => x.Amount), summary.PipelineValue);
    }

    [Fact]
    public async Task Commitment_retry_review_reload_audit_and_today_are_durable()
    {
        var seed = await Seed(); using var client = Client(seed.Company);
        var command = new RecordSalesCommitment(Guid.NewGuid(), "Review proposal with owner", Now.AddMinutes(-1));
        var route = $"api/sales/operational/deals/{seed.Deal}/commitments";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, command with { Summary = "Changed" })).StatusCode);
        var report = await client.GetFromJsonAsync<SalesActivityReport>("api/sales/operational/activities?status=overdue");
        var activity = Assert.Single(report!.Commitments); Assert.Equal(command.CommandId, activity.Id);
        var today = await client.GetFromJsonAsync<TodayWorkspaceDto>($"api/companies/{seed.Company}/workspace/today?lens=sales&refresh=true");
        Assert.Contains(today!.Priorities, x => x.EvidenceSourceType == "sales_commitment" && x.EvidenceSourceId == activity.Id.ToString("D"));
        Assert.Contains(today.Sales!.Agenda!, x => x.Title == command.Summary);
        var reviewRoute = $"api/sales/operational/activities/{activity.Id}/review";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(reviewRoute, new ReviewSalesCommitment(Now.AddDays(-1)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(reviewRoute, new ReviewSalesCommitment(activity.UpdatedUtc))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(reviewRoute, new ReviewSalesCommitment(activity.UpdatedUtc))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<SalesActivityReport>("api/sales/operational/activities?status=overdue"))!.Commitments);
        Assert.Single((await client.GetFromJsonAsync<SalesActivityReport>("api/sales/operational/activities?status=completed"))!.Commitments);
        var state = await factory.ExecuteDbContextAsync(async db => new {
            Count = await db.SalesActivities.IgnoreQueryFilters().CountAsync(x => x.Id == command.CommandId),
            Audits = await db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.Company && x.TargetId == command.CommandId.ToString("D")),
            Open = await db.Deals.IgnoreQueryFilters().Where(x => x.Id == seed.Deal).Select(x => x.Status).SingleAsync()
        });
        Assert.Equal(1, state.Count); Assert.Equal(2, state.Audits); Assert.Equal(SalesStatuses.Open, state.Open);
    }

    [Fact]
    public async Task Report_and_commands_require_sales_responsibility_and_company_membership()
    {
        var seed = await Seed(); using var member = Client(seed.Company, "p04-member"); using var owner = Client(seed.Company);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("api/sales/operational/opportunities")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("api/sales/operational/activities")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"api/sales/operational/deals/{seed.Deal}/commitments", new RecordSalesCommitment(Guid.NewGuid(), "No", Now))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"api/sales/operational/deals/{seed.Foreign}/commitments", new RecordSalesCommitment(Guid.NewGuid(), "No", Now))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"api/sales/operational/activities/{Guid.NewGuid()}/review", new ReviewSalesCommitment(Now))).StatusCode);
        using var foreign = Client(seed.Other);
        Assert.Equal(HttpStatusCode.Forbidden, (await foreign.GetAsync("api/sales/operational/opportunities")).StatusCode);
    }

    [Theory]
    [InlineData("forecast=true&days=7")]
    [InlineData("currency=SEKK")]
    public async Task Invalid_filters_have_explicit_validation(string query)
    {
        var seed = await Seed(); using var client = Client(seed.Company);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/sales/operational/opportunities?" + query)).StatusCode);
    }

    [Fact]
    public async Task Due_boundary_is_exclusive_for_overdue_and_future_activities_are_not_agent_completions()
    {
        var seed = await Seed(); using var client = Client(seed.Company);
        foreach (var due in new[] { Now.AddTicks(-1), Now, Now.AddHours(1) })
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"api/sales/operational/deals/{seed.Deal}/commitments", new RecordSalesCommitment(Guid.NewGuid(), "Internal review", due))).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<SalesActivityReport>("api/sales/operational/activities?status=overdue"))!.Commitments);
        Assert.Equal(3, (await client.GetFromJsonAsync<SalesActivityReport>("api/sales/operational/activities?status=pending"))!.Commitments.Count);
        var today = await client.GetFromJsonAsync<TodayWorkspaceDto>($"api/companies/{seed.Company}/workspace/today?lens=sales&refresh=true");
        Assert.DoesNotContain(today!.AgentUpdates, x => x.Summary == "Internal review" && x.AgentState == "completed");
    }

    private HttpClient Client(Guid company, string identity = "p04-owner")
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", identity); client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", identity + "@example.com");
        return client;
    }
    private async Task<SeedIds> Seed()
    {
        var seed = new SeedIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await factory.SeedAsync(db => {
            var owner = new User(Guid.NewGuid(), "p04-owner@example.com", "P04 Owner", "dev-header", "p04-owner");
            var member = new User(Guid.NewGuid(), "p04-member@example.com", "P04 Member", "dev-header", "p04-member");
            db.Users.AddRange(owner, member); db.Companies.AddRange(new Company(seed.Company, "P04"), new Company(seed.Other, "Private"));
            var membership = new CompanyMembership(Guid.NewGuid(), seed.Company, owner.Id, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
            db.CompanyMemberships.AddRange(membership, new CompanyMembership(Guid.NewGuid(), seed.Company, member.Id, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), seed.Company, ResponsibilityArea.Sales, ResponsibilityAssignmentKind.Primary, membership.Id, null, AgentAutonomyLevel.Level1, null, null));
            db.Deals.AddRange(new Deal(seed.Deal, seed.Company, "Renewal", SalesPipelineStage.QualifiedStageId, 12000, "SEK", expectedCloseUtc: Now),
                new Deal(seed.Boundary, seed.Company, "Boundary", SalesPipelineStage.QualifiedStageId, 12000, "SEK", expectedCloseUtc: Now.AddDays(30)),
                new Deal(Guid.NewGuid(), seed.Company, "USD deal", SalesPipelineStage.QualifiedStageId, 1000, "USD", expectedCloseUtc: Now.AddDays(2)),
                new Deal(Guid.NewGuid(), seed.Company, "Outside horizon", SalesPipelineStage.QualifiedStageId, 100, "SEK", expectedCloseUtc: Now.AddDays(30).AddTicks(1)),
                new Deal(Guid.NewGuid(), seed.Company, "No close time", SalesPipelineStage.QualifiedStageId, 200, "SEK"),
                new Deal(Guid.NewGuid(), seed.Company, "Closed", SalesPipelineStage.WonStageId, 800, "SEK", SalesStatuses.Won, expectedCloseUtc: Now.AddDays(1)),
                new Deal(seed.Foreign, seed.Other, "Private deal", SalesPipelineStage.QualifiedStageId, 9999, "SEK", expectedCloseUtc: Now.AddDays(1)));
            return Task.CompletedTask;
        });
        return seed;
    }
    private sealed record SeedIds(Guid Company, Guid Other, Guid Deal, Guid Boundary, Guid Foreign);
    private sealed class ReportFactory : TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedClock()); });
        }
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
