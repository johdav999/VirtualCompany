using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

internal interface ITeamsCallbackTokenValidator
{
    Task<Guid> ValidateAsync(string token, CancellationToken cancellationToken);
}

internal sealed class TeamsCallbackTokenValidator : ITeamsCallbackTokenValidator
{
    private readonly IOptions<TeamsPresenterOptions> configured;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> metadata;

    public TeamsCallbackTokenValidator(IOptions<TeamsPresenterOptions> configured)
        : this(configured, CreateMetadataManager(configured.Value.CallbackOpenIdConfigurationUrl))
    {
    }

    internal TeamsCallbackTokenValidator(
        IOptions<TeamsPresenterOptions> configured,
        IConfigurationManager<OpenIdConnectConfiguration> metadata)
    {
        this.configured = configured;
        this.metadata = metadata;
    }

    public async Task<Guid> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        try
        {
            var signingConfiguration = await metadata.GetConfigurationAsync(cancellationToken);
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configured.Value.CallbackIssuer.TrimEnd('/'),
                ValidateAudience = true,
                ValidAudience = configured.Value.BotApplicationId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingConfiguration.SigningKeys,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                NameClaimType = ClaimTypes.NameIdentifier
            };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parameters, out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal) &&
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha384, StringComparison.Ordinal) &&
                !string.Equals(jwt.Header.Alg, SecurityAlgorithms.RsaSha512, StringComparison.Ordinal))
            {
                throw new SecurityTokenValidationException("Unexpected callback signing algorithm.");
            }

            var tenantClaim = principal.FindFirst("tid")?.Value;
            return Guid.TryParse(tenantClaim, out var tenantId) && tenantId != Guid.Empty
                ? tenantId
                : throw new SecurityTokenValidationException("The callback token has no valid tenant claim.");
        }
        catch (Exception exception) when (exception is SecurityTokenException or InvalidOperationException or ArgumentException or IOException)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CallbackUnauthorized,
                "The Teams callback could not be authenticated.",
                retryable: exception is IOException);
        }
    }

    private static IConfigurationManager<OpenIdConnectConfiguration> CreateMetadataManager(string address) =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            address,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
}

internal sealed class TeamsCallbackAuthenticator(
    ITeamsCallbackTokenValidator tokenValidator,
    IOptions<TeamsPresenterOptions> configured,
    VirtualCompanyDbContext db) : ITeamsCallbackAuthenticator
{
    public async Task<TeamsCallbackIdentity> AuthenticateAsync(
        string? authorizationHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader) ||
            !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
            authorizationHeader.Length <= "Bearer ".Length)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.CallbackUnauthorized,
                "The Teams callback could not be authenticated.");
        }

        var tenantId = await tokenValidator.ValidateAsync(authorizationHeader["Bearer ".Length..].Trim(), cancellationToken);
        var botApplicationId = Guid.TryParse(configured.Value.BotApplicationId, out var parsedBotId)
            ? parsedBotId
            : Guid.Empty;
        var registration = await db.TeamsTenantRegistrations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.EntraTenantId == tenantId && item.BotApplicationId == botApplicationId,
                cancellationToken);
        if (registration is null)
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.TenantNotAssociated,
                "The Teams callback is not associated with an enabled company.");
        }

        if (!string.Equals(registration.Status, TeamsTenantRegistrationStates.Ready, StringComparison.Ordinal))
        {
            throw new TeamsIdentityException(
                TeamsIdentityFailureCodes.TenantDisabled,
                "The Teams tenant registration is not enabled.");
        }

        return new TeamsCallbackIdentity(registration.CompanyId, registration.Id, registration.EntraTenantId);
    }
}
