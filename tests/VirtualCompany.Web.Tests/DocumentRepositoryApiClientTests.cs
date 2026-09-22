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

    [Fact]
    public async Task Typed_client_keeps_setup_identifiers_inside_opaque_handles()
    {
        var companyId = Guid.NewGuid();
        var handler = new SetupHandler();
        var client = CreateClient(handler);
        var start = await client.BeginMicrosoftOnboardingAsync(companyId, "/settings/document-repositories");
        var kinds = await client.GetMicrosoftSourceKindsAsync(companyId, start.SessionHandle);
        var sites = await client.SearchMicrosoftSharePointSitesAsync(companyId, start.SessionHandle, "Finance & Legal", "opaque page");
        var libraries = await client.GetMicrosoftSharePointLibrariesAsync(companyId, start.SessionHandle, sites.Items[0].SelectionHandle);
        var folders = await client.BrowseMicrosoftFoldersAsync(companyId, start.SessionHandle, libraries.Items[0].SelectionHandle);
        var selection = await client.SelectMicrosoftRootAsync(companyId, start.SessionHandle, libraries.Items[0].SelectionHandle, folders.Items[0].SelectionHandle, 2);
        var access = await client.ConfigureMicrosoftAccessAsync(companyId, start.SessionHandle, false, null, [], selection.SelectionVersion);
        var review = await client.GetMicrosoftReviewAsync(companyId, start.SessionHandle);
        var finalized = await client.FinalizeMicrosoftRepositoryAsync(companyId, start.SessionHandle, review.DraftVersion);
        var provisioning = await client.GetMicrosoftProvisioningAsync(companyId, start.SessionHandle);
        await client.RetryMicrosoftProvisioningAsync(companyId, start.SessionHandle);
        await client.CleanupMicrosoftProvisioningAsync(companyId, start.SessionHandle);
        await client.CancelMicrosoftOnboardingAsync(companyId, start.SessionHandle, access.DraftVersion);

        Assert.Equal("sharepoint_library", kinds[0].Kind);
        Assert.Equal("Policies", selection.RootDisplayName);
        Assert.False(access.EnableWrites);
        Assert.Equal("Policies", review.RootName);
        Assert.Equal("queued", finalized.Status);
        Assert.Equal("queued", provisioning!.Status);
        Assert.Contains("query=Finance%20%26%20Legal", handler.Requests[2].RequestUri!.Query);
        Assert.Contains("pageHandle=opaque%20page", handler.Requests[2].RequestUri!.Query);
        Assert.All(handler.Requests, request => Assert.DoesNotContain("drive-id", request.RequestUri!.ToString(), StringComparison.Ordinal));
        Assert.Contains(handler.Bodies, body => body.Contains("\"sourceHandle\":\"source-handle\"", StringComparison.Ordinal));
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

    private sealed class SetupHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null) Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            var path = request.RequestUri!.AbsolutePath;
            var json = request.Method == HttpMethod.Post && path.EndsWith("/microsoft/onboarding", StringComparison.Ordinal)
                ? """{"sessionHandle":"session-handle","authorizationUrl":"https://login.microsoftonline.com/authorize","expiresUtc":"2026-09-21T12:00:00Z"}"""
                : path.EndsWith("/source-kinds", StringComparison.Ordinal)
                    ? """[{"kind":"sharepoint_library","displayName":"SharePoint","description":"Company source","isAvailable":true}]"""
                    : path.EndsWith("/sites", StringComparison.Ordinal)
                        ? """{"items":[{"selectionHandle":"site-handle","kind":"sharepoint_site","displayName":"Finance"}],"isTruncated":false}"""
                        : path.EndsWith("/libraries", StringComparison.Ordinal)
                            ? """{"items":[{"selectionHandle":"source-handle","kind":"sharepoint_library","displayName":"Documents"}],"isTruncated":false}"""
                            : path.EndsWith("/folders", StringComparison.Ordinal)
                                ? """{"source":{"selectionHandle":"source-handle","kind":"sharepoint_library","displayName":"Documents"},"breadcrumbs":[],"items":[{"selectionHandle":"folder-handle","displayName":"Policies"}],"isTruncated":false}"""
                                : path.EndsWith("/selection", StringComparison.Ordinal)
                                    ? """{"sourceKind":"sharepoint_library","sourceDisplayName":"Documents","rootDisplayName":"Policies","canAssignSelectedApplicationPermission":true,"applicationPermission":"Sites.Selected","selectionVersion":3}"""
                                    : path.EndsWith("/access", StringComparison.Ordinal)
                                        ? """{"enableWrites":false,"agentIds":[],"draftVersion":4}"""
                                        : path.EndsWith("/review", StringComparison.Ordinal)
                                            ? """{"tenantDisplay":"Acme","sourceKind":"sharepoint_library","sourceName":"Documents","rootName":"Policies","accessMode":"read_only","agents":[],"audience":"company","importBehavior":"Automatic","permissionChanges":["Read selected folder"],"draftVersion":4}"""
                                            : """{"id":"11111111-1111-1111-1111-111111111111","status":"queued","canRetry":false,"canCleanup":false,"attemptCount":0,"createdUtc":"2026-09-21T10:00:00Z","updatedUtc":"2026-09-21T10:00:00Z"}""";
            var noContent = path.EndsWith("/cancel", StringComparison.Ordinal);
            return new HttpResponseMessage(noContent ? HttpStatusCode.NoContent : HttpStatusCode.OK) { Content = new StringContent(noContent ? string.Empty : json, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class StaticHandler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/problem+json") });
    }
}
