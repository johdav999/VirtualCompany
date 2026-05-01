using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxApiClientTests
{
    [Fact]
    public async Task Requests_use_real_base_url_and_bearer_token()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""{"CompanyInformation":{"CompanyName":"Acme AB"}}""")
        });
        var client = CreateClient(handler);
        var context = new FortnoxRequestContext(Guid.NewGuid(), Guid.NewGuid(), "corr-1");

        await client.GetCompanyInformationAsync(context, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(new Uri("https://api.fortnox.se/3/companyinformation"), request.RequestUri);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("access-token", request.Headers.Authorization?.Parameter);
        Assert.True(request.Headers.TryGetValues("X-Correlation-ID", out var correlationIds));
        Assert.Equal("corr-1", Assert.Single(correlationIds));
    }

    [Fact]
    public async Task Query_parameters_are_serialized_and_null_values_are_omitted()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "Customers": [],
                  "MetaInformation": {
                    "@CurrentPage": 2,
                    "@TotalPages": 3,
                    "@TotalResources": 12,
                    "@Limit": 5
                  }
                }
                """)
        });
        var client = CreateClient(handler);
        var options = new FortnoxPageOptions(
            LastModified: new DateTimeOffset(2026, 4, 30, 8, 15, 0, TimeSpan.Zero),
            FromDate: new DateOnly(2026, 4, 1),
            ToDate: new DateOnly(2026, 4, 30),
            SortBy: "name",
            SortOrder: "ascending",
            Page: 2,
            Limit: 5);

        await client.GetCustomersAsync(new FortnoxRequestContext(Guid.NewGuid()), options, CancellationToken.None);

        var query = Assert.Single(handler.Requests).RequestUri!.Query.TrimStart('?');
        Assert.Equal("lastmodified=2026-04-30%2008%3A15&fromdate=2026-04-01&todate=2026-04-30&sortby=name&sortorder=ascending&page=2&limit=5", query);
        Assert.DoesNotContain("null", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Paged_endpoint_parses_items_and_metadata()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "Invoices": [
                    { "DocumentNumber": "1001", "CustomerName": "Acme AB", "Total": 1250.50 }
                  ],
                  "MetaInformation": {
                    "@CurrentPage": 1,
                    "@TotalPages": 2,
                    "@TotalResources": 3,
                    "@Limit": 2
                  }
                }
                """)
        });
        var client = CreateClient(handler);

        var page = await client.GetInvoicesAsync(new FortnoxRequestContext(Guid.NewGuid()), new FortnoxPageOptions(Page: 1, Limit: 2), CancellationToken.None);

        var invoice = Assert.Single(page.Items);
        Assert.Equal("1001", invoice.DocumentNumber);
        Assert.Equal("Acme AB", invoice.CustomerName);
        Assert.Equal(1, page.Metadata.CurrentPage);
        Assert.Equal(2, page.Metadata.TotalPages);
        Assert.Equal(3, page.Metadata.TotalResources);
        Assert.True(page.HasNextPage);
    }

    [Fact]
    public async Task Retries_transient_server_failures()
    {
        var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = Json("""{"ErrorInformation":{"Error":"500","Message":"temporary"}}""") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"Accounts":[{"Number":1930,"Description":"Bank"}]}""") });
        var client = CreateClient(handler);

        var page = await client.GetAccountsAsync(new FortnoxRequestContext(Guid.NewGuid()), null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(1930, Assert.Single(page.Items).Number);
    }

    [Fact]
    public async Task Rate_limit_response_honors_retry_after_before_retry()
    {
        var first = new HttpResponseMessage((HttpStatusCode)429)
        {
            Content = Json("""{"ErrorInformation":{"Error":"429","Message":"rate limited"}}""")
        };
        first.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new CapturingHandler(
            first,
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"Projects":[{"ProjectNumber":"P-1","Description":"Launch"}]}""") });
        var client = CreateClient(handler);

        var page = await client.GetProjectsAsync(new FortnoxRequestContext(Guid.NewGuid()), null, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("P-1", Assert.Single(page.Items).ProjectNumber);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authorization", "Fortnox connection needs attention.", true)]
    [InlineData(HttpStatusCode.Forbidden, "permission", "The connected Fortnox account does not have permission for this data.", false)]
    [InlineData(HttpStatusCode.NotFound, "not_found", "The requested Fortnox data could not be found.", false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "validation", "Fortnox could not process the requested data.", false)]
    [InlineData(HttpStatusCode.InternalServerError, "upstream_unavailable", "Fortnox is temporarily unavailable. Please try again shortly.", false)]
    public async Task Errors_are_translated_to_safe_messages(
        HttpStatusCode statusCode,
        string category,
        string safeMessage,
        bool requiresReconnect)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(statusCode)
        {
            Content = Json("""{"ErrorInformation":{"Error":"200001","Message":"internal upstream detail"}}""")
        });
        var client = CreateClient(handler, maxRetries: 0);

        var exception = await Assert.ThrowsAsync<FortnoxApiException>(() =>
            client.GetSuppliersAsync(new FortnoxRequestContext(Guid.NewGuid()), null, CancellationToken.None));

        Assert.Equal(category, exception.Category);
        Assert.Equal(safeMessage, exception.SafeMessage);
        Assert.Equal("200001", exception.FortnoxErrorCode);
        Assert.Equal("internal upstream detail", exception.FortnoxErrorMessage);
        Assert.Equal(requiresReconnect, exception.RequiresReconnect);
        Assert.DoesNotContain("access-token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supports_authenticated_post_put_and_delete()
    {
        var handler = new CapturingHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"ok":true}""") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = Json("""{"ok":true}""") },
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = CreateClient(handler);
        var context = new FortnoxRequestContext(Guid.NewGuid());

        await client.PostAsync<object, Dictionary<string, bool>>(context, "customers", new { Customer = new { Name = "Acme AB" } }, CancellationToken.None);
        await client.PutAsync<object, Dictionary<string, bool>>(context, "customers/1", new { Customer = new { Name = "Acme AB" } }, CancellationToken.None);
        await client.DeleteAsync(context, "customers/1", CancellationToken.None);

        Assert.Collection(
            handler.Requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            });
    }

    private static FortnoxApiClient CreateClient(CapturingHandler handler, int maxRetries = 3)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = FortnoxApiClient.NormalizeBaseAddress("https://api.fortnox.se/3")
        };
        var options = Options.Create(new FortnoxOptions
        {
            Enabled = true,
            ApiBaseUrl = "https://api.fortnox.se/3",
            ApiMaxRetries = maxRetries,
            ApiRetryBaseDelayMilliseconds = 1,
            ApiMaxRetryDelaySeconds = 1
        });

        return new FortnoxApiClient(
            httpClient,
            new StubFortnoxOAuthService(),
            new StaticOptionsMonitor<FortnoxOptions>(options.Value),
            NullLogger<FortnoxApiClient>.Instance,
            TimeProvider.System);
    }

    private static StringContent Json(string value) =>
        new(value, Encoding.UTF8, "application/json");

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public CapturingHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK));
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class StubFortnoxOAuthService : IFortnoxOAuthService
    {
        public Task<FortnoxOAuthStartResult> BuildAuthorizationUrlAsync(StartFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxOAuthCompletionResult> HandleCallbackAsync(CompleteFortnoxOAuthConnectionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<FortnoxAccessTokenResult> GetValidAccessTokenAsync(RefreshFortnoxAccessTokenCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(FortnoxAccessTokenResult.Success("access-token", DateTime.UtcNow.AddHours(1)));

        public Task<FortnoxConnectionStatusResult> GetStatusAsync(GetFortnoxConnectionStatusQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task MarkNeedsReconnectAsync(Guid companyId, Guid connectionId, string safeReason, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
