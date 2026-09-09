using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class DemoTenantExternalSideEffectPolicy(VirtualCompanyDbContext dbContext)
    : IDemoTenantExternalSideEffectPolicy
{
    public async Task<DemoExternalSideEffectDecision> EvaluateAsync(
        Guid companyId,
        string actionType,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(actionType)) throw new ArgumentException("ActionType is required.", nameof(actionType));

        var isDemo = await dbContext.Companies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.IsDemoTenant)
            .SingleOrDefaultAsync(cancellationToken);

        return isDemo
            ? new DemoExternalSideEffectDecision(
                false,
                DemoScenarioProblemCodes.ExternalSideEffectBlocked,
                $"External action '{actionType.Trim()}' is blocked because this is a synthetic demo tenant.")
            : new DemoExternalSideEffectDecision(true, "not_demo_tenant", "Normal company side-effect policy applies.");
    }
}

