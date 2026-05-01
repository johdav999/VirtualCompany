using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Shared;
using VirtualCompany.Web.Pages.Finance;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxSettingsPageTests
{
    [Theory]
    [InlineData(null, "Not connected", "Connect Fortnox")]
    [InlineData("pending", "Connecting", "View sync history")]
    [InlineData("connected", "Connected", "Sync now")]
    [InlineData("needs_reconnect", "Needs reconnect", "Reconnect")]
    [InlineData("error", "Error", "Reconnect")]
    public void Fortnox_card_renders_connection_states_and_expected_actions(string? status, string label, string action)
    {
        var companyId = Guid.Parse("8c212fb5-71cd-42be-a237-5e893285f315");
        using var harness = CreateHarness(companyId, new FortnoxConnectionStatusResponse
        {
            IsConnected = status == "connected",
            ConnectionId = status is null ? null : Guid.Parse("7d03c4c5-d287-4f67-995d-af6a70cf6a1a"),
            ConnectionStatus = status,
            LastErrorSummary = status == "error" ? "Fortnox reported an authorization problem." : null
        });

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Fortnox", cut.Markup);
            Assert.Contains(label, cut.Markup);
            Assert.Contains(action, cut.Markup);
            Assert.Contains("View sync history", cut.Markup);
            Assert.Contains("Approval-gated writes", cut.Markup);
            Assert.Contains("payload hash", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Connected_card_exposes_sync_disconnect_and_history_actions()
    {
        var companyId = Guid.Parse("89d46f96-2f45-4d38-8c8b-b20e8d50622f");
        using var harness = CreateHarness(companyId, ConnectedStatus());

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected", cut.Markup);
            Assert.Contains("Sync now", cut.Markup);
            Assert.Contains("Disconnect", cut.Markup);
            Assert.Contains("View sync history", cut.Markup);
            Assert.DoesNotContain("Connect Fortnox", cut.Markup);
        });
    }

    [Fact]
    public void Sync_now_posts_company_scoped_request_and_refreshes_status()
    {
        var companyId = Guid.Parse("e74dcbb7-a99a-4983-9102-7cf339bb8df5");
        using var harness = CreateHarness(companyId, ConnectedStatus());
        var cut = RenderFortnoxSettings(harness, companyId);

        cut.Find("button.btn-outline-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Fortnox sync finished: 2 created, 1 updated, 3 skipped.", cut.Markup);
            Assert.Contains(harness.Requests, request =>
                request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("/finance/integrations/fortnox/sync", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void History_action_loads_recent_sync_history()
    {
        var companyId = Guid.Parse("902b85fd-64dd-412a-a64b-c2f172df05d9");
        using var harness = CreateHarness(companyId, ConnectedStatus());
        var cut = RenderFortnoxSettings(harness, companyId);

        cut.FindAll("button").Single(button => button.TextContent.Contains("View sync history", StringComparison.OrdinalIgnoreCase)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sync history", cut.Markup);
            Assert.Contains("Created 2, updated 1, skipped 3, errors 0.", cut.Markup);
            Assert.Contains(">3<", cut.Markup);
        });
    }

    private static IRenderedComponent<SettingsPage> RenderFortnoxSettings(FortnoxSettingsHarness harness, Guid companyId)
    {
        harness.Navigation.NavigateTo($"http://localhost/finance/settings/integrations/fortnox?companyId={companyId:D}");
        return harness.Context.RenderComponent<SettingsPage>(parameters => parameters.Add(x => x.CompanyId, companyId));
    }

    private static FortnoxConnectionStatusResponse ConnectedStatus() =>
        new()
        {
            IsConnected = true,
            ConnectionId = Guid.Parse("3df69f40-7d74-4daa-990f-449cce60e2c4"),
            ConnectionStatus = "connected",
            AccessTokenExpiresUtc = new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            LastRefreshAttemptUtc = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc)
        };

    private static FortnoxSettingsHarness CreateHarness(Guid companyId, FortnoxConnectionStatusResponse status)
    {
        var context = new TestContext();
        var requests = new List<HttpRequestMessage>();

        context.Services.AddOptions();
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUser(companyId, "owner")),
                _ => CreateNotFoundResponse()
            });
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        context.Services.AddSingleton(new FinanceApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/finance/integrations/fortnox/status", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(status));
            }

            if (path.EndsWith("/finance/integrations/fortnox/sync", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new FortnoxSyncResultResponse
                {
                    CompanyId = companyId,
                    ConnectionId = status.ConnectionId ?? Guid.NewGuid(),
                    StartedUtc = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc),
                    CompletedUtc = new DateTime(2026, 4, 30, 10, 1, 0, DateTimeKind.Utc),
                    Status = "succeeded",
                    Created = 2,
                    Updated = 1,
                    Skipped = 3
                }));
            }

            if (path.EndsWith("/finance/integrations/fortnox/sync-history", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new FortnoxSyncHistoryResponse
                {
                    CompanyId = companyId,
                    Items =
                    [
                        new FortnoxSyncHistoryItemResponse
                        {
                            Id = Guid.NewGuid(),
                            ConnectionId = status.ConnectionId,
                            StartedUtc = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc),
                            CompletedUtc = new DateTime(2026, 4, 30, 10, 1, 0, DateTimeKind.Utc),
                            Status = "succeeded",
                            Created = 2,
                            Updated = 1,
                            Skipped = 3,
                            Errors = 0,
                            Summary = "Created 2, updated 1, skipped 3, errors 0."
                        }
                    ]
                }));
            }

            if (path.EndsWith("/finance/integrations/fortnox/disconnect", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new FortnoxConnectionDisconnectResponse
                {
                    CompanyId = companyId,
                    ConnectionId = status.ConnectionId,
                    Status = "disconnected",
                    DisconnectedUtc = DateTime.UtcNow,
                    Message = "Fortnox has been disconnected."
                }));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new FortnoxSettingsHarness(context, context.Services.GetRequiredService<FakeNavigationManager>(), requests);
    }

    private static CurrentUserContextViewModel CreateCurrentUser(Guid companyId, string membershipRole) =>
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
        new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound) { Content = JsonContent.Create(new { title = "Not found", detail = "Not found." }) };

    private sealed record FortnoxSettingsHarness(TestContext Context, FakeNavigationManager Navigation, List<HttpRequestMessage> Requests) : IDisposable
    {
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
