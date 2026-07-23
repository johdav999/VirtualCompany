using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Observability;

public sealed class TenantLoggingScopeMiddleware : IMiddleware
{
    private readonly ILogger<TenantLoggingScopeMiddleware> _logger;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public TenantLoggingScopeMiddleware(
        ILogger<TenantLoggingScopeMiddleware> logger,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor)
    {
        _logger = logger;
        _companyContextAccessor = companyContextAccessor;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        using var scope = _logger.BeginScope(new TenantLogScope(_companyContextAccessor, _currentUserAccessor));
        await next(context);
    }

    private sealed class TenantLogScope : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly ICompanyContextAccessor _companyContextAccessor;
        private readonly ICurrentUserAccessor _currentUserAccessor;

        public TenantLogScope(ICompanyContextAccessor companyContextAccessor, ICurrentUserAccessor currentUserAccessor)
        {
            _companyContextAccessor = companyContextAccessor;
            _currentUserAccessor = currentUserAccessor;
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            if (_companyContextAccessor.CompanyId.HasValue)
            {
                yield return new KeyValuePair<string, object?>("CompanyId", _companyContextAccessor.CompanyId);
            }

            var userId = _companyContextAccessor.UserId ?? _currentUserAccessor.UserId;
            if (userId.HasValue)
            {
                yield return new KeyValuePair<string, object?>("UserId", userId);
            }

            if (_companyContextAccessor.Membership is not null)
            {
                yield return new KeyValuePair<string, object?>("CompanyMembershipId", _companyContextAccessor.Membership.MembershipId);
                yield return new KeyValuePair<string, object?>("CompanyMembershipRole", _companyContextAccessor.Membership.MembershipRole.ToString());
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}