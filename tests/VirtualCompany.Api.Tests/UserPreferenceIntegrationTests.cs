using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class UserPreferenceIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("en-GB", "en-GB")]
    [InlineData("SV-se", "sv-SE")]
    public void SupportedCulturePolicy_NormalizesAllowedBcp47Tags(string input, string expected)
    {
        Assert.True(SupportedUserCultures.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("en-US")]
    [InlineData("not-a-culture")]
    public void SupportedCulturePolicy_RejectsUnsupportedValues(string input)
    {
        Assert.False(SupportedUserCultures.TryNormalize(input, out _));
    }

    [Fact]
    public async Task CurrentUser_CanPersistGlobalPreferenceAndAuditChange()
    {
        using var client = CreateAuthenticatedClient("preference-user");

        var update = await client.PutAsJsonAsync("/api/auth/preferences", new
        {
            uiCulture = "sv-SE",
            formattingCulture = "en-GB"
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var preference = await update.Content.ReadFromJsonAsync<UserPreferenceResponse>();
        Assert.Equal("sv-SE", preference!.UiCulture);
        Assert.Equal("en-GB", preference.FormattingCulture);

        var current = await client.GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
        Assert.Equal("sv-SE", current!.User.UiCulture);
        Assert.Equal("en-GB", current.User.FormattingCulture);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var preferenceRow = await db.UserPreferences.AsNoTracking().SingleAsync(x => x.UserId == current.User.Id);
        Assert.Equal("sv-SE", preferenceRow.UiCulture);
        var change = await db.UserPreferenceChanges.AsNoTracking()
            .SingleAsync(x => x.UserId == current.User.Id && x.NewUiCulture == "sv-SE");
        Assert.Equal("sv-SE", change.NewUiCulture);
    }

    [Fact]
    public async Task Preferences_AreOwnedByCurrentUserWithoutCrossUserIdentifier()
    {
        using var first = CreateAuthenticatedClient("first-preference-user");
        using var second = CreateAuthenticatedClient("second-preference-user");
        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync("/api/auth/preferences", new { uiCulture = "sv-SE", formattingCulture = (string?)null })).StatusCode);

        var secondPreference = await second.GetFromJsonAsync<UserPreferenceResponse>("/api/auth/preferences");

        Assert.Equal("en-GB", secondPreference!.UiCulture);
        Assert.Null(secondPreference.FormattingCulture);
    }

    [Fact]
    public async Task UnsupportedCulture_ReturnsStableValidationCodeWithoutPersistence()
    {
        using var client = CreateAuthenticatedClient("invalid-preference-user");

        var response = await client.PutAsJsonAsync("/api/auth/preferences", new { uiCulture = "fr-FR", formattingCulture = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(UserPreferenceErrorCodes.UnsupportedUiCulture, payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnauthenticatedPreferenceRequest_IsRejected()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/auth/preferences", new { uiCulture = "sv-SE" })).StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, $"{subject}@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record UserPreferenceResponse(string UiCulture, string? FormattingCulture, DateTime? UpdatedUtc);
    private sealed record CurrentUserResponse(CurrentUserIdentity User);
    private sealed record CurrentUserIdentity(Guid Id, string? UiCulture, string? FormattingCulture);
}
