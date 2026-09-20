using System.Net;
using System.Text;
using System.Text.Json;

namespace VirtualCompany.Web.Tests;

public sealed class DocumentRepositoryApiClientTests
{
    [Fact]
    public async Task Typed_client_uses_company_transport_for_complete_read_only_workflow()
    {
        var companyId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var handler = new RepositoryHandler(companyId, connectionId);
        var client = CreateClient(handler);

        var listed = await client.ListAsync(companyId);
        var saved = await client.SaveAsync(companyId, null, Request());
        var validation = await client.ValidateAsync(companyId, connectionId);
        var browse = await client.BrowseAsync(companyId, connectionId, "root item");
        var import = await client.StartImportAsync(companyId, connectionId);
        var sync = await client.StartSynchronizationAsync(companyId, connectionId, true);
        var paused = await client.SetPauseAsync(companyId, connectionId, "synchronization", true, 7);
        var retry = await client.RetryFailedItemsAsync(companyId, connectionId);
        await client.ReconcilePublicationAsync(companyId, Guid.NewGuid());
        await client.DisconnectAsync(companyId, connectionId, 7);

        Assert.Single(listed!);
        Assert.Equal(3, saved.IndexedDocumentCount);
        Assert.True(validation.IsAccessible);
        Assert.Single(browse.Items);
        Assert.Equal("queued", import.Status);
        Assert.Equal("full_reconciliation", sync.Mode);
        Assert.NotNull(paused);
        Assert.Equal("full_reconciliation", retry.Mode);
        Assert.All(handler.Requests, request => Assert.Equal(companyId.ToString(), request.Headers.GetValues("X-Company-Id").Single()));
        Assert.Contains("parentItemId=root%20item", handler.Requests[3].RequestUri!.Query);
        Assert.Contains(handler.Bodies, body => body.Contains("\"forceFullReconciliation\":true", StringComparison.Ordinal));
        Assert.Contains(handler.Bodies, body => body.Contains("\"expectedConcurrencyVersion\":7", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task List_returns_null_without_disclosing_repository_state(HttpStatusCode status)
    {
        var client = CreateClient(new StaticHandler(status, string.Empty));
        Assert.Null(await client.ListAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Mutation_preserves_safe_problem_and_status()
    {
        var client = CreateClient(new StaticHandler(HttpStatusCode.Forbidden, "{\"detail\":\"Administrator access is required.\"}"));
        var exception = await Assert.ThrowsAsync<DocumentRepositoryApiException>(() => client.ValidateAsync(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal("Administrator access is required.", exception.Message);
    }

    private static ConfigureDocumentRepositoryRequest Request() => new()
    {
        DirectoryTenantId = Guid.NewGuid(), ApplicationClientId = Guid.NewGuid(), CredentialReference = "kv://graph",
        DriveId = "drive", RootItemId = "root", DisplayName = "Knowledge", AgentIds = []
    };

    private static DocumentRepositoryApiClient CreateClient(HttpMessageHandler handler) => new(
        new CompanyApiTransport(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") }),
        false, new FallbackProblemResolver());

    private sealed class FallbackProblemResolver : IApiProblemMessageResolver
    {
        public string Resolve(ApiProblemResponse? problem, string fallbackMessage) => problem?.Detail ?? fallbackMessage;
    }

    private sealed class RepositoryHandler(Guid companyId, Guid connectionId) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            var path = request.RequestUri!.AbsolutePath;
            var connection = $$"""{"id":"{{connectionId}}","companyId":"{{companyId}}","providerKind":"sharepoint_library","directoryTenantId":"{{Guid.NewGuid()}}","applicationClientId":"{{Guid.NewGuid()}}","driveId":"drive","rootItemId":"root","displayName":"Knowledge","isReadOnly":true,"lifecycleState":"active","audience":"company","agentIds":[],"concurrencyVersion":7,"createdUtc":"2026-09-19T10:00:00Z","updatedUtc":"2026-09-19T10:00:00Z","indexedDocumentCount":3,"processingDocumentCount":1,"failedDocumentCount":0}""";
            var json = request.Method == HttpMethod.Get && path.EndsWith("/document-repositories", StringComparison.Ordinal)
                ? $"[{connection}]"
                : path.EndsWith("/validate", StringComparison.Ordinal) ? """{"isAccessible":true,"code":"succeeded","repositoryName":"Knowledge","rootName":"Root"}"""
                : path.EndsWith("/browse", StringComparison.Ordinal) ? """{"parentItemId":"root","items":[{"itemId":"folder","name":"Policies","isFolder":true}],"isTruncated":false}"""
                : path.EndsWith("/imports", StringComparison.Ordinal) ? $$"""{"id":"{{Guid.NewGuid()}}","connectionId":"{{connectionId}}","status":"queued","discoveredCount":0,"processedCount":0,"failedCount":0,"createdUtc":"2026-09-19T10:00:00Z","updatedUtc":"2026-09-19T10:00:00Z","items":[]}"""
                : path.EndsWith("/synchronizations", StringComparison.Ordinal) || path.EndsWith("/retry-failed-items", StringComparison.Ordinal) ? $$"""{"id":"{{Guid.NewGuid()}}","connectionId":"{{connectionId}}","status":"queued","mode":"full_reconciliation","attemptCount":0,"observedCount":0,"changedCount":0,"removedCount":0,"failedCount":0,"createdUtc":"2026-09-19T10:00:00Z","updatedUtc":"2026-09-19T10:00:00Z"}"""
                : path.EndsWith("/reconcile", StringComparison.Ordinal) ? $$"""{"id":"{{Guid.NewGuid()}}","status":"reconciliation_required"}"""
                : path.EndsWith("/disconnect", StringComparison.Ordinal) ? string.Empty : connection;
            return new HttpResponseMessage(path.EndsWith("/disconnect", StringComparison.Ordinal) ? HttpStatusCode.NoContent : HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StaticHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/problem+json") });
    }
}
