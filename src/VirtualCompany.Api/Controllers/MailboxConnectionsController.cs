using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
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
    private readonly IOptionsMonitor<MailboxIntegrationOptions> _mailboxOptions;
    private readonly IWebHostEnvironment _hostEnvironment;

    public MailboxConnectionsController(
        ICompanyContextAccessor companyContextAccessor,
        IMailboxConnectionService mailboxConnectionService,
        IOptionsMonitor<MailboxIntegrationOptions> mailboxOptions,
        IWebHostEnvironment hostEnvironment)
    {
        _companyContextAccessor = companyContextAccessor;
        _mailboxConnectionService = mailboxConnectionService;
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

    [HttpGet("messages")]
    public async Task<ActionResult<IReadOnlyList<MailboxScannedMessageSummary>>> MessagesAsync(
        Guid companyId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var result = await _mailboxConnectionService.GetScannedMessagesAsync(
            new GetMailboxScannedMessagesQuery(companyId, userId, limit <= 0 ? 50 : limit),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("providers")]
    public ActionResult<MailboxProviderAvailabilityResponse> Providers(Guid companyId)
    {
        var options = _mailboxOptions.CurrentValue;
        return Ok(new MailboxProviderAvailabilityResponse(
            Gmail: ToProviderAvailability("gmail", "Gmail", options.Gmail),
            Microsoft365: ToProviderAvailability("microsoft365", "Microsoft 365", options.Microsoft365)));
    }

    [HttpPost("{provider}/start")]
    public async Task<ActionResult<StartMailboxConnectionResponse>> StartAsync(
        Guid companyId,
        string provider,
        [FromBody] StartMailboxConnectionRequest request,
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
                    request.ConfiguredFolders?.Select(x => new MailboxFolderSelection(x.ProviderFolderId, x.DisplayName)).ToArray()),
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
            !returnUri.AbsolutePath.StartsWith("/finance/mailbox", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Mailbox return URI must be an absolute finance mailbox URL.", nameof(explicitReturnUri));
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

    private static bool IsMailboxProviderConfigurationError(InvalidOperationException exception) =>
        exception.Message.Contains("mailbox OAuth client settings are not configured", StringComparison.OrdinalIgnoreCase);

    public sealed record StartMailboxConnectionRequest(
        string? ReturnUri,
        IReadOnlyCollection<MailboxFolderSelectionRequest>? ConfiguredFolders);

    public sealed record MailboxProviderAvailabilityResponse(
        MailboxProviderAvailability Gmail,
        MailboxProviderAvailability Microsoft365);

    public sealed record MailboxProviderAvailability(
        string Provider,
        string DisplayName,
        bool IsConfigured,
        string? UnavailableReason);

    public sealed record MailboxFolderSelectionRequest(string ProviderFolderId, string? DisplayName);
    public sealed record StartMailboxConnectionResponse(string AuthorizationUrl);
}
