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

public sealed class FinanceMailboxPageTests
{
    [Fact]
    public void Mailbox_page_renders_status_connect_actions_and_scan_summary()
    {
        var companyId = Guid.Parse("d53590ef-f7ff-4b98-a372-a9f3133e0f6c");
        var status = new MailboxConnectionStatusResponse
        {
            IsConnected = true,
            MailboxConnectionId = Guid.NewGuid(),
            Provider = "gmail",
            ConnectionStatus = "active",
            EmailAddress = "ap@example.com",
            DisplayName = "Accounts Payable",
            LastSuccessfulScanAtUtc = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
            LastRun = new EmailIngestionRunSummaryResponse
            {
                Id = Guid.NewGuid(),
                Provider = "gmail",
                StartedUtc = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
                CompletedUtc = new DateTime(2026, 4, 26, 8, 1, 0, DateTimeKind.Utc),
                ScanFromUtc = new DateTime(2026, 3, 27, 8, 0, 0, DateTimeKind.Utc),
                ScanToUtc = new DateTime(2026, 4, 26, 8, 0, 0, DateTimeKind.Utc),
                ScannedMessageCount = 12,
                DetectedCandidateCount = 3
            }
        };
        var messages = new[]
        {
            new MailboxScannedMessageResponse
            {
                Id = Guid.NewGuid(),
                EmailIngestionRunId = status.LastRun.Id,
                ExternalMessageId = "gmail-message-1",
                FromAddress = "johan@example.com",
                FromDisplayName = "Johan Davidsson",
                Subject = "INVOICE Nordic IT Solutions AB",
                ReceivedUtc = new DateTime(2026, 4, 29, 9, 57, 29, DateTimeKind.Utc),
                SourceType = "email_body_only",
                CandidateDecision = "candidate",
                MatchedRules = ["keyword_match"],
                ReasonSummary = "Matched deterministic bill detection rules.",
                BodyPreview = "Supplier: Nordic IT Solutions AB"
            }
        };

        using var harness = CreateHarness(companyId, status, messages: messages);
        harness.Navigation.NavigateTo($"http://localhost/finance/mailbox?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<MailboxPage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Mailbox connection", cut.Markup);
            Assert.Contains("Connect Gmail", cut.Markup);
            Assert.Contains("Connect Microsoft 365", cut.Markup);
            Assert.Contains("Scan inbox for bills", cut.Markup);
            Assert.Contains("Gmail", cut.Markup);
            Assert.Contains("Accounts Payable", cut.Markup);
            Assert.Contains("Messages scanned", cut.Markup);
            Assert.Contains("12", cut.Markup);
            Assert.Contains("Detected candidates", cut.Markup);
            Assert.Contains("3", cut.Markup);
            Assert.Contains("Scanned inbox", cut.Markup);
            Assert.Contains("INVOICE Nordic IT Solutions AB", cut.Markup);
            Assert.Contains("Supplier: Nordic IT Solutions AB", cut.Markup);
        });
    }

    [Fact]
    public void Mailbox_page_disables_scan_when_no_connection_exists()
    {
        var companyId = Guid.Parse("d53590ef-f7ff-4b98-a372-a9f3133e0f6c");
        using var harness = CreateHarness(companyId, new MailboxConnectionStatusResponse());
        harness.Navigation.NavigateTo($"http://localhost/finance/mailbox?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<MailboxPage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Not connected", cut.Markup);
            Assert.Contains("Connect an active mailbox before starting an inbox scan.", cut.Markup);
            Assert.Contains("disabled", cut.FindAll("button").Single(x => x.TextContent.Contains("Scan inbox for bills", StringComparison.Ordinal)).OuterHtml);
        });
    }

    [Fact]
    public void Mailbox_page_keeps_connect_actions_enabled_when_provider_setup_is_missing()
    {
        var companyId = Guid.Parse("d53590ef-f7ff-4b98-a372-a9f3133e0f6c");
        using var harness = CreateHarness(
            companyId,
            new MailboxConnectionStatusResponse(),
            new MailboxProviderAvailabilityResponse
            {
                Gmail = new MailboxProviderAvailability
                {
                    Provider = "gmail",
                    DisplayName = "Gmail",
                    IsConfigured = false,
                    UnavailableReason = "This mailbox provider is not configured by an administrator yet."
                },
                Microsoft365 = new MailboxProviderAvailability
                {
                    Provider = "microsoft365",
                    DisplayName = "Microsoft 365",
                    IsConfigured = false,
                    UnavailableReason = "This mailbox provider is not configured by an administrator yet."
                }
            });
        harness.Navigation.NavigateTo($"http://localhost/finance/mailbox?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<MailboxPage>(parameters => parameters.Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Admin setup required", cut.Markup);
            Assert.False(cut.FindAll("button").Single(x => x.TextContent.Contains("Connect Gmail", StringComparison.Ordinal)).HasAttribute("disabled"));
            Assert.False(cut.FindAll("button").Single(x => x.TextContent.Contains("Connect Microsoft 365", StringComparison.Ordinal)).HasAttribute("disabled"));
        });
    }

    private static MailboxPageHarness CreateHarness(
        Guid companyId,
        MailboxConnectionStatusResponse status,
        MailboxProviderAvailabilityResponse? providerAvailability = null,
        IReadOnlyList<MailboxScannedMessageResponse>? messages = null)
    {
        var context = new TestContext();
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
            var path = request.RequestUri?.AbsolutePath;
            if (path == $"/api/companies/{companyId:D}/mailbox-connections/current" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(status));
            }

            if (path == $"/api/companies/{companyId:D}/mailbox-connections/providers" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(providerAvailability ?? CreateConfiguredProviderAvailability()));
            }

            if (path == $"/api/companies/{companyId:D}/mailbox-connections/messages" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(messages ?? []));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new MailboxPageHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
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

    private static MailboxProviderAvailabilityResponse CreateConfiguredProviderAvailability() =>
        new()
        {
            Gmail = new MailboxProviderAvailability
            {
                Provider = "gmail",
                DisplayName = "Gmail",
                IsConfigured = true
            },
            Microsoft365 = new MailboxProviderAvailability
            {
                Provider = "microsoft365",
                DisplayName = "Microsoft 365",
                IsConfigured = true
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

    private sealed record MailboxPageHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
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
