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

public sealed class FinanceEmailSettingsPageTests
{
    [Fact]
    public void Settings_page_shows_stable_provider_redirect_uris()
    {
        var companyId = Guid.Parse("f73175bd-58d6-4d5d-96d6-10b721ffbd5a");

        using var harness = CreateHarness(companyId);
        harness.Navigation.NavigateTo($"http://localhost/finance/settings/email-settings?companyId={companyId:D}");

        var cut = harness.Context.RenderComponent<SettingsPage>(parameters => parameters
            .Add(x => x.CompanyId, companyId));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Register one redirect URI per provider per environment.", cut.Markup);
            Assert.Contains("http://localhost:5301/api/mailbox-connections/gmail/callback", cut.Markup);
            Assert.Contains("http://localhost:5301/api/mailbox-connections/microsoft365/callback", cut.Markup);
            Assert.Contains("company context is carried in protected OAuth state", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"/api/companies/{companyId:D}/mailbox-connections/gmail/callback", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"/api/companies/{companyId:D}/mailbox-connections/microsoft365/callback", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static EmailSettingsHarness CreateHarness(Guid companyId)
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
            if (request.RequestUri?.AbsolutePath == $"/internal/companies/{companyId:D}/finance/settings/email" &&
                request.Method == HttpMethod.Get)
            {
                return Task.FromResult(CreateJsonResponse(new FinanceEmailSettingsResponse
                {
                    IsWritable = true,
                    RequiresRestart = false,
                    Gmail = new FinanceEmailProviderSettingsResponse
                    {
                        ClientId = "gmail-client",
                        IsClientIdConfigured = true,
                        IsClientSecretConfigured = true
                    },
                    Microsoft365 = new FinanceEmailProviderSettingsResponse
                    {
                        ClientId = "microsoft-client",
                        IsClientIdConfigured = true,
                        IsClientSecretConfigured = true
                    }
                }));
            }

            return Task.FromResult(CreateNotFoundResponse());
        }))
        {
            BaseAddress = new Uri("http://localhost/")
        }));

        return new EmailSettingsHarness(context, context.Services.GetRequiredService<FakeNavigationManager>());
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
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static HttpResponseMessage CreateNotFoundResponse() =>
        new(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not found", detail = "Not found." })
        };

    private sealed record EmailSettingsHarness(TestContext Context, FakeNavigationManager Navigation) : IDisposable
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
