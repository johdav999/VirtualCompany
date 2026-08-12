using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Infrastructure.Mailbox;

namespace VirtualCompany.Api.Tests;

public sealed class MailboxCalendarOAuthScopeTests
{
    [Fact]
    public void Google_mailbox_and_calendar_authorizations_use_separate_scopes()
    {
        var options = new MailboxIntegrationOptions
        {
            Gmail = OAuth("https://accounts.google.test/authorize")
        };
        var provider = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(options),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var mailboxQuery = Uri.UnescapeDataString(provider.BuildAuthorizationUrl(Request()).Query);
        var calendarQuery = Uri.UnescapeDataString(provider.BuildAuthorizationUrl(
            Request(CalendarOAuthScopes.For(VirtualCompany.Domain.Enums.ExternalAccountProvider.Google))).Query);

        Assert.DoesNotContain("calendar.events", provider.DefaultScopes);
        Assert.DoesNotContain("calendar.events", mailboxQuery);
        Assert.Contains("gmail.readonly", mailboxQuery);
        Assert.Contains("calendar.events", calendarQuery);
        Assert.Contains("calendar.events.freebusy", calendarQuery);
        Assert.DoesNotContain("/auth/gmail.", calendarQuery);
        Assert.Contains("include_granted_scopes=true", calendarQuery);
    }

    [Fact]
    public async Task Google_calendar_identity_uses_oidc_userinfo_without_gmail_api()
    {
        var handler = new RecordingHandler(
            """{"sub":"google-account","email":"calendar@example.com","name":"Calendar Owner"}""");
        var options = new MailboxIntegrationOptions
        {
            Gmail = OAuth("https://accounts.google.test/authorize")
        };
        var provider = new GmailMailboxProviderClient(
            new StaticHttpClientFactory(handler),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(options),
            NullLogger<GmailMailboxProviderClient>.Instance);

        var profile = await provider.GetExternalAccountProfileAsync(
            "calendar-token", CancellationToken.None);

        Assert.Equal("calendar@example.com", profile.EmailAddress);
        Assert.Equal("Calendar Owner", profile.DisplayName);
        Assert.Equal("google-account", profile.ProviderAccountId);
        Assert.Equal(
            "https://openidconnect.googleapis.com/v1/userinfo",
            handler.LastRequestUri?.ToString());
    }
    [Fact]
    public void Microsoft_mailbox_and_calendar_authorizations_use_separate_scopes()
    {
        var options = new MailboxIntegrationOptions
        {
            Microsoft365 = OAuth("https://login.microsoft.test/authorize")
        };
        var provider = new Microsoft365MailboxProviderClient(
            new StaticHttpClientFactory(),
            new StaticOptionsMonitor<MailboxIntegrationOptions>(options));

        var mailboxQuery = Uri.UnescapeDataString(provider.BuildAuthorizationUrl(Request()).Query);
        var calendarQuery = Uri.UnescapeDataString(provider.BuildAuthorizationUrl(
            Request(CalendarOAuthScopes.For(VirtualCompany.Domain.Enums.ExternalAccountProvider.Microsoft365))).Query);

        Assert.DoesNotContain("Calendars.ReadWrite", provider.DefaultScopes);
        Assert.DoesNotContain("Calendars.ReadWrite", mailboxQuery);
        Assert.Contains("Mail.Read", mailboxQuery);
        Assert.Contains("Calendars.ReadWrite", calendarQuery);
        Assert.DoesNotContain("Mail.Read", calendarQuery);
        Assert.DoesNotContain("Mail.Send", calendarQuery);
    }
    private static MailboxAuthorizationRequest Request(IReadOnlyCollection<string>? scopes = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), new Uri("https://app.example.test/oauth/callback"), "state-token", RequestedScopes: scopes);

    private static MailboxIntegrationOptions.OAuthProviderOptions OAuth(string authorizationEndpoint) =>
        new()
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            AuthorizationEndpoint = authorizationEndpoint,
            TokenEndpoint = "https://provider.example.test/token",
            ProfileEndpoint = "https://provider.example.test/profile",
            MessagesEndpoint = "https://provider.example.test/messages"
        };

    private sealed class StaticHttpClientFactory(HttpMessageHandler? handler = null) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(string json) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
