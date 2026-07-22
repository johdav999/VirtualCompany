using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/mailbox-connections")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class MailboxConnectionsController : ControllerBase
{
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly IMailboxConnectionService _mailboxConnectionService;
    private readonly IStandardMailboxConnectionService _standardMailboxConnectionService;
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _mailboxOptions;
    private readonly IWebHostEnvironment _hostEnvironment;

    public MailboxConnectionsController(
        ICompanyContextAccessor companyContextAccessor,
        IMailboxConnectionService mailboxConnectionService,
        IStandardMailboxConnectionService standardMailboxConnectionService,
        IOptionsMonitor<MailboxIntegrationOptions> mailboxOptions,
        IWebHostEnvironment hostEnvironment)
    {
        _companyContextAccessor = companyContextAccessor;
        _mailboxConnectionService = mailboxConnectionService;
        _standardMailboxConnectionService = standardMailboxConnectionService;
        _mailboxOptions = mailboxOptions;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("current")]
    public async Task<ActionResult<MailboxConnectionStatusResult>> CurrentAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var result = await _mailboxConnectionService.GetStatusAsync(new GetMailboxConnectionStatusQuery(companyId, userId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("purposes/{purpose}")]
    public async Task<ActionResult<MailboxConnectionStatusResult>> PurposeStatusAsync(
        Guid companyId,
        string purpose,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var result = await _mailboxConnectionService.GetStatusAsync(
            new GetMailboxConnectionStatusQuery(companyId, userId, ParsePurpose(purpose)),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyList<MailboxScannedMessageSummary>>> MessagesAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var result = await _mailboxConnectionService.GetScannedMessagesAsync(
            new GetMailboxScannedMessagesQuery(companyId, userId, limit <= 0 ? 50 : limit, MailboxPurpose.Finance),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("providers")]
    public ActionResult<MailboxProviderAvailabilityResponse> Providers(Guid companyId)
    {
        var options = _mailboxOptions.CurrentValue;
        return Ok(new MailboxProviderAvailabilityResponse(
            Gmail: ToProviderAvailability("gmail", "Gmail", options.Gmail),
            Microsoft365: ToProviderAvailability("microsoft365", "Microsoft 365", options.Microsoft365),
            HostedEmail: new MailboxProviderAvailability("standard_email", "Hosted email", true, null)));
    }

    [HttpGet("standard/profiles")]
    public ActionResult<IReadOnlyList<StandardMailboxProfileResponse>> StandardProfiles(Guid companyId) =>
        Ok(_standardMailboxConnectionService.ListProfiles().Select(profile => new StandardMailboxProfileResponse(
            profile.ProfileKey,
            profile.DisplayName,
            profile.Region,
            new MailboxEndpointResponse(profile.Imap.Host, profile.Imap.Port, profile.Imap.TlsMode.ToStorageValue()),
            new MailboxEndpointResponse(profile.Smtp.Host, profile.Smtp.Port, profile.Smtp.TlsMode.ToStorageValue()),
            profile.AuthenticationTypes.Select(type => type.ToStorageValue()).ToArray(),
            profile.AllowsEndpointOverride)).ToArray());

    [HttpPost("purposes/{purpose}/standard/test")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<StandardMailboxConnectionResponse>> TestStandardAsync(
        Guid companyId,
        string purpose,
        [FromBody] StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _standardMailboxConnectionService.TestAsync(
            new TestStandardMailboxConnectionCommand(
                companyId,
                ResolveUserId(),
                ParsePurpose(purpose),
                ToStandardInput(request),
                ParseTestTarget(request.TestTarget)),
            cancellationToken);
        return Ok(ToStandardResponse(result));
    }

    [HttpPost("purposes/{purpose}/standard")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<StandardMailboxConnectionResponse>> SaveStandardAsync(
        Guid companyId,
        string purpose,
        [FromBody] StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _standardMailboxConnectionService.SaveAsync(
            new SaveStandardMailboxConnectionCommand(
                companyId,
                ResolveUserId(),
                ParsePurpose(purpose),
                ToStandardInput(request),
                request.SelectedFolderIds),
            cancellationToken);
        return result.IncomingSucceeded && result.SendingSucceeded
            ? Ok(ToStandardResponse(result))
            : Problem(
                title: "The mailbox could not be connected.",
                detail: result.FailureMessage,
                statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    [HttpPost("purposes/{purpose}/standard/oauth/start")]
    [EnableRateLimiting(PlatformRateLimitPolicyNames.Tasks)]
    public async Task<ActionResult<StartMailboxConnectionResponse>> StartStandardOAuthAsync(
        Guid companyId,
        string purpose,
        [FromBody] StandardMailboxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var profile = _standardMailboxConnectionService.ListProfiles()
            .FirstOrDefault(item => string.Equals(item.ProfileKey, request.ProfileKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The selected hosted email profile is not available.", nameof(request));
        if (profile.OAuth is null)
        {
            return Problem(
                title: "OAuth is not available for this provider.",
                detail: "Use an application password or ask an administrator to add a trusted OAuth profile.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var imap = ToEndpoint(request.Imap) ?? profile.Imap;
        var smtp = ToEndpoint(request.Smtp) ?? profile.Smtp;
        var returnUri = BuildReturnUri(request.ReturnUri);
        var callbackUri = MailboxOAuthCallbackRoutes.BuildProviderCallbackUri(Request, MailboxProvider.StandardEmail);
        var result = await _mailboxConnectionService.StartOAuthConnectionAsync(
            new StartMailboxOAuthConnectionCommand(
                companyId,
                ResolveUserId(),
                MailboxProvider.StandardEmail,
                callbackUri,
                returnUri,
                Purpose: ParsePurpose(purpose),
                ProfileKey: profile.ProfileKey,
                EmailAddress: request.EmailAddress,
                Username: request.Username,
                Imap: imap,
                Smtp: smtp),
            cancellationToken);
        return Ok(new StartMailboxConnectionResponse(result.AuthorizationUrl.ToString()));
    }

    [HttpPost("{provider}/start")]
    public async Task<ActionResult<StartMailboxConnectionResponse>> StartAsync(
        Guid companyId,
        string provider,
        [FromBody] StartMailboxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        return await StartCoreAsync(companyId, MailboxPurpose.Finance, provider, request, cancellationToken);
    }

    [HttpPost("purposes/{purpose}/{provider}/start")]
    public Task<ActionResult<StartMailboxConnectionResponse>> StartPurposeAsync(
        Guid companyId,
        string purpose,
        string provider,
        [FromBody] StartMailboxConnectionRequest request,
        CancellationToken cancellationToken) =>
        StartCoreAsync(companyId, ParsePurpose(purpose), provider, request, cancellationToken);

    [HttpPost("purposes/{purpose}/disconnect")]
    public async Task<ActionResult<MailboxConnectionStatusResult>> DisconnectPurposeAsync(
        Guid companyId,
        string purpose,
        CancellationToken cancellationToken)
    {
        var result = await _mailboxConnectionService.DisconnectAsync(
            new DisconnectMailboxConnectionCommand(companyId, ResolveUserId(), ParsePurpose(purpose)),
            cancellationToken);
        return Ok(result);
    }

    private async Task<ActionResult<StartMailboxConnectionResponse>> StartCoreAsync(
        Guid companyId,
        MailboxPurpose purpose,
        string provider,
        StartMailboxConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var parsedProvider = ParseProvider(provider);
        var callbackUri = MailboxOAuthCallbackRoutes.BuildProviderCallbackUri(Request, parsedProvider);
        MailboxOAuthStartResult result;
        try
        {
            result = await _mailboxConnectionService.StartOAuthConnectionAsync(
                new StartMailboxOAuthConnectionCommand(
                    companyId,
                    userId,
                    parsedProvider,
                    callbackUri,
                    BuildReturnUri(request.ReturnUri),
                    request.ConfiguredFolders?.Select(x => new MailboxFolderSelection(x.ProviderFolderId, x.DisplayName)).ToArray(),
                    purpose),
            cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsMailboxProviderConfigurationError(ex))
        {
            return Problem(
                title: "Mailbox provider is not configured.",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(new StartMailboxConnectionResponse(result.AuthorizationUrl.ToString()));
    }

    [HttpPost("scan")]
    public async Task<ActionResult<ManualMailboxScanResult>> ScanCurrentUserMailboxAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var result = await _mailboxConnectionService.TriggerManualScanAsync(
            new TriggerManualMailboxScanCommand(companyId, userId),
            cancellationToken);

        return Accepted(result);
    }

    private Guid ResolveUserId() =>
        _companyContextAccessor.UserId is { } userId && userId != Guid.Empty
            ? userId
            : throw new UnauthorizedAccessException("A resolved user is required.");

    private Uri? BuildReturnUri(string? explicitReturnUri)
    {
        if (string.IsNullOrWhiteSpace(explicitReturnUri))
        {
            return null;
        }

        if (!Uri.TryCreate(explicitReturnUri, UriKind.Absolute, out var returnUri) ||
            returnUri.Scheme is not ("http" or "https") ||
            !IsAllowedReturnHost(returnUri) ||
            !(returnUri.AbsolutePath.StartsWith("/agents/manage", StringComparison.OrdinalIgnoreCase) ||
              returnUri.AbsolutePath.StartsWith("/agents/mailboxes/connect", StringComparison.OrdinalIgnoreCase) ||
              returnUri.AbsolutePath.StartsWith("/finance/mailbox", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Mailbox return URI must be an absolute Agent Management URL.", nameof(explicitReturnUri));
        }

        return returnUri;
    }

    private bool IsAllowedReturnHost(Uri returnUri)
    {
        if (string.Equals(returnUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
            returnUri.Port == (Request.Host.Port ?? GetDefaultPort(returnUri.Scheme)))
        {
            return true;
        }

        return _hostEnvironment.IsDevelopment() &&
            string.Equals(returnUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDefaultPort(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

    private static MailboxProviderAvailability ToProviderAvailability(
        string provider,
        string displayName,
        MailboxIntegrationOptions.OAuthProviderOptions options)
    {
        var isConfigured = !string.IsNullOrWhiteSpace(options.ClientId) && !string.IsNullOrWhiteSpace(options.ClientSecret);
        return new MailboxProviderAvailability(
            provider,
            displayName,
            isConfigured,
            isConfigured ? null : "This mailbox provider is not configured by an administrator yet.");
    }

    private static MailboxProvider ParseProvider(string provider) => MailboxProviderValues.Parse(provider);

    private static StandardMailboxConnectionInput ToStandardInput(StandardMailboxConnectionRequest request) =>
        new(
            request.ProfileKey,
            request.EmailAddress,
            request.Username,
            MailboxAuthenticationTypeValues.Parse(request.AuthenticationType),
            request.Credential,
            ToEndpoint(request.Imap),
            ToEndpoint(request.Smtp));

    private static MailboxEndpointSettings? ToEndpoint(MailboxEndpointRequest? endpoint) => endpoint is null
        ? null
        : new MailboxEndpointSettings(endpoint.Host, endpoint.Port, MailboxTlsModeValues.Parse(endpoint.TlsMode));

    private static StandardMailboxConnectionResponse ToStandardResponse(StandardMailboxConnectionResult result) =>
        new(
            result.ConnectionId,
            result.IncomingSucceeded,
            result.SendingSucceeded,
            result.EmailAddress,
            (int)result.Capabilities,
            result.Folders,
            result.FailureCode,
            result.FailureMessage,
            result.CheckedUtc);

    private static MailboxPurpose ParsePurpose(string purpose) => MailboxPurposeValues.Parse(purpose);

    private static StandardMailboxTestTarget ParseTestTarget(string? target) => target?.Trim().ToLowerInvariant() switch
    {
        null or "" or "both" => StandardMailboxTestTarget.Both,
        "incoming" => StandardMailboxTestTarget.Incoming,
        "sending" => StandardMailboxTestTarget.Sending,
        _ => throw new ArgumentOutOfRangeException(nameof(target), "Choose incoming or sending mailbox testing.")
    };

    private static bool IsMailboxProviderConfigurationError(InvalidOperationException exception) =>
        exception.Message.Contains("mailbox OAuth client settings are not configured", StringComparison.OrdinalIgnoreCase);

    public sealed record StartMailboxConnectionRequest(
        string? ReturnUri,
        IReadOnlyCollection<MailboxFolderSelectionRequest>? ConfiguredFolders);

    public sealed record MailboxProviderAvailabilityResponse(
        MailboxProviderAvailability Gmail,
        MailboxProviderAvailability Microsoft365,
        MailboxProviderAvailability HostedEmail);

    public sealed record MailboxProviderAvailability(
        string Provider,
        string DisplayName,
        bool IsConfigured,
        string? UnavailableReason);

    public sealed record MailboxFolderSelectionRequest(string ProviderFolderId, string? DisplayName);
    public sealed record StartMailboxConnectionResponse(string AuthorizationUrl);
    public sealed record MailboxEndpointRequest(string Host, int Port, string TlsMode);
    public sealed record MailboxEndpointResponse(string Host, int Port, string TlsMode);
    public sealed record StandardMailboxConnectionRequest(
        string ProfileKey,
        string EmailAddress,
        string Username,
        string AuthenticationType,
        string? Credential,
        MailboxEndpointRequest? Imap,
        MailboxEndpointRequest? Smtp,
        IReadOnlyCollection<string>? SelectedFolderIds,
        string? TestTarget = null,
        string? ReturnUri = null);
    public sealed record StandardMailboxProfileResponse(
        string ProfileKey,
        string DisplayName,
        string Region,
        MailboxEndpointResponse Imap,
        MailboxEndpointResponse Smtp,
        IReadOnlyList<string> AuthenticationTypes,
        bool AllowsEndpointOverride);
    public sealed record StandardMailboxConnectionResponse(
        Guid? ConnectionId,
        bool IncomingSucceeded,
        bool SendingSucceeded,
        string EmailAddress,
        int Capabilities,
        IReadOnlyList<MailboxTransportFolder> Folders,
        string? FailureCode,
        string? FailureMessage,
        DateTime CheckedUtc);
}
