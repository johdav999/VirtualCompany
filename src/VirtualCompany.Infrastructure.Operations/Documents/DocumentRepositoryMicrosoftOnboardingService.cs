using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class Microsoft365DocumentOnboardingOptions
{
    public const string SectionName = "Microsoft365DocumentOnboarding";
    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";
    public string PlatformClientId { get; set; } = string.Empty;
    public string CallbackUri { get; set; } = string.Empty;
    public string CredentialMode { get; set; } = "client_secret_reference";
    public string CredentialReference { get; set; } = string.Empty;
    public string[] DelegatedSetupScopes { get; set; } = ["openid", "profile", "offline_access", "https://graph.microsoft.com/Files.ReadWrite", "https://graph.microsoft.com/Sites.Read.All"];
    public string SelectedApplicationPermission { get; set; } = "Files.SelectedOperations.Selected";
    public string OneDriveSelectedApplicationPermission { get; set; } = "Files.SelectedOperations.Selected";
    public string SharePointSelectedApplicationPermission { get; set; } = "Files.SelectedOperations.Selected";
    public int SessionLifetimeMinutes { get; set; } = 20;
    public int CleanupIntervalMinutes { get; set; } = 5;
    public int ProvisioningPollIntervalSeconds { get; set; } = 5;
    public string[] AllowedReturnPathPrefixes { get; set; } = ["/settings/document-repositories"];
}

internal sealed class Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper(
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<Microsoft365DocumentOnboardingOptions> configured,
    IPlatformSecretStore secrets,
    ILogger<Microsoft365DocumentOnboardingDevelopmentSecretBootstrapper> logger) : IHostedService
{
    internal const string DevelopmentClientSecretKey = "Microsoft365DocumentOnboarding:DevelopmentClientSecret";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) return;

        var options = configured.Value;
        var clientSecret = configuration[DevelopmentClientSecretKey];
        if (string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(options.CredentialReference)) return;
        if (!secrets.SupportsWrites)
            throw new InvalidOperationException("Microsoft 365 onboarding development credentials require a writable platform secret store.");

        var existing = await secrets.GetAsync(options.CredentialReference, null, cancellationToken);
        if (string.Equals(existing?.Value, clientSecret, StringComparison.Ordinal)) return;

        await secrets.SetAsync(options.CredentialReference, clientSecret, cancellationToken);
        logger.LogInformation("Seeded the Microsoft 365 onboarding credential into the encrypted development secret store.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed record Microsoft365OnboardingSetupMaterial(string Nonce, string CodeVerifier, DateTime IssuedUtc);
internal sealed record Microsoft365DelegatedCredential(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresUtc, string GrantedScope);
internal sealed record Microsoft365CanonicalSelection(string ProviderKind, Guid TenantId, string? SiteId, string DriveId, string RootItemId, string SourceDisplayName, string RootDisplayName, string? SourceContext, string? WebUrl, long SelectionVersion);
internal sealed record Microsoft365RepositoryAccessDraft(bool EnableWrites, string? WritableFolderItemId, string? WritableFolderDisplayName, IReadOnlyList<Guid> AgentIds, long DraftVersion);
internal sealed record Microsoft365OnboardingProtectedState(Microsoft365DelegatedCredential Credential, Microsoft365CanonicalSelection? Selection = null, Microsoft365RepositoryAccessDraft? Draft = null);
internal sealed record Microsoft365SelectionHandle(Guid SessionId, string Purpose, string ProviderKind, string? SiteId, string? DriveId, string? RootItemId, string? ItemId, string? DisplayName, string? Context, string? WebUrl, string? Cursor);

internal interface IMicrosoft365OnboardingMaterialProtector
{
    string ProtectSetup(Microsoft365OnboardingSetupMaterial material);
    Microsoft365OnboardingSetupMaterial UnprotectSetup(string value);
    string ProtectCredential(Microsoft365DelegatedCredential credential);
    string ProtectState(Microsoft365OnboardingProtectedState state);
    Microsoft365OnboardingProtectedState UnprotectState(string value);
    string ProtectHandle(Microsoft365SelectionHandle handle);
    Microsoft365SelectionHandle UnprotectHandle(string value);
}

internal sealed class Microsoft365OnboardingMaterialProtector(IDataProtectionProvider provider) : IMicrosoft365OnboardingMaterialProtector
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector setup = provider.CreateProtector("VirtualCompany.DocumentRepository.Microsoft365Onboarding.Setup.v1");
    private readonly IDataProtector credential = provider.CreateProtector("VirtualCompany.DocumentRepository.Microsoft365Onboarding.DelegatedCredential.v1");
    private readonly IDataProtector handle = provider.CreateProtector("VirtualCompany.DocumentRepository.Microsoft365Onboarding.SelectionHandle.v1");
    public string ProtectSetup(Microsoft365OnboardingSetupMaterial material) => setup.Protect(JsonSerializer.Serialize(material, Json));
    public Microsoft365OnboardingSetupMaterial UnprotectSetup(string value)
    {
        try { return JsonSerializer.Deserialize<Microsoft365OnboardingSetupMaterial>(setup.Unprotect(value), Json) ?? throw new CryptographicException(); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException) { throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 setup state was invalid."); }
    }
    public string ProtectCredential(Microsoft365DelegatedCredential value) => ProtectState(new(value));
    public string ProtectState(Microsoft365OnboardingProtectedState value) => credential.Protect(JsonSerializer.Serialize(value, Json));
    public Microsoft365OnboardingProtectedState UnprotectState(string value)
    {
        try { return JsonSerializer.Deserialize<Microsoft365OnboardingProtectedState>(credential.Unprotect(value), Json) ?? throw new CryptographicException(); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException) { throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "Microsoft 365 discovery state was invalid."); }
    }
    public string ProtectHandle(Microsoft365SelectionHandle value) => handle.Protect(JsonSerializer.Serialize(value, Json));
    public Microsoft365SelectionHandle UnprotectHandle(string value)
    {
        try { return JsonSerializer.Deserialize<Microsoft365SelectionHandle>(handle.Unprotect(value), Json) ?? throw new CryptographicException(); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException) { throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidSelection, "The Microsoft 365 selection handle was invalid."); }
    }
    private static DocumentRepositoryOnboardingException Failure(string code, string message) => new(code, message);
}

internal interface IMicrosoft365IdTokenValidator
{
    Task<ClaimsPrincipal> ValidateAsync(Microsoft365DocumentOnboardingOptions options, string token, string nonce, CancellationToken cancellationToken);
}

internal sealed class Microsoft365IdTokenValidator : IMicrosoft365IdTokenValidator
{
    public async Task<ClaimsPrincipal> ValidateAsync(Microsoft365DocumentOnboardingOptions options, string token, string nonce, CancellationToken ct)
    {
        var parsed = new JwtSecurityTokenHandler { MapInboundClaims = false }.ReadJwtToken(token);
        var tenantText = parsed.Claims.FirstOrDefault(x => x.Type == "tid")?.Value;
        if (!Guid.TryParse(tenantText, out var tenant) || tenant == Guid.Empty) throw Failure(DocumentRepositoryOnboardingFailureCodes.NonOrganizationalAccount, "Use an organizational Microsoft 365 account.");
        var metadataAddress = $"{options.AuthorityHost.TrimEnd('/')}/{tenant:D}/v2.0/.well-known/openid-configuration";
        var metadata = await new ConfigurationManager<OpenIdConnectConfiguration>(metadataAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true }).GetConfigurationAsync(ct);
        var expectedIssuer = $"{options.AuthorityHost.TrimEnd('/')}/{tenant:D}/v2.0";
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = expectedIssuer, ValidateAudience = true, ValidAudience = options.PlatformClientId,
            ValidateIssuerSigningKey = true, IssuerSigningKeys = metadata.SigningKeys, RequireSignedTokens = true,
            RequireExpirationTime = true, ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(2)
        }, out var validated);
        if (validated is not JwtSecurityToken jwt || !jwt.Header.Alg.StartsWith("RS", StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(principal.FindFirst("nonce")?.Value ?? string.Empty), Encoding.UTF8.GetBytes(nonce)))
            throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 authorization could not be verified.");
        return principal;
    }
    private static DocumentRepositoryOnboardingException Failure(string code, string message) => new(code, message);
}

internal sealed partial class DocumentRepositoryMicrosoftOnboardingService(
    VirtualCompanyDbContext db, ICompanyContextAccessor companyContext, ICurrentUserAccessor currentUser,
    IPlatformSecretStore secrets, IMicrosoft365OnboardingMaterialProtector protector, IMicrosoft365IdTokenValidator idTokenValidator,
    IHttpClientFactory clients, IDocumentRepositoryMicrosoftSetupAdapter setupAdapter,
    IDocumentRepositoryMicrosoftPermissionAdapter permissionAdapter, IDocumentRepositoryGraphAdapter graphAdapter,
    IOptions<Microsoft365DocumentOnboardingOptions> configured,
    IAuditEventWriter audit, ICorrelationContextAccessor correlation, TimeProvider clock,
    ILogger<DocumentRepositoryMicrosoftOnboardingService> logger) : IDocumentRepositoryMicrosoftOnboardingService
{
    internal const string HttpClientName = "Microsoft365DocumentOnboarding";
    private static readonly Meter Meter = new("VirtualCompany.DocumentRepository.Onboarding", "1.0");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>("document_repository.onboarding.outcomes");
    private static readonly Histogram<double> Ages = Meter.CreateHistogram<double>("document_repository.onboarding.age.seconds");
    private static readonly HashSet<string> AdministratorRoleIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "62e90394-69f5-4237-9190-012177145e10", // Global Administrator
        "e8611ab8-c189-46e8-94e1-60213ab1f814", // Privileged Role Administrator
        "158c047a-c907-4556-b7ef-446551a6b5f7", // Cloud Application Administrator
        "cf1c38e5-3621-4004-a7cb-879624dced7c"  // Application Administrator
    };

    public async Task<Microsoft365OnboardingStartDto> BeginAsync(Guid companyId, BeginMicrosoft365OnboardingCommand command, CancellationToken ct)
    {
        EnsureCompany(companyId);
        var options = ValidateOptions();
        var userId = RequireUser();
        var returnPath = ValidateReturnPath(command?.ReturnPath, options);
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.SessionLifetimeMinutes);
        var handle = RandomHandle(); var state = handle; var nonce = RandomHandle(); var verifier = RandomHandle(64);
        var record = new CompanyDocumentRepositoryOnboardingSession(companyId, userId, Hash(handle), Hash(state),
            protector.ProtectSetup(new(nonce, verifier, now)), returnPath,
            correlation.CorrelationId ?? Guid.NewGuid().ToString("N"), now, expires);
        db.CompanyDocumentRepositoryOnboardingSessions.Add(record);
        await db.SaveChangesAsync(ct);
        await WriteAudit(record, AuditEventActions.DocumentRepositoryOnboardingStarted, AuditEventOutcomes.Started,
            "Started Microsoft 365 administrator authorization.", ct);

        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.PlatformClientId, ["response_type"] = "code", ["redirect_uri"] = options.CallbackUri,
            ["response_mode"] = "query", ["scope"] = SetupScope(options), ["state"] = state,
            ["nonce"] = nonce, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
            ["prompt"] = "consent"
        };
        var authorizationUrl = $"{options.AuthorityHost.TrimEnd('/')}/organizations/oauth2/v2.0/authorize?{Query(query)}";
        TagList startedTags = default; startedTags.Add("outcome", "started"); Outcomes.Add(1, startedTags);
        return new(handle, authorizationUrl, expires);
    }

    public async Task<Microsoft365OnboardingStatusDto> CompleteCallbackAsync(Microsoft365AuthorizationCallbackCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command?.State)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 returned an invalid callback.");
        var now = clock.GetUtcNow().UtcDateTime;
        var stateHash = Hash(command.State);
        var claimed = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters()
            .Where(x => x.StateHash == stateHash && x.Status == DocumentRepositoryOnboardingStatuses.AwaitingAuthorization && x.ExpiresUtc > now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, DocumentRepositoryOnboardingStatuses.Authorizing)
                .SetProperty(x => x.CallbackReceivedUtc, now).SetProperty(x => x.UpdatedUtc, now)
                .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        if (claimed != 1) throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "Microsoft 365 setup expired or this callback was already used.");

        var session = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters().SingleAsync(x => x.StateHash == stateHash, ct);
        try
        {
            await EnsureInitiatorIsStillAdmin(session, ct);
            if (!string.IsNullOrWhiteSpace(command.Error))
            {
                var code = string.Equals(command.Error, "access_denied", StringComparison.OrdinalIgnoreCase)
                    ? DocumentRepositoryOnboardingFailureCodes.ConsentDenied : DocumentRepositoryOnboardingFailureCodes.InvalidCallback;
                throw Failure(code, code == DocumentRepositoryOnboardingFailureCodes.ConsentDenied
                    ? "Microsoft 365 administrator consent was denied." : "Microsoft 365 could not complete authorization.");
            }
            if (string.IsNullOrWhiteSpace(command.Code)) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 did not return an authorization code.");
            var options = ValidateOptions();
            var setup = protector.UnprotectSetup(session.ProtectedSetupMaterial ?? string.Empty);
            var token = await ExchangeCode(options, command.Code, setup.CodeVerifier, ct);
            var identity = await idTokenValidator.ValidateAsync(options, token.IdToken, setup.Nonce, ct);
            if (!AdministratorRoleIds.Overlaps(identity.FindAll("wids").Select(x => x.Value)))
                throw Failure(DocumentRepositoryOnboardingFailureCodes.InsufficientAdministratorAuthority, "Use an organizational Microsoft administrator account allowed to grant application consent.");
            var tenantText = identity.FindFirst("tid")?.Value;
            if (!Guid.TryParse(tenantText, out var tenantId) || tenantId == Guid.Empty)
                throw Failure(DocumentRepositoryOnboardingFailureCodes.NonOrganizationalAccount, "Use an organizational Microsoft 365 account.");
            RequireGrantedScopes(options, token.Scope);
            session.CompleteAuthorization(tenantId, protector.ProtectCredential(new(token.AccessToken, token.RefreshToken,
                now.AddSeconds(Math.Clamp(token.ExpiresIn, 60, 7200)), token.Scope)), now);
            await db.SaveChangesAsync(ct);
            await WriteAudit(session, AuditEventActions.DocumentRepositoryOnboardingTenantAssociated, AuditEventOutcomes.Succeeded,
                "Associated a verified Microsoft tenant with the repository onboarding session.", ct);
            Observe(session, "authorized", now);
            return Map(session, command.State);
        }
        catch (DocumentRepositoryOnboardingException e)
        {
            session.Fail(e.Code, e.SafeMessage, now); await db.SaveChangesAsync(ct);
            await WriteAudit(session, AuditEventActions.DocumentRepositoryOnboardingDenied, AuditEventOutcomes.Denied, e.SafeMessage, ct);
            Observe(session, e.Code, now); return Map(session, command.State);
        }
        catch (Exception e) when (e is SecurityTokenException or HttpRequestException or TaskCanceledException or JsonException or CryptographicException or ArgumentException or InvalidOperationException or IOException)
        {
            logger.LogWarning("Microsoft 365 repository onboarding callback failed with {FailureType}.", e.GetType().Name);
            var failure = Failure(e is TaskCanceledException ? DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable : DocumentRepositoryOnboardingFailureCodes.InvalidCallback,
                "Microsoft 365 authorization could not be verified. Start setup again.", e is HttpRequestException or TaskCanceledException);
            session.Fail(failure.Code, failure.SafeMessage, now); await db.SaveChangesAsync(ct); Observe(session, failure.Code, now); return Map(session, command.State);
        }
    }

    public async Task<Microsoft365OnboardingStatusDto?> GetStatusAsync(Guid companyId, string sessionHandle, CancellationToken ct)
    {
        EnsureCompany(companyId); var userId = RequireUser(); var now = clock.GetUtcNow().UtcDateTime;
        var session = await Find(companyId, userId, sessionHandle, true, ct); if (session is null) return null;
        if (!session.IsTerminal && session.ExpiresUtc <= now) { session.Expire(now); await db.SaveChangesAsync(ct); await WriteAudit(session, AuditEventActions.DocumentRepositoryOnboardingExpired, AuditEventOutcomes.Failed, "Microsoft 365 repository onboarding expired.", ct); Observe(session, "expired", now); }
        return Map(session, sessionHandle);
    }

    public async Task CancelAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken ct)
    {
        EnsureCompany(companyId); var userId = RequireUser(); var session = await Find(companyId, userId, sessionHandle, true, ct) ?? throw new KeyNotFoundException();
        if (expectedConcurrencyVersion <= 0 || session.ConcurrencyVersion != expectedConcurrencyVersion) throw Failure(DocumentRepositoryOnboardingFailureCodes.ExpiredOrReplayedState, "Microsoft 365 setup changed. Reload its status before cancelling.");
        if (await db.CompanyDocumentRepositoryProvisionings.AnyAsync(x => x.OnboardingSessionId == session.Id && x.Status != DocumentRepositoryProvisioningStatuses.Cleaned, ct))
            throw Failure("provisioning_cleanup_required", "Microsoft permission provisioning has started. Use the reviewed retry or cleanup action instead of cancelling.");
        session.Cancel(clock.GetUtcNow().UtcDateTime); await db.SaveChangesAsync(ct);
        await WriteAudit(session, AuditEventActions.DocumentRepositoryOnboardingCancelled, AuditEventOutcomes.Succeeded, "Cancelled Microsoft 365 repository onboarding.", ct); Observe(session, "cancelled", clock.GetUtcNow().UtcDateTime);
    }

    public async Task ExpirePendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var sessions = await db.CompanyDocumentRepositoryOnboardingSessions.IgnoreQueryFilters()
            .Where(x => x.ExpiresUtc <= now && (x.Status == DocumentRepositoryOnboardingStatuses.AwaitingAuthorization || x.Status == DocumentRepositoryOnboardingStatuses.Authorizing || x.Status == DocumentRepositoryOnboardingStatuses.Authorized) && x.ProtectedSetupMaterial != null)
            .Take(100).ToListAsync(ct);
        foreach (var session in sessions) session.Expire(now);
        if (sessions.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var session in sessions)
            {
                await WriteAudit(session, AuditEventActions.DocumentRepositoryOnboardingExpired, AuditEventOutcomes.Failed, "Microsoft 365 repository onboarding expired.", ct);
                Observe(session, "expired", now);
            }
        }
    }

    private async Task EnsureInitiatorIsStillAdmin(CompanyDocumentRepositoryOnboardingSession session, CancellationToken ct)
    {
        if (RequireUser() != session.InitiatingUserId) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 setup belongs to a different user.");
        var allowed = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == session.CompanyId && x.UserId == session.InitiatingUserId && x.Status == CompanyMembershipStatus.Active && (x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin), ct);
        if (!allowed) throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 setup is no longer authorized for this company.");
    }

    private async Task<OAuthTokenResponse> ExchangeCode(Microsoft365DocumentOnboardingOptions options, string code, string verifier, CancellationToken ct)
    {
        var secret = await secrets.GetAsync(options.CredentialReference, null, ct) ?? throw Failure(DocumentRepositoryOnboardingFailureCodes.ConfigurationUnavailable, "Microsoft 365 connection is not configured for this deployment.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.AuthorityHost.TrimEnd('/')}/organizations/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = options.PlatformClientId, ["client_secret"] = secret.Value, ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = options.CallbackUri, ["code_verifier"] = verifier, ["scope"] = SetupScope(options) })
        };
        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw Failure(DocumentRepositoryOnboardingFailureCodes.ProviderThrottled, "Microsoft 365 temporarily throttled authorization. Start again shortly.", true);
        if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized ? DocumentRepositoryOnboardingFailureCodes.InvalidCallback : DocumentRepositoryOnboardingFailureCodes.ProviderUnavailable, "Microsoft 365 could not complete authorization.", (int)response.StatusCode >= 500);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<OAuthTokenResponse>(stream, cancellationToken: ct) is { AccessToken.Length: > 0, IdToken.Length: > 0, RefreshToken.Length: > 0 } value ? value : throw Failure(DocumentRepositoryOnboardingFailureCodes.InvalidCallback, "Microsoft 365 returned an incomplete authorization result.");
    }

    private static void RequireGrantedScopes(Microsoft365DocumentOnboardingOptions options, string granted)
    {
        var values = GrantedScopes(granted);
        var required = options.DelegatedSetupScopes.Select(NormalizeGraphScope).Where(x => !x.Equals("openid", StringComparison.OrdinalIgnoreCase) && !x.Equals("profile", StringComparison.OrdinalIgnoreCase) && !x.Equals("offline_access", StringComparison.OrdinalIgnoreCase));
        if (required.Any(x => !values.Contains(x))) throw Failure(DocumentRepositoryOnboardingFailureCodes.ConsentDenied, "Microsoft 365 administrator consent did not include the required setup permission.");
    }

    private static string NormalizeGraphScope(string scope)
    {
        const string graphPrefix = "https://graph.microsoft.com/";
        scope = scope.Trim();
        return scope.StartsWith(graphPrefix, StringComparison.OrdinalIgnoreCase) ? scope[graphPrefix.Length..] : scope;
    }

    private static string SetupScope(Microsoft365DocumentOnboardingOptions options) =>
        string.Join(' ', options.DelegatedSetupScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private Microsoft365DocumentOnboardingOptions ValidateOptions()
    {
        var value = configured.Value;
        if (!Uri.TryCreate(value.AuthorityHost, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps || !Uri.TryCreate(value.CallbackUri, UriKind.Absolute, out var callback) || (callback.Scheme != Uri.UriSchemeHttps && !callback.IsLoopback) || !Guid.TryParse(value.PlatformClientId, out var clientId) || clientId == Guid.Empty || !string.Equals(value.CredentialMode, "client_secret_reference", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(value.CredentialReference) || value.SessionLifetimeMinutes is < 5 or > 60 || value.ProvisioningPollIntervalSeconds is < 2 or > 60 || value.DelegatedSetupScopes.Length == 0 || value.AllowedReturnPathPrefixes.Length == 0)
            throw Failure(DocumentRepositoryOnboardingFailureCodes.ConfigurationUnavailable, "Microsoft 365 connection is not configured for this deployment.");
        return value;
    }
    private void EnsureCompany(Guid companyId) { if (companyId == Guid.Empty || companyContext.CompanyId != companyId) throw new UnauthorizedAccessException("Microsoft 365 setup is scoped to the active company."); }
    private Guid RequireUser() => currentUser.UserId is { } id && id != Guid.Empty ? id : throw new UnauthorizedAccessException("An authenticated user is required.");
    private static string ValidateReturnPath(string? value, Microsoft365DocumentOnboardingOptions options)
    {
        value = string.IsNullOrWhiteSpace(value) ? options.AllowedReturnPathPrefixes[0] : value.Trim();
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.Contains('\\') || Uri.TryCreate(value, UriKind.Absolute, out _) || !options.AllowedReturnPathPrefixes.Any(x => value.StartsWith(x, StringComparison.Ordinal))) throw new ArgumentException("ReturnPath is not allowed.", nameof(value));
        return value;
    }
    private Task<CompanyDocumentRepositoryOnboardingSession?> Find(Guid companyId, Guid userId, string handle, bool tracking, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(handle)) return Task.FromResult<CompanyDocumentRepositoryOnboardingSession?>(null);
        var query = db.CompanyDocumentRepositoryOnboardingSessions.Where(x => x.CompanyId == companyId && x.InitiatingUserId == userId && x.SessionHandleHash == Hash(handle));
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }
    private async Task WriteAudit(CompanyDocumentRepositoryOnboardingSession session, string action, string outcome, string summary, CancellationToken ct) => await audit.WriteAsync(new(session.CompanyId, AuditActorTypes.User, session.InitiatingUserId, action, AuditTargetTypes.DocumentRepositoryOnboardingSession, session.Id.ToString("D"), outcome, summary, ["microsoft_identity"], new Dictionary<string, string?> { ["status"] = session.Status, ["failureCode"] = session.FailureCode }, session.CorrelationId), ct);
    private static Microsoft365OnboardingStatusDto Map(CompanyDocumentRepositoryOnboardingSession x, string handle) => new(handle, x.Status, x.ProviderTenantId, x.ReturnPath, x.FailureCode, x.FailureSummary, x.CreatedUtc, x.ExpiresUtc, x.CompletedUtc, x.ConcurrencyVersion);
    private static void Observe(CompanyDocumentRepositoryOnboardingSession session, string outcome, DateTime now) { TagList tags = default; tags.Add("outcome", outcome); Outcomes.Add(1, tags); Ages.Record(Math.Max(0, (now - session.CreatedUtc).TotalSeconds), tags); }
    private static string RandomHandle(int bytes = 32) => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(bytes));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
    private static string Query(IReadOnlyDictionary<string, string> values) => string.Join('&', values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    private static DocumentRepositoryOnboardingException Failure(string code, string message, bool retryable = false) => new(code, message, retryable);

    private sealed record OAuthTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("id_token")] string IdToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string Scope);
}

internal sealed class DocumentRepositoryOnboardingExpiryWorker(IServiceScopeFactory scopes, IOptions<Microsoft365DocumentOnboardingOptions> options, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(options.Value.CleanupIntervalMinutes, 1, 60)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDocumentRepositoryMicrosoftOnboardingService>().ExpirePendingAsync(stoppingToken);
        }
    }
}
