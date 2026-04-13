using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Tenancy;

public sealed class CompanyExecutionScopeFactory : ICompanyExecutionScopeFactory
{
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public CompanyExecutionScopeFactory(ICompanyContextAccessor companyContextAccessor)
    {
        _companyContextAccessor = companyContextAccessor;
    }

    public IDisposable BeginScope(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new InvalidOperationException("Tenant-scoped background execution requires a non-empty CompanyId.");
        }

        var previousCompanyId = _companyContextAccessor.CompanyId;
        var previousMembership = _companyContextAccessor.Membership;

        if (previousCompanyId.HasValue && previousCompanyId.Value != companyId)
        {
            throw new InvalidOperationException(
                $"Cannot enter tenant background execution scope for company '{companyId}' while company '{previousCompanyId.Value}' is active.");
        }

        _companyContextAccessor.SetCompanyId(companyId);
        return new CompanyExecutionScope(_companyContextAccessor, previousCompanyId, previousMembership);
    }

    private sealed class CompanyExecutionScope : IDisposable
    {
        private readonly ICompanyContextAccessor _companyContextAccessor;
        private readonly Guid? _previousCompanyId;
        private readonly ResolvedCompanyMembershipContext? _previousMembership;
        private bool _disposed;

        public CompanyExecutionScope(
            ICompanyContextAccessor companyContextAccessor,
            Guid? previousCompanyId,
            ResolvedCompanyMembershipContext? previousMembership)
        {
            _companyContextAccessor = companyContextAccessor;
            _previousCompanyId = previousCompanyId;
            _previousMembership = previousMembership;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _companyContextAccessor.SetCompanyId(_previousCompanyId);
            _companyContextAccessor.SetCompanyContext(_previousMembership);
        }
    }
}