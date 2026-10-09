using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using AccountingDimensionCodes = VirtualCompany.Application.Finance.AccountingDimensionCodes;
namespace VirtualCompany.Infrastructure.Finance;

internal static class FinancePlanningDimensionValidator
{
    public static async Task ValidateAsync(VirtualCompanyDbContext db, Guid company, Guid account, Guid? member, DateOnly date, CancellationToken ct)
    {
        var type = await db.AccountingDimensionTypes.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.Code == AccountingDimensionCodes.CostCenter, ct);
        if (type is null) { if (member.HasValue) throw new ArgumentException("Cost centers are not configured."); return; }
        var policy = await db.AccountingDimensionAccountPolicies.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && x.FinanceAccountId == account &&
            x.DimensionTypeId == type.Id && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (policy?.Requirement == AccountingDimensionRequirementValues.Required && member is null || policy?.Requirement == AccountingDimensionRequirementValues.Prohibited && member.HasValue)
            throw new ArgumentException("The account's effective cost-center requirement is not satisfied.");
        if (member.HasValue && !await db.AccountingDimensionMembers.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company && x.Id == member && x.DimensionTypeId == type.Id &&
            x.Status == AccountingDimensionStatusValues.Active && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date), ct))
            throw new ArgumentException("The cost center is inactive, outside its effective dates or belongs to another company.");
    }
}
