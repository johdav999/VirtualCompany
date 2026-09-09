using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Auth;

public static class VirtualCompanyAuthenticationDefaults
{
    public const string Scheme = "VirtualCompany";
    public const string TeamsSsoScheme = "TeamsSso";
}

public static class TeamsSsoAuthenticationRegistration
{
    public static AuthenticationBuilder AddVirtualCompanyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var tenants = configuration.GetSection("TeamsPresenter:AllowedTenantIds").Get<string[]>() ?? [];
        var allowedTenants = tenants
            .Where(value => Guid.TryParse(value, out _))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var audiences = new[]
            {
                configuration["TeamsPresenter:WebApplicationId"],
                configuration["TeamsPresenter:WebApplicationResource"]
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = VirtualCompanyAuthenticationDefaults.Scheme;
                options.DefaultChallengeScheme = VirtualCompanyAuthenticationDefaults.Scheme;
            })
            .AddPolicyScheme(VirtualCompanyAuthenticationDefaults.Scheme, null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                        ? VirtualCompanyAuthenticationDefaults.TeamsSsoScheme
                        : DevHeaderAuthenticationDefaults.Scheme;
            })
            .AddScheme<AuthenticationSchemeOptions, DevHeaderAuthenticationHandler>(
                DevHeaderAuthenticationDefaults.Scheme,
                _ => { })
            .AddJwtBearer(VirtualCompanyAuthenticationDefaults.TeamsSsoScheme, options =>
            {
                options.MapInboundClaims = false;
                options.Authority = allowedTenants.Count == 1
                    ? $"https://login.microsoftonline.com/{allowedTenants.Single()}/v2.0"
                    : "https://login.microsoftonline.com/organizations/v2.0";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidAudiences = audiences,
                    ClockSkew = TimeSpan.FromMinutes(2),
                    NameClaimType = "name"
                };
                options.TokenValidationParameters.IssuerValidator = (issuer, token, _) =>
                {
                    var allowed = allowedTenants.Any(tenantId =>
                        string.Equals(issuer, $"https://login.microsoftonline.com/{tenantId}/v2.0", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(issuer, $"https://sts.windows.net/{tenantId}/", StringComparison.OrdinalIgnoreCase));
                    return allowed ? issuer : throw new SecurityTokenInvalidIssuerException("The Teams token issuer or tenant is invalid.");
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            var subject = context.Principal.FindFirst("oid")?.Value ??
                                          context.Principal.FindFirst("sub")?.Value;
                            if (!string.IsNullOrWhiteSpace(subject))
                            {
                                identity.AddClaim(new Claim(CurrentUserClaimTypes.AuthProvider, "teams-sso"));
                                identity.AddClaim(new Claim(CurrentUserClaimTypes.AuthSubject, subject));
                                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
                            }
                        }
                        return Task.CompletedTask;
                    }
                };
            });
    }
}
