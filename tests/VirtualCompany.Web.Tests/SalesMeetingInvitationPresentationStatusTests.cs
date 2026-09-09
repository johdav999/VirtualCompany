using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Components.Sales;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class SalesMeetingInvitationPresentationStatusTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InvitationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData("session_required", false, false, "Session not configured", "Prepare presentation")]
    [InlineData("processing", true, false, "Deck processing", "Continue preparation")]
    [InlineData("ready", true, true, "Presentation active", "Open presenter controls")]
    public void Authoritative_readiness_drives_status_and_navigation(
        string readiness, bool hasSession, bool canOpen, string expectedStatus, string expectedAction)
    {
        using var context = CreateContext(new StatusHandler(Model(readiness, hasSession, canOpen)));

        var cut = context.RenderComponent<SalesMeetingInvitationPresentationStatus>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.InvitationId, InvitationId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(expectedStatus, cut.Markup);
            var link = cut.Find(".invitation-presentation__action");
            Assert.Equal(expectedAction, link.TextContent.Trim());
            Assert.Contains(canOpen ? $"/teams/meetings/{SessionId:D}/side-panel" : $"/meeting-invitations/{InvitationId:D}/prepare", link.GetAttribute("href"));
            Assert.Contains($"companyId={CompanyId:D}", link.GetAttribute("href"));
        });
    }

    [Fact]
    public void Retryable_failure_uses_safe_authoritative_status()
    {
        var model = Model("blocked", true, false) with
        {
            BlockingReasons = [new("deck_processing_failed", "Deck processing failed and can be retried.")]
        };
        using var context = CreateContext(new StatusHandler(model));
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Deck processing failed", cut.Markup);
            Assert.Contains("Continue preparation", cut.Markup);
        });
    }

    [Fact]
    public void Authorization_failure_discloses_no_preparation_state_and_allows_retry()
    {
        using var context = CreateContext(new StatusHandler(null, HttpStatusCode.Forbidden));
        var cut = Render(context);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Presentation status unavailable", cut.Markup);
            Assert.Contains("Retry status", cut.Markup);
            Assert.Empty(cut.FindAll("a"));
        });
    }

    private static IRenderedComponent<SalesMeetingInvitationPresentationStatus> Render(TestContext context) =>
        context.RenderComponent<SalesMeetingInvitationPresentationStatus>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.InvitationId, InvitationId));

    private static TestContext CreateContext(StatusHandler handler)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var transport = new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
        context.Services.AddSingleton(serviceProvider => new SalesPresentationPreparationApiClient(
            transport, false, serviceProvider.GetRequiredService<IApiProblemMessageResolver>()));
        return context;
    }

    private static SalesPresentationPreparationViewModel Model(string readiness, bool hasSession, bool canOpen) =>
        new(CompanyId, InvitationId, Guid.NewGuid(), "Welheld opportunity", "Northstar AB",
            hasSession ? SessionId : null, 26_214_400,
            new(InvitationId, Guid.NewGuid(), null, null, "Welheld discovery", DateTime.UtcNow.AddDays(1),
                DateTime.UtcNow.AddDays(1).AddMinutes(45), "Europe/Stockholm", null, true,
                "microsoft365", "scheduled", true, true),
            null, [], [], canOpen ? Guid.NewGuid() : null, canOpen ? 3 : 0, readiness, canOpen, [],
            readiness == "session_required" ? ["create_session"] : canOpen ? ["open_presenter"] : ["upload_deck"]);

    private sealed class StatusHandler(SalesPresentationPreparationViewModel? model, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(status == HttpStatusCode.OK
                ? new HttpResponseMessage(status) { Content = JsonContent.Create(model) }
                : new HttpResponseMessage(status)
                {
                    Content = new StringContent("{\"title\":\"Forbidden\",\"status\":403}", System.Text.Encoding.UTF8, "application/problem+json")
                });
    }
}
