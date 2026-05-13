using System.Net;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class DefaultFortnoxErrorTranslator : IFortnoxErrorTranslator
{
    public string Translate(FortnoxErrorTranslationContext context) =>
        IsScopePermissionError(context)
            ? "Fortnox did not grant one or more requested permissions. Enable the scopes in the Fortnox Developer Portal, reconnect Fortnox, and try again."
            : context.Category switch
        {
            "authorization" => "Fortnox connection needs attention. Reconnect Fortnox and try again.",
            "permission" => "The connected Fortnox account does not have permission for this data.",
            "not_found" => "The requested Fortnox data could not be found.",
            "validation" => "Fortnox could not process the requested data. Review the record details and try again.",
            "rate_limited" => BuildRateLimitMessage(context.RetryAfter),
            "upstream_unavailable" => "Fortnox is temporarily unavailable. Please try again shortly.",
            "invalid_response" => "Fortnox returned data in an unexpected format.",
            _ when context.StatusCode == HttpStatusCode.Unauthorized => "Fortnox connection needs attention. Reconnect Fortnox and try again.",
            _ when context.StatusCode == HttpStatusCode.Forbidden => "The connected Fortnox account does not have permission for this data.",
            _ when context.StatusCode == HttpStatusCode.TooManyRequests => BuildRateLimitMessage(context.RetryAfter),
            _ when (int?)context.StatusCode >= 500 => "Fortnox is temporarily unavailable. Please try again shortly.",
            _ => "Fortnox could not complete the request. Please try again."
        };

    private static bool IsScopePermissionError(FortnoxErrorTranslationContext context) =>
        string.Equals(context.FortnoxErrorCode, "2000663", StringComparison.OrdinalIgnoreCase) ||
        context.FortnoxErrorMessage?.Contains("scope", StringComparison.OrdinalIgnoreCase) == true;

    private static string BuildRateLimitMessage(TimeSpan? retryAfter) =>
        retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
            ? $"Fortnox is receiving too many requests. Please try again in about {Math.Ceiling(retryAfter.Value.TotalSeconds)} seconds."
            : "Fortnox is receiving too many requests. Please try again shortly.";
}
