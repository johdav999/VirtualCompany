using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Auth;

public static class DevHeaderAuthenticationDefaults
{
    public const string Scheme = "DevHeader";
    public const string SubjectHeader = "X-Dev-Auth-Subject";
    public const string EmailHeader = "X-Dev-Auth-Email";
    public const string DisplayNameHeader = "X-Dev-Auth-DisplayName";
    public const string ProviderHeader = "X-Dev-Auth-Provider";
}

public sealed class DevHeaderAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[DevHeaderAuthenticationDefaults.SubjectHeader].FirstOrDefault()?.Trim();
        var email = Request.Headers[DevHeaderAuthenticationDefaults.EmailHeader].FirstOrDefault()?.Trim();
        var displayName = Request.Headers[DevHeaderAuthenticationDefaults.DisplayNameHeader].FirstOrDefault()?.Trim();
        var provider = Request.Headers[DevHeaderAuthenticationDefaults.ProviderHeader].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        provider = string.IsNullOrWhiteSpace(provider) ? "dev-header" : provider;
        subject = string.IsNullOrWhiteSpace(subject) ? email! : subject;

        var claims = new List<Claim>
        {
            new(CurrentUserClaimTypes.AuthProvider, provider),
            new(CurrentUserClaimTypes.AuthSubject, subject),
            new(ClaimTypes.NameIdentifier, subject)
        };

        if (!string.IsNullOrWhiteSpace(email))
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            claims.Add(new Claim(ClaimTypes.Name, displayName));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal Principal => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var value = Principal.FindFirstValue(CurrentUserClaimTypes.UserId);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }
}

public sealed class ClaimsExternalUserIdentityAccessor : IExternalUserIdentityAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimsExternalUserIdentityAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ExternalUserIdentity? GetCurrentIdentity()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var provider = principal.FindFirstValue(CurrentUserClaimTypes.AuthProvider);
        var subject = principal.FindFirstValue(CurrentUserClaimTypes.AuthSubject)
                      ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        return new ExternalUserIdentity(
            provider,
            subject,
            principal.FindFirstValue(ClaimTypes.Email),
            principal.FindFirstValue(ClaimTypes.Name));
    }
}

public sealed class RequestCompanyContextAccessor : ICompanyContextAccessor
{
    public Guid? CompanyId { get; private set; }

    public void SetCompanyId(Guid? companyId)
    {
        CompanyId = companyId;
    }
}

public sealed class UserClaimsTransformation : IClaimsTransformation
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IExternalUserIdentityAccessor _externalUserIdentityAccessor;

    public UserClaimsTransformation(
        VirtualCompanyDbContext dbContext,
        IExternalUserIdentityAccessor externalUserIdentityAccessor)
    {
        _dbContext = dbContext;
        _externalUserIdentityAccessor = externalUserIdentityAccessor;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (principal.HasClaim(claim => claim.Type == CurrentUserClaimTypes.UserId))
        {
            return principal;
        }

        var externalIdentity = _externalUserIdentityAccessor.GetCurrentIdentity();
        if (externalIdentity is null)
        {
            return principal;
        }

        var email = NormalizeEmail(externalIdentity.Email, externalIdentity.Subject);
        var displayName = string.IsNullOrWhiteSpace(externalIdentity.DisplayName)
            ? email
            : externalIdentity.DisplayName.Trim();

        var user = await _dbContext.Users.SingleOrDefaultAsync(x =>
            x.AuthProvider == externalIdentity.Provider &&
            x.AuthSubject == externalIdentity.Subject);

        if (user is null && !string.IsNullOrWhiteSpace(externalIdentity.Email))
        {
            user = await _dbContext.Users.SingleOrDefaultAsync(x => x.Email == email);
        }

        if (user is null)
        {
            user = new User(Guid.NewGuid(), email, displayName, externalIdentity.Provider, externalIdentity.Subject);
            _dbContext.Users.Add(user);
        }
        else
        {
            user.UpdateIdentity(email, displayName, externalIdentity.Provider, externalIdentity.Subject);
        }

        if (_dbContext.ChangeTracker.HasChanges())
        {
            await _dbContext.SaveChangesAsync();
        }

        var clonedPrincipal = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
        var claimsIdentity = clonedPrincipal.Identities.First();
        claimsIdentity.AddClaim(new Claim(CurrentUserClaimTypes.UserId, user.Id.ToString()));
        return clonedPrincipal;
    }

    private static string NormalizeEmail(string? email, string subject)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim().ToLowerInvariant();
        }

        var fallback = subject.Contains('@')
            ? subject
            : $"{subject.Trim().ToLowerInvariant().Replace(' ', '.') }@local.virtualcompany.test";

        return fallback.ToLowerInvariant();
    }
}