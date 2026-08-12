using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingChannelOAuthOptions
{
    public const string SectionName = "Marketing:ChannelOAuth";
    public bool Enabled { get; set; }
    public MarketingOAuthProviderOptions LinkedIn { get; set; } = new();
    public MarketingOAuthProviderOptions Meta { get; set; } = new();
    public MarketingOAuthProviderOptions X { get; set; } = new();
}

public sealed class MarketingOAuthProviderOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecretReference { get; set; } = string.Empty;
    public string AccessTier { get; set; } = "unconfigured";
    public string[] AllowedRedirectUris { get; set; } = [];
}

public sealed class DataProtectionMarketingChannelOAuthStateProtector : IMarketingChannelOAuthStateProtector
{
    private readonly IDataProtector protector;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public DataProtectionMarketingChannelOAuthStateProtector(IDataProtectionProvider provider) =>
        protector = provider.CreateProtector("VirtualCompany.MarketingChannelOAuthState.v1");
    public string Protect(MarketingChannelOAuthState state) => protector.Protect(JsonSerializer.Serialize(state, JsonOptions));
    public MarketingChannelOAuthState Unprotect(string protectedState)
    {
        try
        {
            var state = JsonSerializer.Deserialize<MarketingChannelOAuthState>(protector.Unprotect(protectedState), JsonOptions)
                ?? throw new InvalidOperationException("Marketing authorization state was invalid.");
            if (state.SessionId == Guid.Empty || state.CompanyId == Guid.Empty || state.UserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(state.Provider) || string.IsNullOrWhiteSpace(state.RedirectUri) ||
                string.IsNullOrWhiteSpace(state.CodeVerifier)) throw new InvalidOperationException("Marketing authorization state was invalid.");
            return state;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        { throw new InvalidOperationException("Marketing authorization state was invalid.", ex); }
    }
}

public sealed record MarketingOAuthToken(string AccessToken, string? RefreshToken, int? ExpiresInSeconds, string Scope);
public sealed record MarketingOAuthIdentity(string Reference, string DisplayName);
public sealed record MarketingDiscoveredDestination(string Reference, string DisplayName, string Type,
    string CapabilitiesJson, string? AccessToken = null);

public interface IMarketingChannelOAuthAdapter
{
    string Provider { get; }
    Uri BuildAuthorizationUri(MarketingOAuthProviderOptions options, string redirectUri, string protectedState, string codeChallenge);
    Task<MarketingOAuthToken> ExchangeAsync(MarketingOAuthProviderOptions options, string code, string redirectUri,
        string codeVerifier, CancellationToken ct);
    Task<MarketingOAuthToken?> RefreshAsync(MarketingOAuthProviderOptions options, string refreshToken, CancellationToken ct);
    Task<MarketingOAuthIdentity> GetIdentityAsync(string accessToken, CancellationToken ct);
    Task<IReadOnlyList<MarketingDiscoveredDestination>> DiscoverAsync(string accessToken, CancellationToken ct);
}

internal abstract class MarketingOAuthAdapterBase(string provider, IHttpClientFactory clients,
    IPlatformSecretStore secrets) : IMarketingChannelOAuthAdapter
{
    public string Provider { get; } = provider;
    protected IHttpClientFactory Clients { get; } = clients;
    protected async Task<string> ClientSecretAsync(MarketingOAuthProviderOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ClientSecretReference)) throw new InvalidOperationException($"{Provider} client secret is not configured.");
        return (await secrets.GetAsync(options.ClientSecretReference, null, ct))?.Value
            ?? throw new InvalidOperationException($"{Provider} client secret is unavailable.");
    }
    protected static MarketingOAuthToken Token(JsonElement root)
    {
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(access)) throw new InvalidOperationException("Provider did not return an access token.");
        return new(access, root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds) ? seconds : null,
            root.TryGetProperty("scope", out var s) ? s.GetString() ?? string.Empty : string.Empty);
    }
    protected static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException("Provider authorization was rejected. Reconnect the channel and verify application permissions.");
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Provider authorization failed with status {(int)response.StatusCode}.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
    public abstract Uri BuildAuthorizationUri(MarketingOAuthProviderOptions options, string redirectUri, string protectedState, string codeChallenge);
    public abstract Task<MarketingOAuthToken> ExchangeAsync(MarketingOAuthProviderOptions options, string code, string redirectUri, string codeVerifier, CancellationToken ct);
    public virtual Task<MarketingOAuthToken?> RefreshAsync(MarketingOAuthProviderOptions options,string refreshToken,CancellationToken ct)=>Task.FromResult<MarketingOAuthToken?>(null);
    public abstract Task<MarketingOAuthIdentity> GetIdentityAsync(string accessToken, CancellationToken ct);
    public abstract Task<IReadOnlyList<MarketingDiscoveredDestination>> DiscoverAsync(string accessToken, CancellationToken ct);
    protected static Uri UriWithQuery(string endpoint, IReadOnlyDictionary<string,string> values) =>
        new($"{endpoint}?{string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"))}");
    protected static HttpRequestMessage Bearer(HttpMethod method, string uri, string token)
    { var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
}

internal sealed class LinkedInMarketingOAuthAdapter(IHttpClientFactory clients, IPlatformSecretStore secrets,
    IOptions<MarketingChannelDeliveryOptions> delivery) : MarketingOAuthAdapterBase("linkedin", clients, secrets)
{
    public override Uri BuildAuthorizationUri(MarketingOAuthProviderOptions o, string redirect, string state, string challenge) =>
        UriWithQuery("https://www.linkedin.com/oauth/v2/authorization", new Dictionary<string,string>
        { ["response_type"]="code",["client_id"]=o.ClientId,["redirect_uri"]=redirect,["state"]=state,["scope"]="openid profile rw_organization_admin r_organization_social w_organization_social" });
    public override async Task<MarketingOAuthToken> ExchangeAsync(MarketingOAuthProviderOptions o, string code, string redirect, string verifier, CancellationToken ct)
    {
        using var response = await Clients.CreateClient(nameof(LinkedInMarketingOAuthAdapter)).PostAsync("https://www.linkedin.com/oauth/v2/accessToken",
            new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","authorization_code"},{"code",code},{"redirect_uri",redirect},{"client_id",o.ClientId},{"client_secret",await ClientSecretAsync(o,ct)}}),ct);
        using var json = await ReadSuccessAsync(response,ct); return Token(json.RootElement);
    }
    public override async Task<MarketingOAuthIdentity> GetIdentityAsync(string token, CancellationToken ct)
    {
        using var request=Bearer(HttpMethod.Get,"https://api.linkedin.com/v2/userinfo",token); using var response=await Clients.CreateClient(nameof(LinkedInMarketingOAuthAdapter)).SendAsync(request,ct); using var json=await ReadSuccessAsync(response,ct);
        return new(json.RootElement.GetProperty("sub").GetString()!,json.RootElement.TryGetProperty("name",out var n)?n.GetString()??"LinkedIn member":"LinkedIn member");
    }
    public override async Task<IReadOnlyList<MarketingDiscoveredDestination>> DiscoverAsync(string token, CancellationToken ct)
    {
        using var request=Bearer(HttpMethod.Get,"https://api.linkedin.com/rest/organizationAcls?q=roleAssignee&state=APPROVED",token);
        request.Headers.Add("X-Restli-Protocol-Version","2.0.0"); request.Headers.Add("Linkedin-Version",delivery.Value.LinkedInVersion);
        using var response=await Clients.CreateClient(nameof(LinkedInMarketingOAuthAdapter)).SendAsync(request,ct); using var json=await ReadSuccessAsync(response,ct);
        var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"ADMINISTRATOR","CONTENT_ADMIN","CONTENT_ADMINISTRATOR","DIRECT_SPONSORED_CONTENT_POSTER"};
        return json.RootElement.TryGetProperty("elements",out var elements)?elements.EnumerateArray().Where(x=>x.TryGetProperty("role",out var role)&&allowed.Contains(role.GetString()??"")&&x.TryGetProperty("organization",out _)).Select(x=>x.GetProperty("organization").GetString()!).Distinct().Select(x=>new MarketingDiscoveredDestination(x,x,"linkedin_organization","{\"actions\":[\"publish_post\"],\"scheduling\":false}")).ToArray():[];
    }
}

internal sealed class XMarketingOAuthAdapter(IHttpClientFactory clients, IPlatformSecretStore secrets,
    IOptions<MarketingChannelOAuthOptions> oauth) : MarketingOAuthAdapterBase("x",clients,secrets)
{
    public override Uri BuildAuthorizationUri(MarketingOAuthProviderOptions o,string redirect,string state,string challenge)=>UriWithQuery("https://x.com/i/oauth2/authorize",new Dictionary<string,string>{{"response_type","code"},{"client_id",o.ClientId},{"redirect_uri",redirect},{"scope","tweet.read tweet.write users.read offline.access"},{"state",state},{"code_challenge",challenge},{"code_challenge_method","S256"}});
    public override async Task<MarketingOAuthToken> ExchangeAsync(MarketingOAuthProviderOptions o,string code,string redirect,string verifier,CancellationToken ct)
    { using var response=await SendTokenAsync(o,new Dictionary<string,string>{{"code",code},{"grant_type","authorization_code"},{"client_id",o.ClientId},{"redirect_uri",redirect},{"code_verifier",verifier}},ct);using var json=await ReadSuccessAsync(response,ct);return Token(json.RootElement); }
    public override async Task<MarketingOAuthToken?> RefreshAsync(MarketingOAuthProviderOptions o,string refreshToken,CancellationToken ct)
    {using var response=await SendTokenAsync(o,new Dictionary<string,string>{{"refresh_token",refreshToken},{"grant_type","refresh_token"},{"client_id",o.ClientId}},ct);using var json=await ReadSuccessAsync(response,ct);return Token(json.RootElement);}
    public override async Task<MarketingOAuthIdentity> GetIdentityAsync(string token,CancellationToken ct)
    {using var request=Bearer(HttpMethod.Get,"https://api.x.com/2/users/me?user.fields=id,name,username",token);using var response=await Clients.CreateClient(nameof(XMarketingOAuthAdapter)).SendAsync(request,ct);using var json=await ReadSuccessAsync(response,ct);var data=json.RootElement.GetProperty("data");return new(data.GetProperty("id").GetString()!,"@"+(data.TryGetProperty("username",out var u)?u.GetString():data.GetProperty("name").GetString()));}
    public override async Task<IReadOnlyList<MarketingDiscoveredDestination>> DiscoverAsync(string token,CancellationToken ct)
    {var identity=await GetIdentityAsync(token,ct);var tier=oauth.Value.X.AccessTier.Trim().ToLowerInvariant();var capabilities=JsonSerializer.Serialize(new{actions=new[]{"publish_post"},accessTier=tier,costWarning="X API usage may incur provider charges.",scheduling=false,media=false});return[new MarketingDiscoveredDestination(identity.Reference,identity.DisplayName,"x_account",capabilities)];}
    private async Task<HttpResponseMessage> SendTokenAsync(MarketingOAuthProviderOptions o,Dictionary<string,string> form,CancellationToken ct)
    {var request=new HttpRequestMessage(HttpMethod.Post,"https://api.x.com/2/oauth2/token"){Content=new FormUrlEncodedContent(form)};if(!string.IsNullOrWhiteSpace(o.ClientSecretReference)){var secret=await ClientSecretAsync(o,ct);request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes($"{o.ClientId}:{secret}")));}return await Clients.CreateClient(nameof(XMarketingOAuthAdapter)).SendAsync(request,ct);}
}

internal sealed class MetaMarketingOAuthAdapter(IHttpClientFactory clients,IPlatformSecretStore secrets,
    IOptions<MarketingChannelDeliveryOptions> delivery) : MarketingOAuthAdapterBase("meta",clients,secrets)
{
    public override Uri BuildAuthorizationUri(MarketingOAuthProviderOptions o,string redirect,string state,string challenge)=>UriWithQuery($"https://www.facebook.com/{delivery.Value.MetaGraphVersion}/dialog/oauth",new Dictionary<string,string>{{"client_id",o.ClientId},{"redirect_uri",redirect},{"state",state},{"scope","pages_show_list,pages_read_engagement,pages_manage_posts,instagram_basic,instagram_content_publish"},{"response_type","code"}});
    public override async Task<MarketingOAuthToken> ExchangeAsync(MarketingOAuthProviderOptions o,string code,string redirect,string verifier,CancellationToken ct)
    {var uri=UriWithQuery($"https://graph.facebook.com/{delivery.Value.MetaGraphVersion}/oauth/access_token",new Dictionary<string,string>{{"client_id",o.ClientId},{"client_secret",await ClientSecretAsync(o,ct)},{"redirect_uri",redirect},{"code",code}});using var response=await Clients.CreateClient(nameof(MetaMarketingOAuthAdapter)).GetAsync(uri,ct);using var json=await ReadSuccessAsync(response,ct);return Token(json.RootElement);}
    public override async Task<MarketingOAuthIdentity> GetIdentityAsync(string token,CancellationToken ct)
    {using var request=Bearer(HttpMethod.Get,$"https://graph.facebook.com/{delivery.Value.MetaGraphVersion}/me?fields=id,name",token);using var response=await Clients.CreateClient(nameof(MetaMarketingOAuthAdapter)).SendAsync(request,ct);using var json=await ReadSuccessAsync(response,ct);return new(json.RootElement.GetProperty("id").GetString()!,json.RootElement.GetProperty("name").GetString()!);}
    public override async Task<IReadOnlyList<MarketingDiscoveredDestination>> DiscoverAsync(string token,CancellationToken ct)
    {
        using var request=Bearer(HttpMethod.Get,$"https://graph.facebook.com/{delivery.Value.MetaGraphVersion}/me/accounts?fields=id,name,access_token,tasks,instagram_business_account{{id,username}}",token);using var response=await Clients.CreateClient(nameof(MetaMarketingOAuthAdapter)).SendAsync(request,ct);using var json=await ReadSuccessAsync(response,ct);
        var result=new List<MarketingDiscoveredDestination>();if(!json.RootElement.TryGetProperty("data",out var data))return result;
        foreach(var page in data.EnumerateArray())
        {var tasks=page.TryGetProperty("tasks",out var t)?t.EnumerateArray().Select(x=>x.GetString()).ToHashSet(StringComparer.OrdinalIgnoreCase):[];if(!tasks.Overlaps(new[]{"CREATE_CONTENT","MANAGE","MODERATE"}))continue;var id=page.GetProperty("id").GetString()!;var name=page.GetProperty("name").GetString()!;var pageToken=page.TryGetProperty("access_token",out var pt)?pt.GetString():null;result.Add(new(id,name,"facebook_page","{\"actions\":[\"publish_facebook_post\"],\"scheduling\":false}",pageToken));if(page.TryGetProperty("instagram_business_account",out var ig)&&ig.ValueKind==JsonValueKind.Object){var igId=ig.GetProperty("id").GetString()!;var username=ig.TryGetProperty("username",out var un)?un.GetString():igId;result.Add(new(igId,"@"+username,"instagram_professional","{\"actions\":[\"publish_instagram_media\"],\"scheduling\":false,\"requiresPublicImageUrl\":true}",pageToken));}}
        return result;
    }
}

public sealed class MarketingChannelConnectionService(VirtualCompanyDbContext db,
    IMarketingChannelOAuthStateProtector stateProtector, IPlatformSecretStore secrets,
    IEnumerable<IMarketingChannelOAuthAdapter> adapters, IOptions<MarketingChannelOAuthOptions> options,
    TimeProvider clock) : IMarketingChannelConnectionService
{
    public async Task<MarketingChannelOAuthStartDto> StartOAuthAsync(Guid companyId,Guid userId,StartMarketingChannelOAuthRequest request,CancellationToken ct)
    {
        if(!options.Value.Enabled)throw new InvalidOperationException("Marketing channel authorization is not enabled.");
        var provider=MarketingChannelConnection.NormalizeProvider(request.Provider);var providerOptions=Options(provider);
        if(!providerOptions.Enabled||string.IsNullOrWhiteSpace(providerOptions.ClientId))throw new InvalidOperationException($"{provider} authorization is not configured.");
        if(!Uri.TryCreate(request.RedirectUri,UriKind.Absolute,out var redirect)||redirect.Scheme!=Uri.UriSchemeHttps)throw new ArgumentException("A secure absolute callback URI is required.");
        var normalizedRedirect=redirect.GetComponents(UriComponents.HttpRequestUrl,UriFormat.UriEscaped);
        if(!providerOptions.AllowedRedirectUris.Any(x=>Uri.TryCreate(x,UriKind.Absolute,out var allowed)&&
            string.Equals(allowed.GetComponents(UriComponents.HttpRequestUrl,UriFormat.UriEscaped),normalizedRedirect,StringComparison.Ordinal)))
            throw new InvalidOperationException("The callback URI is not registered for this Marketing provider.");
        var now=clock.GetUtcNow().UtcDateTime;var sessionId=Guid.NewGuid();var verifier=Base64Url(RandomNumberGenerator.GetBytes(48));var expires=now.AddMinutes(10);
        var state=stateProtector.Protect(new(sessionId,companyId,userId,provider,redirect.ToString(),verifier,expires));
        var session=new MarketingChannelOAuthSession(sessionId,companyId,userId,provider,Hash(state),redirect.ToString(),expires);db.MarketingChannelOAuthSessions.Add(session);await db.SaveChangesAsync(ct);
        var challenge=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));return new(provider,Adapter(provider).BuildAuthorizationUri(providerOptions,redirect.ToString(),state,challenge),expires);
    }
    public async Task<MarketingChannelOAuthCompletionDto> CompleteOAuthAsync(CompleteMarketingChannelOAuthRequest request,CancellationToken ct)
    {
        var state=stateProtector.Unprotect(request.State);var now=clock.GetUtcNow().UtcDateTime;if(state.ExpiresUtc<=now)throw new InvalidOperationException("Marketing authorization state has expired.");
        var session=await db.MarketingChannelOAuthSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==state.CompanyId&&x.Id==state.SessionId&&x.StateHash==Hash(request.State),ct)??throw new InvalidOperationException("Marketing authorization session is unavailable.");
        if(session.UserId!=state.UserId||session.Provider!=state.Provider||session.RedirectUri!=state.RedirectUri)throw new InvalidOperationException("Marketing authorization state did not match its session.");
        session.Consume(now);await db.SaveChangesAsync(ct);
        if(!secrets.SupportsWrites)throw new InvalidOperationException("The configured protected secret store cannot persist provider credentials.");
        var adapter=Adapter(state.Provider);var token=await adapter.ExchangeAsync(Options(state.Provider),request.Code,state.RedirectUri,state.CodeVerifier,ct);var identity=await adapter.GetIdentityAsync(token.AccessToken,ct);
        var secretReference=$"companies/{state.CompanyId:N}/marketing/{state.Provider}/{identity.Reference}/access";await secrets.SetAsync(secretReference,token.AccessToken,ct);
        if(!string.IsNullOrWhiteSpace(token.RefreshToken))await secrets.SetAsync($"companies/{state.CompanyId:N}/marketing/{state.Provider}/{identity.Reference}/refresh",token.RefreshToken,ct);
        var discovered=await adapter.DiscoverAsync(token.AccessToken,ct);var capabilities=JsonSerializer.Serialize(new{actions=discovered.SelectMany(x=>ReadActions(x.CapabilitiesJson)).Distinct().ToArray(),oauthScopes=token.Scope,discoveredAtUtc=now,accessTier=Options(state.Provider).AccessTier});
        var connection=await db.MarketingChannelConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==state.CompanyId&&x.Provider==state.Provider&&x.ExternalAccountReference==identity.Reference,ct);
        if(connection is null){connection=new(Guid.NewGuid(),state.CompanyId,state.Provider,identity.Reference,identity.DisplayName,capabilities,secretReference,state.UserId);db.MarketingChannelConnections.Add(connection);}else connection.Reconnect(identity.Reference,identity.DisplayName,capabilities,secretReference);
        await db.SaveChangesAsync(ct);await UpsertDestinationsAsync(connection,discovered,ct);connection.RecordHealth(true,null);await db.SaveChangesAsync(ct);return new(Map(connection),await ListDestinationsAsync(state.CompanyId,connection.Id,ct));
    }
    public async Task<IReadOnlyList<MarketingChannelDestinationDto>> ListDestinationsAsync(Guid companyId,Guid? connectionId,CancellationToken ct)=>await db.MarketingChannelDestinations.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==companyId&&(!connectionId.HasValue||x.MarketingChannelConnectionId==connectionId)).OrderBy(x=>x.DisplayName).Select(x=>new MarketingChannelDestinationDto(x.Id,x.MarketingChannelConnectionId,x.ProviderReference,x.DisplayName,x.DestinationType,x.CapabilitiesJson,x.Status,x.LastDiscoveredUtc)).ToListAsync(ct);
    public async Task<IReadOnlyList<MarketingChannelDestinationDto>> RefreshDestinationsAsync(Guid companyId,Guid connectionId,CancellationToken ct)
    {var connection=await db.MarketingChannelConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==connectionId,ct)??throw new InvalidOperationException("Marketing channel connection is unavailable.");var token=(await secrets.GetAsync(connection.SecretReference,null,ct))?.Value;if(string.IsNullOrWhiteSpace(token)){connection.RecordHealth(false,"Protected provider credential is unavailable.");await db.SaveChangesAsync(ct);throw new InvalidOperationException("Provider authorization must be renewed.");}var adapter=Adapter(connection.Provider);try{await UpsertDestinationsAsync(connection,await adapter.DiscoverAsync(token,ct),ct);connection.RecordHealth(true,null);await db.SaveChangesAsync(ct);}catch(InvalidOperationException){var refreshReference=$"companies/{companyId:N}/marketing/{connection.Provider}/{connection.ExternalAccountReference}/refresh";var refresh=(await secrets.GetAsync(refreshReference,null,ct))?.Value;var renewed=string.IsNullOrWhiteSpace(refresh)?null:await adapter.RefreshAsync(Options(connection.Provider),refresh,ct);if(renewed is null){connection.RecordHealth(false,"Provider authorization or destination discovery failed.");await db.SaveChangesAsync(ct);throw;}await secrets.SetAsync(connection.SecretReference,renewed.AccessToken,ct);if(!string.IsNullOrWhiteSpace(renewed.RefreshToken))await secrets.SetAsync(refreshReference,renewed.RefreshToken,ct);await UpsertDestinationsAsync(connection,await adapter.DiscoverAsync(renewed.AccessToken,ct),ct);connection.RecordHealth(true,null);await db.SaveChangesAsync(ct);}return await ListDestinationsAsync(companyId,connectionId,ct);}
    public async Task<bool> DisconnectAsync(Guid companyId,Guid connectionId,CancellationToken ct){var connection=await db.MarketingChannelConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==connectionId,ct);if(connection is null)return false;connection.Disconnect();var destinations=await db.MarketingChannelDestinations.IgnoreQueryFilters().Where(x=>x.CompanyId==companyId&&x.MarketingChannelConnectionId==connectionId).ToListAsync(ct);foreach(var item in destinations)item.MarkUnavailable();await db.SaveChangesAsync(ct);return true;}
    private async Task UpsertDestinationsAsync(MarketingChannelConnection connection,IReadOnlyList<MarketingDiscoveredDestination> discovered,CancellationToken ct){var existing=await db.MarketingChannelDestinations.IgnoreQueryFilters().Where(x=>x.CompanyId==connection.CompanyId&&x.MarketingChannelConnectionId==connection.Id).ToListAsync(ct);var seen=new HashSet<string>(StringComparer.Ordinal);foreach(var item in discovered){seen.Add(item.Reference);string? destinationSecret=null;if(!string.IsNullOrWhiteSpace(item.AccessToken)){destinationSecret=$"companies/{connection.CompanyId:N}/marketing/{connection.Provider}/{connection.ExternalAccountReference}/destination/{item.Reference}";await secrets.SetAsync(destinationSecret,item.AccessToken,ct);}var current=existing.SingleOrDefault(x=>x.ProviderReference==item.Reference);if(current is null)db.MarketingChannelDestinations.Add(new(Guid.NewGuid(),connection.CompanyId,connection.Id,item.Reference,item.DisplayName,item.Type,item.CapabilitiesJson,destinationSecret));else current.Refresh(item.DisplayName,item.CapabilitiesJson,destinationSecret??current.SecretReference);}foreach(var missing in existing.Where(x=>!seen.Contains(x.ProviderReference)))missing.MarkUnavailable();await db.SaveChangesAsync(ct);}
    private IMarketingChannelOAuthAdapter Adapter(string provider)=>adapters.SingleOrDefault(x=>x.Provider==provider)??throw new InvalidOperationException($"{provider} authorization adapter is unavailable.");
    private MarketingOAuthProviderOptions Options(string provider)=>provider switch{"linkedin"=>options.Value.LinkedIn,"meta"=>options.Value.Meta,"x"=>options.Value.X,_=>throw new ArgumentException("Unsupported provider.")};
    private static string[] ReadActions(string json){using var document=JsonDocument.Parse(json);return document.RootElement.TryGetProperty("actions",out var actions)?actions.EnumerateArray().Select(x=>x.GetString()).Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray():[];}
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Base64Url(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static MarketingChannelConnectionDto Map(MarketingChannelConnection x)=>new(x.Id,x.Provider,x.DisplayName,x.CapabilitiesJson,x.Status,x.HealthStatus,x.FailureSummary,x.LastCheckedUtc);
}
