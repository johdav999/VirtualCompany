using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingOperationsReadService : IAccountingOperationsReadService
{
    private readonly IAccountingMigrationService _migrationService;
    private readonly IAccountingReadinessService _readinessService;

    public AccountingOperationsReadService(
        IAccountingMigrationService migrationService,
        IAccountingReadinessService readinessService)
    {
        _migrationService = migrationService;
        _readinessService = readinessService;
    }

    public async Task<AccountingOperationsReadModel> GetAsync(
        GetAccountingOperationsQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(query));
        var migration = await _migrationService.GetLatestAsync(query.CompanyId, cancellationToken);
        var readiness = await _readinessService.EvaluateAsync(query.CompanyId, cancellationToken);
        return new AccountingOperationsReadModel(query.CompanyId, migration, readiness);
    }
}
