using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/mailbox-connections")]
public sealed class MailboxConnectionCallbacksController : ControllerBase
{
    private readonly IMailboxConnectionService _mailboxConnectionService;
    private readonly IMailboxOAuthStateProtector _stateProtector;
    private readonly TimeProvider _timeProvider;
    private readonly IWebHostEnvironment _hostEnvironment;

    public MailboxConnectionCallbacksController(
        IMailboxConnectionService mailboxConnectionService,
        IMailboxOAuthStateProtector stateProtector,
        TimeProvider timeProvider,
        IWebHostEnvironment hostEnvironment)
    {
        _mailboxConnectionService = mailboxConnectionService;
        _stateProtector = stateProtector;
        _timeProvider = timeProvider;
        _hostEnvironment = hostEnvironment;
    }

    [HttpGet("gmail/callback")]
    public Task<IActionResult> GmailCallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken) =>
        CompleteProviderCallbackAsync(MailboxProvider.Gmail, code, state, error, cancellationToken);

    [HttpGet("microsoft365/callback")]
    public Task<IActionResult> Microsoft365CallbackAsync(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken) =>
        CompleteProviderCallbackAsync(MailboxProvider.Microsoft365, code, state, error, cancellationToken);

    [HttpGet("/api/companies/{companyId:guid}/mailbox-connections/{provider}/callback")]
    public Task<IActionResult> LegacyProviderCallbackAsync(
        Guid companyId,
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!TryParseProvider(provider, out var parsedProvider, out var failure))
        {
            return Task.FromResult(OAuthFailure(failure));
        }

        // Legacy route tenant identity is intentionally ignored; protected OAuth state is authoritative.
        return CompleteProviderCallbackAsync(parsedProvider, code, state, error, cancellationToken);
    }

    private async Task<IActionResult> CompleteProviderCallbackAsync(
        MailboxProvider expectedProvider,
        string? code,
        string? protectedState,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!TryUnprotectState(protectedState, out var state, out var failure))
        {
            return OAuthFailure(failure);
        }

        if (state.Provider != expectedProvider)
        {
            return OAuthFailure("Mailbox OAuth state provider did not match the callback endpoint.");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return RedirectToMailbox("denied", error, state.CompanyId, state.ReturnUri);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return RedirectToMailbox("failed", "mailbox_oauth_callback_missing_code", state.CompanyId, state.ReturnUri);
        }

        var callbackUri = MailboxOAuthCallbackRoutes.BuildProviderCallbackUri(Request, expectedProvider);
        MailboxOAuthCompletionResult result;
        try
        {
            result = await _mailboxConnectionService.CompleteOAuthConnectionAsync(
                new CompleteMailboxOAuthConnectionCommand(protectedState!, code, callbackUri, expectedProvider),
                cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or HttpRequestException)
        {
            return OAuthFailure(ex.Message);
        }

        return RedirectToMailbox("connected", null, result.CompanyId, result.ReturnUri);
    }

    private bool TryUnprotectState(string? protectedState, out MailboxOAuthState state, out string failure)
    {
        state = default!;
        failure = string.Empty;

        if (string.IsNullOrWhiteSpace(protectedState))
        {
            failure = "Mailbox OAuth state is required.";
            return false;
        }

        try
        {
            state = _stateProtector.Unprotect(protectedState);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException or CryptographicException)
        {
            failure = "Mailbox OAuth state was invalid.";
            return false;
        }

        if (state.CompanyId == Guid.Empty || state.UserId == Guid.Empty)
        {
            failure = "Mailbox OAuth state was invalid.";
            return false;
        }

        if (!Enum.IsDefined(state.Provider))
        {
            failure = "Mailbox OAuth state provider was invalid.";
            return false;
        }

        if (state.ExpiresUtc <= _timeProvider.GetUtcNow().UtcDateTime)
        {
            failure = "Mailbox OAuth state has expired.";
            return false;
        }

        return true;
    }

    private IActionResult OAuthFailure(string detail) =>
        Problem(
            title: "Mailbox OAuth authentication failed.",
            detail: detail,
            statusCode: StatusCodes.Status401Unauthorized);

    private static bool TryParseProvider(string provider, out MailboxProvider parsedProvider, out string failure)
    {
        try
        {
            parsedProvider = MailboxProviderValues.Parse(provider);
            failure = string.Empty;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            parsedProvider = default;
            failure = "Mailbox OAuth callback provider was unsupported.";
            return false;
        }
    }

    private IActionResult RedirectToMailbox(string status, string? detail, Guid companyId, Uri? returnUri)
    {
        if (returnUri is not null && IsAllowedReturnUri(returnUri))
        {
            return Redirect(AppendMailboxStatus(returnUri, status, detail).ToString());
        }

        var uri = new UriBuilder(Request.Scheme, Request.Host.Host)
        {
            Path = "/finance/mailbox",
            Query = $"companyId={Uri.EscapeDataString(companyId.ToString("D"))}"
        };

        if (Request.Host.Port.HasValue)
        {
            uri.Port = Request.Host.Port.Value;
        }

        return Redirect(AppendMailboxStatus(uri.Uri, status, detail).ToString());
    }

    private bool IsAllowedReturnUri(Uri returnUri)
    {
        if (returnUri.Scheme is not ("http" or "https") ||
            !returnUri.AbsolutePath.StartsWith("/finance/mailbox", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPort = Request.Host.Port ?? GetDefaultPort(returnUri.Scheme);
        if (string.Equals(returnUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
            returnUri.Port == expectedPort)
        {
            return true;
        }

        return _hostEnvironment.IsDevelopment() &&
            string.Equals(returnUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDefaultPort(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;

    private static Uri AppendMailboxStatus(Uri uri, string status, string? detail)
    {
        var builder = new UriBuilder(uri);
        var separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : $"{builder.Query.TrimStart('?')}&";
        builder.Query = $"{separator}mailboxConnection={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.Query = $"{builder.Query.TrimStart('?')}&mailboxMessage={Uri.EscapeDataString(detail)}";
        }

        return builder.Uri;
    }
}
