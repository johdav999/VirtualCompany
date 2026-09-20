using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Api.Tests;
using VirtualCompany.Web.Pages;

namespace VirtualCompany.Web.Tests;

public sealed class DocumentRepositorySettingsComponentTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Connected_repository_renders_health_freshness_counts_and_agent_grant()
    {
        var repository = Connection();
        using var context = CreateContext(new FakeRepositoryClient([repository]));
        var cut = Render(context);

        cut.WaitForAssertion(() => Assert.Contains("Connected", cut.Markup));
        Assert.Contains("3", cut.Find(".repository-counts").TextContent);
        Assert.Contains("Nina", cut.Find(".repository-grants").TextContent);
        Assert.Contains("Last synchronized", cut.Markup);
        Assert.Contains("Published to Virtual Company", cut.Markup);
    }

    [Fact]
    public void Partial_failure_exposes_actionable_full_resynchronization()
    {
        var repository = Connection();
        repository.FailedDocumentCount = 2;
        repository.LatestImportStatus = "partially_failed";
        var api = new FakeRepositoryClient([repository]);
        using var context = CreateContext(api);
        var cut = Render(context);

        cut.WaitForElement(".repository-attention button").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, api.FullSynchronizationCalls));
        Assert.Contains("not available to agents", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Update_conflict_is_visible_and_routes_administrator_to_work_approvals()
    {
        var repository = Connection();
        repository.LatestPublicationOperationKind = "update";
        repository.LatestPublicationStatus = "conflict";
        repository.LatestPublicationFailureMessage = "The Microsoft file changed after this proposal was reviewed.";
        using var context = CreateContext(new FakeRepositoryClient([repository]));
        var cut = Render(context);

        var status = cut.WaitForElement("[data-testid='document-update-status']");
        Assert.Contains("Replacement stopped", status.TextContent);
        Assert.Contains("Microsoft file changed", status.TextContent);
        Assert.Equal($"/approvals?companyId={CompanyId:D}", status.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("A replacement was stopped safely", cut.Find(".repository-attention").TextContent);
    }

    [Fact]
    public void Disconnect_requires_confirmation_and_refreshes_to_disconnected_state()
    {
        var repository = Connection();
        var api = new FakeRepositoryClient([repository]);
        using var context = CreateContext(api);
        var cut = Render(context);

        cut.WaitForElement(".repository-card footer .btn-outline-danger").Click();
        Assert.Contains("Remote files and shared Microsoft credentials are not deleted", cut.Find(".repository-confirm").TextContent);
        cut.Find(".repository-confirm .btn-danger").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(1, api.DisconnectCalls);
            Assert.Contains("Disconnected", cut.Markup);
        });
    }

    [Fact]
    public void Forbidden_read_renders_no_management_actions()
    {
        using var context = CreateContext(new FakeRepositoryClient(null));
        var cut = Render(context);
        cut.WaitForAssertion(() => Assert.Contains("Administrator access required", cut.Markup));
        Assert.Empty(cut.FindAll(".repository-card"));
    }

    private static TestContext CreateContext(FakeRepositoryClient repositoryClient)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton<IDocumentRepositoryApiClient>(repositoryClient);
        context.Services.AddSingleton(new AgentApiClient(new HttpClient(new AgentHandler()) { BaseAddress = new Uri("http://localhost/") }));
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }));
        return context;
    }

    private static IRenderedComponent<DocumentRepositoriesSettings> Render(TestContext context)
    {
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/settings/document-repositories?companyId={CompanyId:D}");
        return context.RenderComponent<DocumentRepositoriesSettings>();
    }

    private static DocumentRepositoryConnectionViewModel Connection() => new()
    {
        Id = Guid.NewGuid(), CompanyId = CompanyId, ProviderKind = "sharepoint_library", DirectoryTenantId = Guid.NewGuid(),
        ApplicationClientId = Guid.NewGuid(), DriveId = "drive", RootItemId = "root", DisplayName = "Company Knowledge Hub",
        IsReadOnly = true, LifecycleState = "active", Audience = "company", AgentIds = [AgentHandler.AgentId],
        LastValidatedUtc = DateTime.UtcNow.AddMinutes(-15), LastSynchronizedUtc = DateTime.UtcNow.AddMinutes(-5),
        ConcurrencyVersion = 4, CreatedUtc = DateTime.UtcNow.AddDays(-1), UpdatedUtc = DateTime.UtcNow,
        IndexedDocumentCount = 3, ProcessingDocumentCount = 1
    };

    private sealed class FakeRepositoryClient(IReadOnlyList<DocumentRepositoryConnectionViewModel>? data) : IDocumentRepositoryApiClient
    {
        private IReadOnlyList<DocumentRepositoryConnectionViewModel>? current = data;
        public int FullSynchronizationCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public Task<IReadOnlyList<DocumentRepositoryConnectionViewModel>?> ListAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<DocumentRepositoryConnectionViewModel> SaveAsync(Guid companyId, Guid? connectionId, ConfigureDocumentRepositoryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentRepositoryValidationViewModel> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) => Task.FromResult(new DocumentRepositoryValidationViewModel(true, "succeeded", "Knowledge", "Root", null));
        public Task<DocumentRepositoryBrowseViewModel> BrowseAsync(Guid companyId, Guid connectionId, string? parentItemId, CancellationToken cancellationToken = default) => Task.FromResult(new DocumentRepositoryBrowseViewModel("root", [], false));
        public Task<DocumentRepositoryImportJobViewModel> StartImportAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentRepositorySynchronizationJobViewModel> StartSynchronizationAsync(Guid companyId, Guid connectionId, bool forceFullReconciliation, CancellationToken cancellationToken = default)
        {
            if (forceFullReconciliation) FullSynchronizationCalls++;
            return Task.FromResult(new DocumentRepositorySynchronizationJobViewModel(Guid.NewGuid(), connectionId, "queued", forceFullReconciliation ? "full_reconciliation" : "delta", 0, 0, 0, 0, 0, null, null, null, DateTime.UtcNow, DateTime.UtcNow, null));
        }
        public Task<DocumentRepositoryConnectionViewModel> SetPauseAsync(Guid companyId, Guid connectionId, string scope, bool paused, long expectedConcurrencyVersion, CancellationToken cancellationToken = default)
        {
            var connection = current!.Single(x => x.Id == connectionId);
            DateTime? value = paused ? DateTime.UtcNow : null;
            if (scope == "retrieval") connection.RetrievalPausedUtc = value;
            else if (scope == "synchronization") connection.SynchronizationPausedUtc = value;
            else connection.WritesPausedUtc = value;
            connection.ConcurrencyVersion++;
            return Task.FromResult(connection);
        }
        public Task<DocumentRepositorySynchronizationJobViewModel> RetryFailedItemsAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) =>
            StartSynchronizationAsync(companyId, connectionId, true, cancellationToken);
        public Task ReconcilePublicationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(Guid companyId, Guid connectionId, long expectedConcurrencyVersion, CancellationToken cancellationToken = default)
        {
            DisconnectCalls++;
            var connection = current!.Single(x => x.Id == connectionId);
            connection.LifecycleState = "disconnected";
            return Task.CompletedTask;
        }
    }

    private sealed class AgentHandler : HttpMessageHandler
    {
        public static readonly Guid AgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"[{{\"id\":\"{AgentId}\",\"companyId\":\"{CompanyId}\",\"displayName\":\"Nina\",\"roleName\":\"Operations Manager\",\"department\":\"Operations\",\"status\":\"active\"}}]", Encoding.UTF8, "application/json")
            });
    }
}
