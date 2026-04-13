using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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
    private readonly IHostEnvironment _hostEnvironment;

    public DevHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IHostEnvironment hostEnvironment)
        : base(options, logger, encoder)
    {
        _hostEnvironment = hostEnvironment;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers[DevHeaderAuthenticationDefaults.SubjectHeader].FirstOrDefault()?.Trim();
        var email = Request.Headers[DevHeaderAuthenticationDefaults.EmailHeader].FirstOrDefault()?.Trim();
        var displayName = Request.Headers[DevHeaderAuthenticationDefaults.DisplayNameHeader].FirstOrDefault()?.Trim();
        var provider = Request.Headers[DevHeaderAuthenticationDefaults.ProviderHeader].FirstOrDefault()?.Trim();

        if (string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(email))
        {
            if (!_hostEnvironment.IsDevelopment())
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            subject = "alice";
            email = "alice@example.com";
            displayName = "Alice Admin";
            provider = "dev-header";
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

public sealed class ClaimsPrincipalExternalUserIdentityFactory
{
    private const string SubjectClaimType = "sub";
    private const string EmailClaimType = "email";
    private const string NameClaimType = "name";

    public ExternalUserIdentity? Create(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var provider = Normalize(
            principal.FindFirstValue(CurrentUserClaimTypes.AuthProvider)
            ?? principal.Identity.AuthenticationType);

        var subject = Normalize(
            principal.FindFirstValue(CurrentUserClaimTypes.AuthSubject)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(SubjectClaimType));

        if (provider is null || subject is null)
        {
            return null;
        }

        var email = Normalize(
            principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(EmailClaimType));

        var displayName = Normalize(
            principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(NameClaimType));

        return new ExternalUserIdentity(
            new ExternalIdentityKey(provider, subject),
            email,
            displayName);
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ClaimsPrincipalExternalUserIdentityFactory _externalUserIdentityFactory;

    public HttpContextCurrentUserAccessor(
        IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipalExternalUserIdentityFactory externalUserIdentityFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _externalUserIdentityFactory = externalUserIdentityFactory;
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

    public AuthenticatedUserIdentity Current =>
        new(IsAuthenticated, UserId, _externalUserIdentityFactory.Create(Principal));
}

public sealed class ClaimsExternalUserIdentityAccessor : IExternalUserIdentityAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ClaimsPrincipalExternalUserIdentityFactory _externalUserIdentityFactory;
    private readonly IHostEnvironment _hostEnvironment;

    public ClaimsExternalUserIdentityAccessor(
        IHttpContextAccessor httpContextAccessor,
        ClaimsPrincipalExternalUserIdentityFactory externalUserIdentityFactory,
        IHostEnvironment hostEnvironment)
    {
        _httpContextAccessor = httpContextAccessor;
        _externalUserIdentityFactory = externalUserIdentityFactory;
        _hostEnvironment = hostEnvironment;
    }

    public ExternalUserIdentity? GetCurrentIdentity() =>
        _externalUserIdentityFactory.Create(_httpContextAccessor.HttpContext?.User)
        ?? (_hostEnvironment.IsDevelopment()
            ? new ExternalUserIdentity(
                new ExternalIdentityKey("dev-header", "alice"),
                "alice@example.com",
                "Alice Admin")
            : null);
}

public sealed class ExternalUserIdentityResolver : IExternalUserIdentityResolver
{
    private readonly VirtualCompanyDbContext _dbContext;

    public ExternalUserIdentityResolver(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ResolvedUserIdentity> ResolveAsync(ExternalUserIdentity externalIdentity, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(externalIdentity.Email, externalIdentity.Provider, externalIdentity.Subject);
        var displayName = string.IsNullOrWhiteSpace(externalIdentity.DisplayName)
            ? email
            : externalIdentity.DisplayName.Trim();

        var user = await _dbContext.Users.SingleOrDefaultAsync(
            x => x.AuthProvider == externalIdentity.Provider && x.AuthSubject == externalIdentity.Subject,
            cancellationToken);

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
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new ResolvedUserIdentity(
            user.Id,
            user.Email,
            user.DisplayName,
            new ExternalUserIdentity(
                new ExternalIdentityKey(user.AuthProvider, user.AuthSubject),
                user.Email,
                user.DisplayName));
    }

    private static string NormalizeEmail(string? email, string provider, string subject)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Trim().ToLowerInvariant();
        }

        if (subject.Contains('@'))
        {
            return subject.Trim().ToLowerInvariant();
        }

        return $"{NormalizeLocalPart(provider)}.{NormalizeLocalPart(subject)}@local.virtualcompany.test";
    }

    private static string NormalizeLocalPart(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '.');
        }

        return builder.ToString().Trim('.');
    }
}

public sealed class RequestCompanyContextAccessor : ICompanyContextAccessor
{
    public Guid? CompanyId { get; private set; }
    public Guid? UserId { get; private set; }
    public bool IsResolved => Membership is not null;
    public ResolvedCompanyMembershipContext? Membership { get; private set; }

    public void SetCompanyId(Guid? companyId)
    {
        if (companyId == Guid.Empty)
        {
            companyId = null;
        }

        CompanyId = companyId;

        if (!companyId.HasValue || Membership?.CompanyId != companyId.Value)
        {
            Membership = null;
            UserId = null;
        }
    }

    public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
    {
        Membership = companyContext;

        if (companyContext is null)
        {
            UserId = null;
            return;
        }

        CompanyId = companyContext.CompanyId;
        UserId = companyContext.UserId;
    }
}

public sealed class UserClaimsTransformation : IClaimsTransformation
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IExternalUserIdentityAccessor _externalUserIdentityAccessor;
    private readonly IExternalUserIdentityResolver _externalUserIdentityResolver;

    public UserClaimsTransformation(
        VirtualCompanyDbContext dbContext,
        IExternalUserIdentityAccessor externalUserIdentityAccessor,
        IExternalUserIdentityResolver externalUserIdentityResolver)
    {
        _dbContext = dbContext;
        _externalUserIdentityAccessor = externalUserIdentityAccessor;
        _externalUserIdentityResolver = externalUserIdentityResolver;
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

        var resolvedUser = await _externalUserIdentityResolver.ResolveAsync(externalIdentity, CancellationToken.None);
        var clonedPrincipal = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
        var claimsIdentity = clonedPrincipal.Identities.FirstOrDefault();
        if (claimsIdentity is null)
        {
            claimsIdentity = new ClaimsIdentity(principal.Identity?.AuthenticationType);
            clonedPrincipal.AddIdentity(claimsIdentity);
        }

        ReplaceClaim(claimsIdentity, CurrentUserClaimTypes.AuthProvider, resolvedUser.ExternalIdentity.Provider);
        ReplaceClaim(claimsIdentity, CurrentUserClaimTypes.AuthSubject, resolvedUser.ExternalIdentity.Subject);
        ReplaceClaim(claimsIdentity, CurrentUserClaimTypes.UserId, resolvedUser.UserId.ToString());

        return clonedPrincipal;
    }

    private static void ReplaceClaim(ClaimsIdentity identity, string claimType, string claimValue)
    {
        foreach (var existingClaim in identity.FindAll(claimType).ToList())
        {
            identity.RemoveClaim(existingClaim);
        }

        identity.AddClaim(new Claim(claimType, claimValue));
    }
}
