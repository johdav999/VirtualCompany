using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingDecisionApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public OperatingDecisionApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.OperatingDecisions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
}
