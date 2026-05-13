using System.Net;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxErrorTranslatorTests
{
    private readonly DefaultFortnoxErrorTranslator _translator = new();

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authorization", "Fortnox connection needs attention. Reconnect Fortnox and try again.")]
    [InlineData(HttpStatusCode.Forbidden, "permission", "The connected Fortnox account does not have permission for this data.")]
    [InlineData(HttpStatusCode.BadRequest, "validation", "Fortnox could not process the requested data. Review the record details and try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "upstream_unavailable", "Fortnox is temporarily unavailable. Please try again shortly.")]
    public void Translate_returns_plain_english_safe_messages(HttpStatusCode statusCode, string category, string expected)
    {
        var message = _translator.Translate(new FortnoxErrorTranslationContext(
            statusCode,
            category,
            "200001",
            "Internal Fortnox provider detail"));

        Assert.Equal(expected, message);
        Assert.DoesNotContain("200001", message);
        Assert.DoesNotContain("Internal Fortnox provider detail", message);
    }

    [Fact]
    public void Translate_includes_safe_retry_after_hint_for_rate_limits()
    {
        var message = _translator.Translate(new FortnoxErrorTranslationContext(
            HttpStatusCode.TooManyRequests,
            "rate_limited",
            "429",
            "Rate limit exceeded",
            TimeSpan.FromSeconds(12)));

        Assert.Equal("Fortnox is receiving too many requests. Please try again in about 12 seconds.", message);
    }

    [Theory]
    [InlineData("2000663", "Har inte behörighet för scope.")]
    [InlineData(null, "Not authorized for scope.")]
    public void Translate_explains_scope_permission_errors(string? code, string providerMessage)
    {
        var message = _translator.Translate(new FortnoxErrorTranslationContext(
            HttpStatusCode.BadRequest,
            "validation",
            code,
            providerMessage));

        Assert.Equal("Fortnox did not grant one or more requested permissions. Enable the scopes in the Fortnox Developer Portal, reconnect Fortnox, and try again.", message);
    }
}
