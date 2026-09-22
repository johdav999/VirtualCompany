using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Documents;

internal sealed partial class DocumentRepositoryMicrosoftOnboardingService
{
    public async Task<IReadOnlyList<Microsoft365SourceKindDto>> GetSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        var (_, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, false, ct);
        var scopes = GrantedScopes(state.Credential.GrantedScope);
        return
        [
            new(DocumentRepositoryProviderKinds.OneDriveForBusiness, "OneDrive for Business", "Choose a folder in the administrator's organizational OneDrive.", scopes.Contains("Files.Read") || scopes.Contains("Files.ReadWrite"), scopes.Contains("Files.Read") || scopes.Contains("Files.ReadWrite") ? null : "Microsoft consent does not include Files.Read or Files.ReadWrite."),
            new(DocumentRepositoryProviderKinds.SharePointLibrary, "SharePoint", "Choose a company-owned SharePoint site and document library.", scopes.Contains("Sites.Read.All") || scopes.Contains("Sites.FullControl.All"), scopes.Contains("Sites.Read.All") || scopes.Contains("Sites.FullControl.All") ? null : "Microsoft consent does not include Sites.Read.All.")
        ];
    }

    public async Task<Microsoft365SourcePageDto> GetOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        RequireScope(state, "Files.Read");
        var source = await setupAdapter.GetOneDriveForBusinessAsync(state.Credential.AccessToken, ct);
        var result = source is null ? Array.Empty<Microsoft365SourceDto>() : [MapSource(session, source)];
        await AuditDiscoveryAsync(session, AuditEventActions.DocumentRepositoryOnboardingSourcesDiscovered, source is null ? "unavailable" : "succeeded", DocumentRepositoryProviderKinds.OneDriveForBusiness, ct);
        return new(result, null, false);
    }

    public async Task<Microsoft365SourcePageDto> SearchSharePointSitesAsync(Guid companyId, string sessionHandle, string query, string? pageHandle, int maxItems, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        RequireSharePointScope(state);
        var cursor = ReadPageCursor(session, pageHandle, "sites_page", null, null);
        var page = await setupAdapter.SearchSharePointSitesAsync(state.Credential.AccessToken, query, cursor, maxItems, ct);
        var items = page.Items.Select(site => new Microsoft365SourceDto(Protect(session, new(session.Id, "site", DocumentRepositoryProviderKinds.SharePointLibrary, site.SiteId, null, null, null, site.DisplayName, null, null, null)), "sharepoint_site", site.DisplayName, null)).ToArray();
        var next = page.NextCursor is null ? null : Protect(session, new(session.Id, "sites_page", DocumentRepositoryProviderKinds.SharePointLibrary, null, null, null, null, null, null, null, page.NextCursor));
        await AuditDiscoveryAsync(session, AuditEventActions.DocumentRepositoryOnboardingSitesDiscovered, "succeeded", DocumentRepositoryProviderKinds.SharePointLibrary, ct);
        return new(items, next, next is not null);
    }

    public async Task<Microsoft365SourcePageDto> GetSharePointLibrariesAsync(Guid companyId, string sessionHandle, string siteHandle, string? pageHandle, int maxItems, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        RequireSharePointScope(state);
        var site = ReadHandle(session, siteHandle, "site");
        var cursor = ReadPageCursor(session, pageHandle, "libraries_page", site.SiteId, null);
        var page = await setupAdapter.GetSharePointLibrariesAsync(state.Credential.AccessToken, site.SiteId!, cursor, maxItems, ct);
        var items = page.Items.Select(source => MapSource(session, source)).ToArray();
        var next = page.NextCursor is null ? null : Protect(session, new(session.Id, "libraries_page", DocumentRepositoryProviderKinds.SharePointLibrary, site.SiteId, null, null, null, null, null, null, page.NextCursor));
        await AuditDiscoveryAsync(session, AuditEventActions.DocumentRepositoryOnboardingLibrariesDiscovered, "succeeded", DocumentRepositoryProviderKinds.SharePointLibrary, ct);
        return new(items, next, next is not null);
    }

    public async Task<Microsoft365FolderPageDto> BrowseFoldersAsync(Guid companyId, string sessionHandle, string sourceHandle, string? folderHandle, string? pageHandle, int maxItems, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        var sourceClaim = ReadHandle(session, sourceHandle, "source");
        RequireProviderScope(state, sourceClaim.ProviderKind);
        var source = ToSource(sourceClaim);
        var folderId = source.RootItemId;
        if (!string.IsNullOrWhiteSpace(folderHandle))
        {
            var folder = ReadHandle(session, folderHandle, "folder");
            EnsureSameSource(sourceClaim, folder);
            folderId = folder.ItemId!;
        }
        var cursor = ReadPageCursor(session, pageHandle, "folders_page", source.DriveId, folderId, true);
        var page = await setupAdapter.BrowseFoldersAsync(state.Credential.AccessToken, source, folderId, cursor, maxItems, ct);
        var breadcrumbs = page.Breadcrumbs.Select(x => new Microsoft365FolderBreadcrumbDto(FolderHandle(session, source, x), x.DisplayName)).ToArray();
        var items = page.Items.Select(x => new Microsoft365FolderDto(FolderHandle(session, source, x), x.DisplayName, x.LastModifiedUtc)).ToArray();
        var next = page.NextCursor is null ? null : Protect(session, new(session.Id, "folders_page", source.ProviderKind, source.SiteId, source.DriveId, source.RootItemId, folderId, null, null, null, page.NextCursor));
        await AuditDiscoveryAsync(session, AuditEventActions.DocumentRepositoryOnboardingFoldersBrowsed, "succeeded", source.ProviderKind, ct);
        return new(MapSource(session, source), breadcrumbs, items, next, next is not null);
    }

    public async Task<Microsoft365RepositorySelectionDto> SelectRootAsync(Guid companyId, string sessionHandle, SelectMicrosoft365RepositoryRootCommand command, CancellationToken ct)
    {
        var (session, state) = await RequireDiscoverySessionAsync(companyId, sessionHandle, true, ct);
        if (command.ExpectedConcurrencyVersion <= 0 || session.ConcurrencyVersion != command.ExpectedConcurrencyVersion)
            throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "Microsoft 365 setup changed. Reload before selecting a folder.");
        var sourceClaim = ReadHandle(session, command.SourceHandle, "source");
        var folderClaim = ReadHandle(session, command.FolderHandle, "folder");
        EnsureSameSource(sourceClaim, folderClaim);
        RequireProviderScope(state, sourceClaim.ProviderKind);
        var source = ToSource(sourceClaim);
        var folder = await setupAdapter.ValidateFolderAsync(state.Credential.AccessToken, source, folderClaim.ItemId!, ct);
        var permission = source.ProviderKind == DocumentRepositoryProviderKinds.OneDriveForBusiness
            ? configured.Value.OneDriveSelectedApplicationPermission
            : configured.Value.SharePointSelectedApplicationPermission;
        var assignable = string.Equals(permission, "Files.SelectedOperations.Selected", StringComparison.Ordinal);
        var version = session.ConcurrencyVersion + 1;
        var selection = new Microsoft365CanonicalSelection(source.ProviderKind, session.ProviderTenantId!.Value, source.SiteId, source.DriveId, folder.ItemId, source.DisplayName, folder.DisplayName, source.Context, folder.WebUrl, version);
        session.UpdateProtectedMaterial(protector.ProtectState(state with { Selection = selection, Draft = null }), clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        await AuditDiscoveryAsync(session, AuditEventActions.DocumentRepositoryOnboardingRootSelected, assignable ? "succeeded" : "unavailable", source.ProviderKind, ct);
        return new(source.ProviderKind, source.DisplayName, folder.DisplayName, source.Context, assignable, permission,
            assignable ? null : "The configured selected application permission cannot be assigned to this source type.", session.ConcurrencyVersion);
    }

    private async Task<(CompanyDocumentRepositoryOnboardingSession Session, Microsoft365OnboardingProtectedState State)> RequireDiscoverySessionAsync(Guid companyId, string sessionHandle, bool refresh, CancellationToken ct)
    {
        EnsureCompany(companyId);
        var session = await Find(companyId, RequireUser(), sessionHandle, true, ct) ?? throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Microsoft 365 setup was not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (session.ExpiresUtc <= now) { session.Expire(now); await db.SaveChangesAsync(ct); throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "Microsoft 365 setup expired. Start again to continue."); }
        if (session.Status != DocumentRepositoryOnboardingStatuses.Authorized || session.ProviderTenantId is null || string.IsNullOrWhiteSpace(session.ProtectedSetupMaterial))
            throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Complete Microsoft administrator authorization before choosing a source.");
        await EnsureInitiatorIsStillAdmin(session, ct);
        var state = protector.UnprotectState(session.ProtectedSetupMaterial);
        if (refresh && state.Credential.AccessTokenExpiresUtc <= now.AddMinutes(2))
        {
            state = state with { Credential = await RefreshCredentialAsync(session.ProviderTenantId.Value, state.Credential, ct) };
            session.UpdateProtectedMaterial(protector.ProtectState(state), now);
            await db.SaveChangesAsync(ct);
        }
        return (session, state);
    }

    private async Task<Microsoft365DelegatedCredential> RefreshCredentialAsync(Guid tenantId, Microsoft365DelegatedCredential current, CancellationToken ct)
    {
        var options = ValidateOptions();
        var secret = await secrets.GetAsync(options.CredentialReference, null, ct) ?? throw Failure(DocumentRepositoryOnboardingFailureCodes.ConfigurationUnavailable, "Microsoft 365 connection is not configured for this deployment.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.AuthorityHost.TrimEnd('/')}/{tenantId:D}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = options.PlatformClientId, ["client_secret"] = secret.Value, ["grant_type"] = "refresh_token", ["refresh_token"] = current.RefreshToken, ["scope"] = SetupScope(options) })
        };
        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw Failure(DocumentRepositoryOnboardingFailureCodes.ProviderThrottled, "Microsoft 365 temporarily throttled discovery. Try again shortly.", true);
        if (!response.IsSuccessStatusCode) throw Failure(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator access was lost. Reconnect to continue.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var token = await JsonSerializer.DeserializeAsync<RefreshTokenResponse>(stream, cancellationToken: ct) ?? throw Failure(DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 returned an invalid token response.");
        if (string.IsNullOrWhiteSpace(token.AccessToken)) throw Failure(DocumentRepositoryOnboardingFailureCodes.AccessLost, "Microsoft administrator access was lost. Reconnect to continue.");
        return new(token.AccessToken, string.IsNullOrWhiteSpace(token.RefreshToken) ? current.RefreshToken : token.RefreshToken, clock.GetUtcNow().UtcDateTime.AddSeconds(Math.Clamp(token.ExpiresIn, 60, 7200)), string.IsNullOrWhiteSpace(token.Scope) ? current.GrantedScope : token.Scope);
    }

    private Microsoft365SourceDto MapSource(CompanyDocumentRepositoryOnboardingSession session, GraphSetupSource source) => new(
        Protect(session, new(session.Id, "source", source.ProviderKind, source.SiteId, source.DriveId, source.RootItemId, source.RootItemId, source.DisplayName, source.Context, source.WebUrl, null)), source.ProviderKind, source.DisplayName, source.Context);
    private string FolderHandle(CompanyDocumentRepositoryOnboardingSession session, GraphSetupSource source, GraphSetupFolder folder) => Protect(session, new(session.Id, "folder", source.ProviderKind, source.SiteId, source.DriveId, source.RootItemId, folder.ItemId, folder.DisplayName, source.Context, folder.WebUrl, null));
    private string Protect(CompanyDocumentRepositoryOnboardingSession session, Microsoft365SelectionHandle handle) => protector.ProtectHandle(handle with { SessionId = session.Id });
    private Microsoft365SelectionHandle ReadHandle(CompanyDocumentRepositoryOnboardingSession session, string value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The Microsoft 365 selection handle was invalid.");
        var handle = protector.UnprotectHandle(value);
        if (handle.SessionId != session.Id || !string.Equals(handle.Purpose, purpose, StringComparison.Ordinal)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The Microsoft 365 selection handle does not belong to this setup session.");
        return handle;
    }
    private string? ReadPageCursor(CompanyDocumentRepositoryOnboardingSession session, string? value, string purpose, string? scopeId, string? itemId, bool driveScope = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var handle = ReadHandle(session, value, purpose);
        if (scopeId is not null && !string.Equals(driveScope ? handle.DriveId : handle.SiteId ?? handle.DriveId, scopeId, StringComparison.Ordinal) || itemId is not null && !string.Equals(handle.ItemId, itemId, StringComparison.Ordinal)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The paging handle does not belong to this source.");
        return handle.Cursor;
    }
    private static GraphSetupSource ToSource(Microsoft365SelectionHandle value)
    {
        if (string.IsNullOrWhiteSpace(value.DriveId) || string.IsNullOrWhiteSpace(value.RootItemId) || string.IsNullOrWhiteSpace(value.DisplayName)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The Microsoft 365 source selection was invalid.");
        return new(value.ProviderKind, value.SiteId, value.DriveId, value.RootItemId, value.DisplayName, value.Context, value.WebUrl);
    }
    private static void EnsureSameSource(Microsoft365SelectionHandle source, Microsoft365SelectionHandle child)
    {
        if (!string.Equals(source.ProviderKind, child.ProviderKind, StringComparison.Ordinal) || !string.Equals(source.SiteId, child.SiteId, StringComparison.Ordinal) || !string.Equals(source.DriveId, child.DriveId, StringComparison.Ordinal) || !string.Equals(source.RootItemId, child.RootItemId, StringComparison.Ordinal)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The selected folder does not belong to this source.");
    }
    private static HashSet<string> GrantedScopes(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(NormalizeGraphScope).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static void RequireScope(Microsoft365OnboardingProtectedState state, string scope) { var scopes = GrantedScopes(state.Credential.GrantedScope); if (!scopes.Contains(scope) && !(scope == "Files.Read" && scopes.Contains("Files.ReadWrite"))) throw Failure(DocumentRepositoryOnboardingFailureCodes.DiscoveryAccessDenied, $"Microsoft consent does not include {scope}."); }
    private static void RequireSharePointScope(Microsoft365OnboardingProtectedState state) { var scopes = GrantedScopes(state.Credential.GrantedScope); if (!scopes.Contains("Sites.Read.All") && !scopes.Contains("Sites.FullControl.All")) throw Failure(DocumentRepositoryOnboardingFailureCodes.DiscoveryAccessDenied, "Microsoft consent does not include SharePoint discovery access."); }
    private static void RequireProviderScope(Microsoft365OnboardingProtectedState state, string providerKind) { if (providerKind == DocumentRepositoryProviderKinds.OneDriveForBusiness) RequireScope(state, "Files.Read"); else RequireSharePointScope(state); }
    private async Task AuditDiscoveryAsync(CompanyDocumentRepositoryOnboardingSession session, string action, string outcome, string providerKind, CancellationToken ct) => await audit.WriteAsync(new(session.CompanyId, AuditActorTypes.User, session.InitiatingUserId, action, AuditTargetTypes.DocumentRepositoryOnboardingSession, session.Id.ToString("D"), outcome, "Processed a bounded Microsoft 365 repository discovery request.", ["microsoft_graph"], new Dictionary<string, string?> { ["providerKind"] = providerKind }, session.CorrelationId), ct);

    private sealed record RefreshTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}
