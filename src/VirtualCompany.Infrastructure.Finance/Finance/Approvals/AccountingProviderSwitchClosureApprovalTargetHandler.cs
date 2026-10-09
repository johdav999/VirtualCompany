using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchClosureApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public AccountingProviderSwitchClosureApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
}
