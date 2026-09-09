using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingDemoControlPanelTests
{
    [Fact]
    public void Panel_runs_typed_step_and_requires_exact_reset_confirmation()
    {
        using var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var companyId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var handler = new DemoPanelHandler(companyId, sessionId);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var transport = new CompanyApiTransport(http);
        context.Services.AddSingleton(new DemoScenarioApiClient(http, transport, false));
        context.Services.AddSingleton(new SalesMeetingSessionApiClient(transport, false));

        var cut = context.RenderComponent<SalesMeetingDemoControlPanel>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.MeetingSessionId, sessionId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("DEMO · SYNTHETIC DATA", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Northstar Demo Company", cut.Markup, StringComparison.Ordinal);
            Assert.Equal(3, cut.FindAll("[data-testid='demo-step']").Count);
        });

        FindButton(cut, "Start demo").Click();
        cut.WaitForAssertion(() => Assert.Equal("Run next step", FindButton(cut, "Run next step").TextContent.Trim()));
        FindButton(cut, "Run next step").Click();
        cut.WaitForAssertion(() => Assert.Contains("real Sales workspace", cut.Markup, StringComparison.Ordinal));

        FindButton(cut, "Preview reset").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='demo-reset-preview']");
            Assert.True(FindButton(cut, "Reset demo").HasAttribute("disabled"));
        });
        cut.Find("[data-testid='demo-reset-preview'] input[type='checkbox']").Change(true);
        Assert.False(FindButton(cut, "Reset demo").HasAttribute("disabled"));
        FindButton(cut, "Reset demo").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid='demo-reset-preview']"));
            Assert.Contains("Ready · Step 1 of 3", cut.Markup, StringComparison.Ordinal);
        });
        Assert.Contains(handler.Paths, x => x.EndsWith("/start", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, x => x.EndsWith("/commands", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, x => x.EndsWith("/reset", StringComparison.Ordinal));
    }

    private static AngleSharp.Dom.IElement FindButton(IRenderedFragment cut, string text) =>
        cut.FindAll("button").Single(x => string.Equals(x.TextContent.Trim(), text, StringComparison.Ordinal));

    private sealed class DemoPanelHandler(Guid companyId, Guid sessionId) : HttpMessageHandler
    {
        private int currentStep;
        private string runStatus = "ready";
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            object? body;
            if (path == $"/api/sales/meeting-sessions/{sessionId:D}")
            {
                body = Meeting();
            }
            else if (path.EndsWith("/reset-preview", StringComparison.Ordinal))
            {
                body = Preview();
            }
            else if (path.EndsWith("/start", StringComparison.Ordinal))
            {
                runStatus = "running";
                body = Status();
            }
            else if (path.EndsWith("/commands", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal(Actions()[currentStep].CommandName, json.RootElement.GetProperty("commandName").GetString());
                currentStep++;
                runStatus = currentStep == Actions().Count ? "completed" : "running";
                body = new DemoScenarioCommandResultViewModel("executed", null, "done",
                    Actions()[currentStep - 1].CommandName, currentStep, Actions()[currentStep - 1].ExpectedVisibleOutcome,
                    "lead", Guid.NewGuid(), Status());
            }
            else if (path.EndsWith("/reset", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Assert.Equal("Northstar Demo Company", json.RootElement.GetProperty("expectedCompanyName").GetString());
                currentStep = 0;
                runStatus = "ready";
                body = Status();
            }
            else
            {
                body = Status();
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }

        private SalesMeetingSessionViewModel Meeting() => new(
            sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(),
            "Demo", "Synthetic buyer", 30, "northstar-sales", "provider-demo", "ready", 0, 0, null,
            "pending", null, null, "standard", 365, DateTime.UtcNow, DateTime.UtcNow.AddDays(365), null, null,
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow, 1);

        private DemoScenarioStatusViewModel Status()
        {
            var actions = Actions();
            return new(companyId, "Northstar Demo Company", Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "northstar-sales", 1, sessionId, runStatus, currentStep, actions.Count, 1, true,
                currentStep < actions.Count ? actions[currentStep] : null, actions,
                [new("verified_demo_tenant", true, "The exact company and scenario version are verified."),
                 new("external_integrations_blocked", true, "All external integrations are blocked.")],
                DateTime.UtcNow);
        }

        private DemoScenarioResetPreviewViewModel Preview() => new(
            companyId, "Northstar Demo Company", Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "northstar-sales", 1, 1,
            [new("customer_company", 1, 1), new("contact", 1, 1), new("lead", 1, 1), new("deal", 1, 0)],
            ["Email delivery", "Payments"], [new("verified_demo_tenant", true, "Verified")],
            ["Exactly one synthetic lead exists.", "External integration delivery remains blocked."], true, true, "preview-token");

        private static IReadOnlyList<DemoScenarioActionViewModel> Actions() =>
        [
            new(1, "demo.sales.qualify_lead", "Qualify lead", "The lead appears as qualified in the real Sales workspace."),
            new(2, "demo.sales.convert_lead", "Convert to deal", "A deal appears in the real pipeline."),
            new(3, "demo.sales.move_deal_to_proposal", "Move deal to proposal", "The deal moves to Proposal.")
        ];
    }
}
