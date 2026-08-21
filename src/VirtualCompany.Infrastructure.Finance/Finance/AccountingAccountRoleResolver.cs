using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingAccountRoleResolver : IAccountingAccountRoleResolver
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AccountingAccountRoleResolver(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<AccountingAccountRoleResolutionDto> ResolveRequiredAsync(
        Guid companyId,
        string roleKey,
        CancellationToken cancellationToken) =>
        await ResolveOptionalAsync(companyId, roleKey, cancellationToken)
        ?? throw new AccountingPostingException(
            BankReconciliationReasonCodes.MissingAccountRole,
            $"Accounting setup does not have an account assigned to the {Friendly(roleKey)} role.");

    public async Task<AccountingAccountRoleResolutionDto?> ResolveOptionalAsync(
        Guid companyId,
        string roleKey,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(roleKey)) throw new ArgumentException("Account role is required.", nameof(roleKey));
        var normalized = roleKey.Trim().ToLowerInvariant();

        var assignment = await _dbContext.AccountingConfigurationAccountRoles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RoleKey == normalized)
            .Select(x => new AccountingAccountRoleResolutionDto(
                x.RoleKey,
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name))
            .SingleOrDefaultAsync(cancellationToken);

        if (assignment is not null) return assignment;
        if (normalized == AccountingAccountRoleKeys.Bank)
            return await ResolveOptionalAsync(companyId, AccountingAccountRoleKeys.Cash, cancellationToken);
        return null;
    }

    private static string Friendly(string roleKey) => roleKey.Trim().Replace('_', ' ');
}
