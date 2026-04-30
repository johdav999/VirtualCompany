using Microsoft.AspNetCore.Http;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Controllers;

internal static class MailboxOAuthCallbackRoutes
{
    public const string GmailCallbackPath = "/api/mailbox-connections/gmail/callback";
    public const string Microsoft365CallbackPath = "/api/mailbox-connections/microsoft365/callback";

    public static string GetProviderCallbackPath(MailboxProvider provider) =>
        provider switch
        {
            MailboxProvider.Gmail => GmailCallbackPath,
            MailboxProvider.Microsoft365 => Microsoft365CallbackPath,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported mailbox provider.")
        };

    public static Uri BuildProviderCallbackUri(HttpRequest request, MailboxProvider provider)
    {
        var host = request.Host;
        if (!host.HasValue)
        {
            throw new InvalidOperationException("Cannot build mailbox callback URI because the request host is missing.");
        }

        // OAuth redirect URIs are provider scoped and do not carry tenant identifiers in the callback path.
        var builder = new UriBuilder(request.Scheme, host.Host)
        {
            Path = GetProviderCallbackPath(provider)
        };

        if (host.Port.HasValue)
        {
            builder.Port = host.Port.Value;
        }

        return builder.Uri;
    }
}