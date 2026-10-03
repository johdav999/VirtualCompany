using System.Xml.Linq;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class TreasuryWorkspaceSurfaceTests
{
    [Fact]
    public void Missing_cash_renders_unavailable_amounts_and_recovery_instead_of_zero_and_healthy()
    {
        using var ctx = new TestContext().AddVirtualCompanyWebPresentationServices();
        var company = Guid.NewGuid();
        var workspace = new TreasuryWorkspaceResponse
        {
            CompanyId = company, HasMissingEvidence = true,
            Liquidity = new() { Currency = "USD", RiskLevel = "missing", HorizonDays = 14 },
            Exceptions = [new() { Kind = "liquidity", Currency = "USD", Severity = "high" }],
            Laura = new() { AgentName = "Laura", MissingEvidence = ["No mapped connected bank account evidence is available."] }
        };
        var user = new CurrentUserContextViewModel
        {
            ActiveCompany = new() { CompanyId = company, CompanyName = "Company", MembershipRole = "owner", Status = "active" },
            Memberships = [new() { CompanyId = company, CompanyName = "Company", MembershipRole = "owner", Status = "active" }]
        };
        var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath.Contains("treasury-workspace")
            ? new(HttpStatusCode.OK) { Content = JsonContent.Create(workspace) }
            : new(HttpStatusCode.OK) { Content = JsonContent.Create(user) })) { BaseAddress = new("http://localhost/") };
        ctx.Services.AddSingleton(new FinanceApiClient(new CompanyApiTransport(http)));
        ctx.Services.AddSingleton(new OnboardingApiClient(http));
        ctx.Services.AddSingleton<FinanceAccessResolver>();
        ctx.Services.AddSingleton<TreasuryWorkspaceUsageTelemetry>();
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo($"/finance/cash-position?companyId={company}");
        var cut = ctx.RenderComponent<CashPositionPage>(p => p.Add(x => x.CompanyId, company));
        cut.WaitForAssertion(() => Assert.Equal(4, cut.FindAll(".treasury-kpi strong").Count));
        Assert.All(cut.FindAll(".treasury-kpi strong"), value => Assert.Equal("Not available", value.TextContent));
        Assert.DoesNotContain("Healthy", cut.Find(".treasury-kpis").TextContent);
        Assert.DoesNotContain("current for this review", cut.Markup);
        Assert.DoesNotContain("USD 0.00", cut.Markup);
        Assert.DoesNotContain("Projected cash is", cut.Markup);
        Assert.Contains("Recover", cut.Find(".treasury-laura").TextContent, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(action(request));
    }

    [Fact]
    public void Cash_route_is_a_consolidated_evidence_grounded_daily_treasury_workspace()
    {
        var page = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor");
        var code = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor.cs");
        var css = Read("src", "VirtualCompany.Web", "Pages", "Finance", "CashPositionPage.razor.css");

        Assert.Contains("@page \"/finance/cash-position\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Accounts", page, StringComparison.Ordinal);
        Assert.Contains("EvidenceSource", page, StringComparison.Ordinal);
        Assert.Contains("EvidenceUtc", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Exceptions", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PaymentWork", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Laura.Citations", page, StringComparison.Ordinal);
        Assert.Contains("MissingEvidenceMessages()", page, StringComparison.Ordinal);
        Assert.Contains("ExceptionTitle(item)", page, StringComparison.Ordinal);
        Assert.Contains("ProjectionEvidenceBasis(point)", page, StringComparison.Ordinal);
        Assert.Contains("TreasuryCashProjectionCitation", code, StringComparison.Ordinal);
        Assert.Contains("GetTreasuryWorkspaceAsync", code, StringComparison.Ordinal);
        Assert.Contains("TreasuryWorkspaceUsageTelemetry", code, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("prefers-reduced-motion", css, StringComparison.Ordinal);
    }

    [Fact]
    public void English_and_swedish_have_matching_daily_treasury_resources()
    {
        var english = Keys("FinanceResources.resx");
        var swedish = Keys("FinanceResources.sv-SE.resx");
        var treasuryKeys = english.Where(key => key.StartsWith("TreasuryDaily", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryAccountCoverage", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryEvidenceNeeds", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryPaymentWork", StringComparison.Ordinal) ||
                                                key.StartsWith("TreasuryRecommendOnly", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(treasuryKeys);
        Assert.All(treasuryKeys, key => Assert.Contains(key, swedish));
        Assert.Contains("Daglig likviditet", Read("src", "VirtualCompany.Web", "Localization", "Finance",
            "FinanceResources.sv-SE.resx"), StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshot_first_reference_and_operations_runbook_are_committed()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "docs", "design", "references",
            "daily-treasury-workspace-reference.png")));
        var prompt = Read("docs", "design", "references", "daily-treasury-workspace-reference-prompt.md");
        var runbook = Read("docs", "runbooks", "daily-treasury-workspace.md");
        Assert.Contains("account coverage", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recommendation-only", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reconciliation_required", runbook, StringComparison.Ordinal);
        Assert.Contains("maximum 50", runbook, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> Keys(string file) => XDocument.Load(Path.Combine(
            Root(), "src", "VirtualCompany.Web", "Localization", "Finance", file))
        .Root!
        .Elements("data")
        .Select(element => (string?)element.Attribute("name"))
        .Where(name => name is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([Root(), .. segments]));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
