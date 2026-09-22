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

    [Fact]
    public void Guided_flow_uses_opaque_session_and_connects_from_server_review()
    {
        var api = new FakeRepositoryClient([]);
        using var context = CreateContext(api);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/settings/document-repositories?companyId={CompanyId:D}&microsoft365Session=session-handle");
        var cut = context.RenderComponent<DocumentRepositoriesSettings>();

        cut.WaitForAssertion(() => Assert.Contains("Choose a source type", cut.Markup));
        Assert.Equal("session-handle", api.LastRequestedSessionHandle);
        cut.FindAll(".m365-choice-grid button").Single(x => x.TextContent.Contains("SharePoint", StringComparison.Ordinal)).Click();
        cut.Find("#sharepoint-search").Input("Finance");
        cut.Find(".m365-search button").Click();
        cut.WaitForElement(".m365-source-list button").Click();
        cut.WaitForElement(".m365-source-list button").Click();
        cut.WaitForElement(".m365-folder-list .btn").Click();
        cut.FindAll(".wizard-actions .btn").Last().Click();
        cut.WaitForElement(".m365-agent-grid input").Change(true);
        cut.FindAll(".wizard-actions .btn").Last().Click();

        cut.WaitForAssertion(() => Assert.Contains("Review and confirm", cut.Markup));
        Assert.DoesNotContain("drive-id", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Published to Virtual Company", cut.Markup);
        cut.Find(".m365-confirm input").Change(true);
        cut.FindAll(".wizard-actions .btn").Last().Click();
        cut.WaitForAssertion(() => Assert.Contains("Repository connected", cut.Markup));
        Assert.Equal(1, api.FinalizeCalls);
        Assert.Equal([AgentHandler.AgentId], api.ReviewAgentIds);
    }

    [Fact]
    public void Opening_guided_setup_without_a_session_does_not_attempt_resume()
    {
        var api = new FakeRepositoryClient([]);
        using var context = CreateContext(api);
        var cut = Render(context);
        cut.FindAll("button").First(x => x.TextContent.Trim() == "Connect Microsoft 365").Click();
        cut.WaitForElement("[data-testid='microsoft-365-wizard']");
        Assert.Null(api.LastRequestedSessionHandle);
        Assert.Empty(cut.FindAll(".m365-alert--error"));
    }

    [Fact]
    public void OneDrive_continue_opens_drive_and_folder_selection_advances_to_access()
    {
        using var context = CreateContext(new FakeRepositoryClient([]));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/settings/document-repositories?companyId={CompanyId:D}&microsoft365Session=session-handle");
        var cut = context.RenderComponent<DocumentRepositoriesSettings>();
        cut.WaitForElement(".m365-choice-grid");
        cut.FindAll(".m365-choice-grid button").Single(x => x.TextContent.Contains("OneDrive", StringComparison.Ordinal)).Click();
        cut.WaitForElement(".m365-source-list");
        var next = cut.Find(".wizard-actions .btn-primary");
        Assert.False(next.HasAttribute("disabled"));
        next.Click();
        cut.WaitForElement(".m365-folder-list .btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Choose repository access", cut.Markup));
    }

    private static TestContext CreateContext(FakeRepositoryClient repositoryClient)
    {
        var context = new TestContext().AddVirtualCompanyWebPresentationServices();
        context.Services.AddSingleton<IDocumentRepositoryApiClient>(repositoryClient);
        context.Services.AddSingleton(new AgentApiClient(new HttpClient(new AgentHandler()) { BaseAddress = new Uri("http://localhost/") }));
        context.Services.AddSingleton(new OnboardingApiClient(new HttpClient { BaseAddress = new Uri("http://localhost/") }));
        return context;
    }

    [Fact]
    public void Resumed_provisioning_refreshes_automatically_and_notifies_parent_on_completion()
    {
        var api = new FakeRepositoryClient([]) { InitialStatus = "provisioning", SimulatePendingProvisioning = true };
        using var context = CreateContext(api);
        var connected = 0;
        var cut = context.RenderComponent<DocumentRepositoryMicrosoftWizard>(parameters => parameters
            .Add(x => x.CompanyId, CompanyId)
            .Add(x => x.SessionHandle, "session-handle")
            .Add(x => x.Connected, () => connected++));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Repository connected", cut.Markup);
            Assert.Equal(1, connected);
        }, TimeSpan.FromSeconds(6));
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
        public int FinalizeCalls { get; private set; }
        public string? LastRequestedSessionHandle { get; private set; }
        public string InitialStatus { get; init; } = "authorized";
        public bool SimulatePendingProvisioning { get; init; }
        private int provisioningReads;
        public IReadOnlyCollection<Guid> ReviewAgentIds { get; private set; } = [];
        public Task<IReadOnlyList<DocumentRepositoryConnectionViewModel>?> ListAsync(Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<Microsoft365OnboardingStartViewModel> BeginMicrosoftOnboardingAsync(Guid companyId, string returnPath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Microsoft365OnboardingStatusViewModel> GetMicrosoftOnboardingStatusAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default)
        {
            LastRequestedSessionHandle = sessionHandle;
            if (sessionHandle != "session-handle") throw new DocumentRepositoryApiException("Not Found", statusCode: HttpStatusCode.NotFound);
            return Task.FromResult(new Microsoft365OnboardingStatusViewModel(sessionHandle, InitialStatus, Guid.NewGuid(), "/settings/document-repositories", null, null, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), DateTime.UtcNow, 1));
        }
        public Task<IReadOnlyList<Microsoft365SourceKindViewModel>> GetMicrosoftSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Microsoft365SourceKindViewModel>>([new("sharepoint_library", "SharePoint", "Company-owned document libraries", true, null), new("onedrive_business", "OneDrive for Business", "An organizational drive", true, null)]);
        public Task<Microsoft365SourcePageViewModel> GetMicrosoftOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365SourcePageViewModel([new("onedrive-handle", "onedrive_business", "OneDrive", "Administrator")], null, false));
        public Task<Microsoft365SourcePageViewModel> SearchMicrosoftSharePointSitesAsync(Guid companyId, string sessionHandle, string query, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365SourcePageViewModel([new("site-handle", "sharepoint_site", "Finance", "Company site")], null, false));
        public Task<Microsoft365SourcePageViewModel> GetMicrosoftSharePointLibrariesAsync(Guid companyId, string sessionHandle, string siteHandle, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365SourcePageViewModel([new("source-handle", "sharepoint_library", "Company Knowledge", "Finance")], null, false));
        public Task<Microsoft365FolderPageViewModel> BrowseMicrosoftFoldersAsync(Guid companyId, string sessionHandle, string sourceHandle, string? folderHandle = null, string? pageHandle = null, int maxItems = 50, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365FolderPageViewModel(new(sourceHandle, "sharepoint_library", "Company Knowledge", "Finance"), [], [new("folder-handle", "Policies", DateTime.UtcNow)], null, false));
        public Task<Microsoft365RepositorySelectionViewModel> SelectMicrosoftRootAsync(Guid companyId, string sessionHandle, string sourceHandle, string folderHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365RepositorySelectionViewModel("sharepoint_library", "Company Knowledge", "Policies", "Finance", true, "Sites.Selected", null, 2));
        public Task CancelMicrosoftOnboardingAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Microsoft365RepositoryAccessDraftViewModel> ConfigureMicrosoftAccessAsync(Guid companyId, string sessionHandle, bool enableWrites, string? outputFolderHandle, IReadOnlyCollection<Guid> agentIds, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) { ReviewAgentIds = agentIds; return Task.FromResult(new Microsoft365RepositoryAccessDraftViewModel(enableWrites, null, [.. agentIds], 3)); }
        public Task<Microsoft365RepositoryReviewViewModel> GetMicrosoftReviewAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult(new Microsoft365RepositoryReviewViewModel("Acme Advisory", "sharepoint_library", "Company Knowledge", "Policies", "read_only", null, ReviewAgentIds.Select(x => new Microsoft365RepositoryReviewAgentViewModel(x, "Nina")).ToList(), "company", "Existing files will be imported automatically", ["Add read access to the selected folder"], 3));
        public Task<Microsoft365RepositoryProvisioningViewModel> FinalizeMicrosoftRepositoryAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) { FinalizeCalls++; return Task.FromResult(Provisioning("connected")); }
        public Task<Microsoft365RepositoryProvisioningViewModel?> GetMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365RepositoryProvisioningViewModel?>(Provisioning(SimulatePendingProvisioning && provisioningReads++ == 0 ? "provisioning" : "connected"));
        public Task<Microsoft365RepositoryProvisioningViewModel> RetryMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult(Provisioning("queued"));
        public Task<Microsoft365RepositoryProvisioningViewModel> CleanupMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) => Task.FromResult(Provisioning("cleaned"));
        private static Microsoft365RepositoryProvisioningViewModel Provisioning(string status) => new(Guid.NewGuid(), status, status == "connected" ? Guid.NewGuid() : null, null, null, false, false, 1, DateTime.UtcNow, DateTime.UtcNow, status == "connected" ? DateTime.UtcNow : null);
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
