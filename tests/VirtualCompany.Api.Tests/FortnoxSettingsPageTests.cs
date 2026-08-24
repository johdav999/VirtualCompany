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

public sealed class FinanceIntegrationSettingsPageTests
{
    [Theory]
    [InlineData(null, "Not connected", "Connect accounting system")]
    [InlineData("pending", "Connecting", "Hide sync history")]
    [InlineData("connected", "Connected", "Sync data")]
    [InlineData("needs_reconnect", "Needs reconnect", "Reconnect")]
    [InlineData("error", "Needs attention", "Reconnect")]
    public void Provider_card_renders_connection_states_and_expected_actions(string? status, string label, string action)
    {
        var companyId = Guid.Parse("8c212fb5-71cd-42be-a237-5e893285f315");
        using var harness = CreateHarness(companyId, new FinanceIntegrationConnectionStatusResponse
        {
            ProviderKey = "fortnox",
            IsConnected = status == "connected",
            ConnectionId = status is null ? null : Guid.Parse("7d03c4c5-d287-4f67-995d-af6a70cf6a1a"),
            ConnectionStatus = status,
            LastErrorSummary = status == "error" ? "Fortnox reported an authorization problem." : null,
            LastSuccessfulSyncUtc = status == "connected" ? new DateTime(2026, 4, 30, 10, 1, 0, DateTimeKind.Utc) : null
        });

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Fortnox", cut.Markup);
            Assert.Contains(label, cut.Markup);
            Assert.Contains(action, cut.Markup);
            Assert.Contains("Hide sync history", cut.Markup);
            Assert.Contains("Approval-gated writes", cut.Markup);
            Assert.DoesNotContain("payload hash", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Token expiry", cut.Markup);
            Assert.DoesNotContain("Last token refresh", cut.Markup);
            Assert.DoesNotContain(">needs_reconnect<", cut.Markup);
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
            Assert.Contains("Sync data", cut.Markup);
            Assert.Contains("Disconnect", cut.Markup);
            Assert.Contains("Hide sync history", cut.Markup);
            Assert.DoesNotContain("Connect Fortnox", cut.Markup);
        });
    }

    [Fact]
    public void Finance_viewer_can_review_status_but_cannot_trigger_integration_actions()
    {
        var companyId = Guid.Parse("4fa845cb-fcd6-43bb-b7a2-31db92062f62");
        using var harness = CreateHarness(companyId, ConnectedStatus(), membershipRole: "manager");

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected", cut.Markup);
            Assert.Contains("Company owner or admin access is required to connect, sync, reconnect, or disconnect Fortnox.", cut.Markup);
            Assert.Contains("Hide sync history", cut.Markup);
            Assert.DoesNotContain("Sync data", cut.Markup);
            Assert.DoesNotContain("Disconnect", cut.Markup);
            Assert.DoesNotContain("Reconnect", cut.Markup);
        });
    }

    [Fact]
    public void Tester_cannot_trigger_admin_only_integration_actions()
    {
        var companyId = Guid.Parse("822969ac-48b9-46c3-a338-42ea29611f04");
        using var harness = CreateHarness(companyId, ConnectedStatus(), membershipRole: "tester");

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected", cut.Markup);
            Assert.Contains("Hide sync history", cut.Markup);
            Assert.DoesNotContain("Sync data", cut.Markup);
            Assert.DoesNotContain("Disconnect", cut.Markup);
            Assert.DoesNotContain(harness.Requests, request =>
                request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.Contains("/finance/integrations/fortnox/", StringComparison.OrdinalIgnoreCase));
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
            Assert.Contains("Sync finished: 2 created, 1 updated, 3 skipped.", cut.Markup);
            Assert.Contains(harness.Requests, request =>
                request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("/finance/integrations/fortnox/sync", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Initial_load_shows_recent_sync_history()
    {
        var companyId = Guid.Parse("902b85fd-64dd-412a-a64b-c2f172df05d9");
        using var harness = CreateHarness(companyId, ConnectedStatus());
        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sync history", cut.Markup);
            Assert.Contains("Imported records", cut.Markup);
            Assert.Contains("Duration", cut.Markup);
            Assert.Contains(">3<", cut.Markup);
            Assert.Contains("1 minute", cut.Markup);
        });
    }

    [Fact]
    public void Connect_posts_to_connect_endpoint_and_navigates_to_authorization_url()
    {
        var companyId = Guid.Parse("f066aa85-e7c9-4598-a21f-e8830f023927");
        using var harness = CreateHarness(companyId, new FinanceIntegrationConnectionStatusResponse
        {
            ProviderKey = "fortnox",
            IsConnected = false,
            ConnectionStatus = "disconnected"
        });
        var cut = RenderFortnoxSettings(harness, companyId);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Connect accounting system", StringComparison.OrdinalIgnoreCase)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains(harness.Requests, request =>
                request.Method == HttpMethod.Post &&
                request.RequestUri!.AbsolutePath.EndsWith("/finance/integrations/fortnox/connect", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith("https://apps.fortnox.test/oauth", harness.Navigation.Uri, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Unsafe_backend_error_details_are_not_rendered()
    {
        var companyId = Guid.Parse("af1670d7-c61d-4db4-8277-3a1cb4a32272");
        using var harness = CreateHarness(companyId, ConnectedStatus(), syncFailureDetail: "System.InvalidOperationException: access_token leaked\n   at Provider.Raw()");
        var cut = RenderFortnoxSettings(harness, companyId);

        cut.Find("button.btn-outline-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sync failed. Review the latest sync history entry for a safe summary.", cut.Markup);
            Assert.DoesNotContain("access_token", cut.Markup);
            Assert.DoesNotContain("InvalidOperationException", cut.Markup);
        });
    }

    [Fact]
    public void Empty_sync_history_renders_empty_state()
    {
        var companyId = Guid.Parse("9f89c3e5-fb89-48f9-8e62-6f5c16e3f31d");
        using var harness = CreateHarness(companyId, ConnectedStatus(), historyEmpty: true);

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Sync history", cut.Markup);
            Assert.Contains("No accounting syncs have run yet.", cut.Markup);
            Assert.DoesNotContain("Imported records", cut.Markup);
        });
    }

    [Fact]
    public void Sync_history_failure_renders_safe_error_without_hiding_connection_state()
    {
        var companyId = Guid.Parse("a326984f-c084-43ce-8f9a-5aa5ef03c82f");
        using var harness = CreateHarness(
            companyId,
            ConnectedStatus(),
            historyFailureDetail: "System.Net.Http.HttpRequestException: provider_payload access_token secret");

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected", cut.Markup);
            Assert.Contains("Sync history is unavailable. Please try again.", cut.Markup);
            Assert.DoesNotContain("access_token", cut.Markup);
            Assert.DoesNotContain("provider_payload", cut.Markup);
            Assert.DoesNotContain("HttpRequestException", cut.Markup);
        });
    }

    [Fact]
    public void OAuth_success_callback_renders_connection_success_feedback()
    {
        var companyId = Guid.Parse("f2e67788-4d46-463c-8d08-3ae82efa0f6d");
        using var harness = CreateHarness(companyId, ConnectedStatus());

        var cut = RenderFortnoxSettings(harness, companyId, "integrationConnection=connected");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Fortnox is connected.", cut.Markup);
            Assert.Contains("Connected", cut.Markup);
        });
    }

    [Fact]
    public void OAuth_failure_callback_renders_safe_error_feedback()
    {
        var companyId = Guid.Parse("c6f3b0ef-c8db-4dc6-a7ef-063fb3128d66");
        using var harness = CreateHarness(companyId, new FinanceIntegrationConnectionStatusResponse
        {
            ProviderKey = "fortnox",
            IsConnected = false,
            ConnectionStatus = "disconnected"
        });

        var cut = RenderFortnoxSettings(
            harness,
            companyId,
            "integrationMessage=System.InvalidOperationException%3A%20access_token%20leaked%0A%20at%20Callback");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Finance integration authorization could not be completed.", cut.Markup);
            Assert.DoesNotContain("access_token", cut.Markup);
            Assert.DoesNotContain("InvalidOperationException", cut.Markup);
        });
    }

    [Fact]
    public void Token_refresh_failure_state_uses_safe_reconnect_message()
    {
        var companyId = Guid.Parse("44e8bb15-976c-4f57-9de3-ff051fb0f692");
        using var harness = CreateHarness(companyId, new FinanceIntegrationConnectionStatusResponse
        {
            ProviderKey = "fortnox",
            IsConnected = false,
            ConnectionId = Guid.Parse("8b734fde-2bcf-4a11-bd01-4d13f555b17e"),
            ConnectionStatus = "needs_reconnect",
            LastErrorSummary = "System.Security.Authentication.AuthenticationException: refresh_token expired"
        });

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Needs reconnect", cut.Markup);
            Assert.Contains("Fortnox needs a fresh authorization before syncing can continue.", cut.Markup);
            Assert.Contains("Reconnect", cut.Markup);
            Assert.DoesNotContain("refresh_token", cut.Markup);
            Assert.DoesNotContain("AuthenticationException", cut.Markup);
        });
    }

    [Fact]
    public void Finance_integration_requests_are_scoped_to_active_tenant()
    {
        var companyId = Guid.Parse("59f2b0df-257a-4633-99a2-b3903958be72");
        using var harness = CreateHarness(companyId, ConnectedStatus());

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Connected", cut.Markup);
            Assert.All(harness.Requests, request => Assert.Contains($"/api/companies/{companyId:D}/finance/integrations", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase));
            Assert.All(harness.Requests, request => Assert.Equal(companyId.ToString(), request.Headers.GetValues("X-Company-Id").Single()));
        });
    }

    [Fact]
    public void Sync_history_uses_safe_plain_english_error_summary()
    {
        var companyId = Guid.Parse("deff246e-b9d1-42f9-a8b4-f0e94b060c5b");
        using var harness = CreateHarness(
            companyId,
            ConnectedStatus(),
            historyErrorSummary: "{\"status\":\"failed\",\"access_token\":\"secret\",\"provider_payload\":{\"error\":\"bad\"}}",
            historyStatus: "completed_with_errors",
            historyErrors: 2);

        var cut = RenderFortnoxSettings(harness, companyId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Completed with issues", cut.Markup);
            Assert.Contains("Review the sync details.", cut.Markup);
            Assert.Contains("issues 2", cut.Markup);
            Assert.DoesNotContain("access_token", cut.Markup);
            Assert.DoesNotContain("provider_payload", cut.Markup);
            Assert.DoesNotContain("completed_with_errors", cut.Markup);
        });
    }

    private static IRenderedComponent<SettingsPage> RenderFortnoxSettings(FortnoxSettingsHarness harness, Guid companyId, string? query = null)
    {
        var queryString = string.IsNullOrWhiteSpace(query) ? $"companyId={companyId:D}" : $"companyId={companyId:D}&{query}";
        harness.Navigation.NavigateTo($"http://localhost/finance/settings/integrations/fortnox?{queryString}");
        return harness.Context.RenderComponent<SettingsPage>(parameters => parameters.Add(x => x.CompanyId, companyId));
    }

    private static FinanceIntegrationConnectionStatusResponse ConnectedStatus() =>
        new()
        {
            ProviderKey = "fortnox",
            IsConnected = true,
            ConnectionId = Guid.Parse("3df69f40-7d74-4daa-990f-449cce60e2c4"),
            ConnectionStatus = "connected",
            AccessTokenExpiresUtc = new DateTime(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            LastRefreshAttemptUtc = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc),
            LastSuccessfulSyncUtc = new DateTime(2026, 4, 30, 10, 1, 0, DateTimeKind.Utc)
        };

    private static FortnoxSettingsHarness CreateHarness(
        Guid companyId,
        FinanceIntegrationConnectionStatusResponse status,
        string membershipRole = "owner",
        string? syncFailureDetail = null,
        string? historyErrorSummary = null,
        string historyStatus = "succeeded",
        int historyErrors = 0,
        bool historyEmpty = false,
        string? historyFailureDetail = null)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        var requests = new List<HttpRequestMessage>();

        context.Services.AddOptions();
        context.Services.AddSingleton(new FinanceAccessResolver());
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient(new AsyncStubHttpMessageHandler((request, _) =>
        {
            return Task.FromResult(request.RequestUri?.AbsolutePath switch
            {
                "/api/auth/me" => CreateJsonResponse(CreateCurrentUser(companyId, membershipRole)),
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
            if (path.EndsWith("/finance/integrations", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new List<FinanceIntegrationProviderResponse>
                {
                    new()
                    {
                        ProviderKey = "fortnox",
                        DisplayName = "Fortnox",
                        Capabilities = ["invoices", "payments", "write"],
                        Status = status
                    }
                }));
            }

            if (path.EndsWith("/finance/integrations/fortnox/status", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(status));
            }

            if (path.EndsWith("/finance/integrations/fortnox/connect", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith("/finance/integrations/fortnox/reconnect", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new StartFinanceIntegrationConnectionResponse
                {
                    AuthorizationUrl = "https://apps.fortnox.test/oauth?state=test",
                    ExpiresUtc = new DateTime(2026, 4, 30, 10, 10, 0, DateTimeKind.Utc)
                }));
            }

            if (path.EndsWith("/finance/integrations/fortnox/sync", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(syncFailureDetail))
                {
                    return Task.FromResult(CreateProblemResponse(HttpStatusCode.BadGateway, syncFailureDetail));
                }

                return Task.FromResult(CreateJsonResponse(new FinanceIntegrationSyncResultResponse
                {
                    ProviderKey = "fortnox",
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
                if (!string.IsNullOrWhiteSpace(historyFailureDetail))
                {
                    return Task.FromResult(CreateProblemResponse(HttpStatusCode.BadGateway, historyFailureDetail));
                }

                return Task.FromResult(CreateJsonResponse(new FinanceIntegrationSyncHistoryResponse
                {
                    ProviderKey = "fortnox",
                    CompanyId = companyId,
                    Items = historyEmpty
                    ? []
                    : [
                        new FinanceIntegrationSyncHistoryItemResponse
                        {
                            Id = Guid.NewGuid(),
                            ConnectionId = status.ConnectionId,
                            StartedUtc = new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc),
                            CompletedUtc = new DateTime(2026, 4, 30, 10, 1, 0, DateTimeKind.Utc),
                            Status = historyStatus,
                            Created = 2,
                            Updated = 1,
                            Skipped = 3,
                            Errors = historyErrors,
                            Summary = $"Created 2, updated 1, skipped 3, errors {historyErrors}.",
                            ErrorSummary = historyErrorSummary
                        }
                    ]
                }));
            }

            if (path.EndsWith("/finance/integrations/fortnox/disconnect", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(new FinanceIntegrationConnectionDisconnectResponse
                {
                    ProviderKey = "fortnox",
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

    private static HttpResponseMessage CreateProblemResponse(HttpStatusCode statusCode, string detail) =>
        new(statusCode)
        {
            Content = JsonContent.Create(new
            {
                title = "Finance request failed",
                detail
            })
        };

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
