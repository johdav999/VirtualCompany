using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Application.Auth;

namespace VirtualCompany.Infrastructure.Finance;

/// <summary>
/// Resolves the company's operational finance authority from durable configuration.  Read callers use
/// the returned operational source by default; simulation is a separately selected, non-authoritative view.
/// </summary>
public sealed class FinanceOperatingModeService : IFinanceOperatingModeService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISimulationFeatureGate _simulationFeatureGate;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public FinanceOperatingModeService(
        VirtualCompanyDbContext dbContext,
        ISimulationFeatureGate simulationFeatureGate,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _simulationFeatureGate = simulationFeatureGate;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<FinanceOperatingModeDecisionDto> GetAsync(
        GetFinanceOperatingModeQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(query));
        }

        if (_companyContextAccessor?.CompanyId is Guid activeCompanyId && activeCompanyId != query.CompanyId)
        {
            throw new UnauthorizedAccessException("Finance operating mode is scoped to the active company context.");
        }

        var asOfDate = query.AsOfDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var configuration = await _dbContext.AccountingConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        var period = await _dbContext.AccountingAuthorityPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.EffectiveFrom <= asOfDate &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= asOfDate))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        var authority = period?.Authority ?? configuration?.Authority ?? "not_configured";
        var providerKey = period?.ProviderKey;
        var providerConnected = !string.IsNullOrWhiteSpace(providerKey) && await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.ProviderKey == providerKey &&
                           x.Status == FinanceIntegrationConnectionStatuses.Connected, cancellationToken);
        var simulationActive = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId &&
                           (x.Status == CompanySimulationStatus.Running || x.Status == CompanySimulationStatus.Paused), cancellationToken);
        var simulationFeatureEnabled = _simulationFeatureGate.IsBackendExecutionEnabled();
        var setupReady = configuration?.SetupState == AccountingSetupStateValues.Ready;
        var migrationInProgress = authority == AccountingAuthorityValues.Migration;
        var issues = new List<FinanceOperatingModeIssueDto>();

        if (configuration is null)
        {
            issues.Add(new("accounting_setup_missing", "Accounting setup has not been completed for this company."));
        }
        else if (!setupReady)
        {
            issues.Add(new("accounting_setup_incomplete", "Accounting setup is incomplete. Complete the chart, policy pack, and required account roles before posting."));
        }

        if (migrationInProgress)
        {
            issues.Add(new("accounting_authority_migration", "Accounting authority is being migrated. Posting and combined operational views are paused until reconciliation completes."));
        }
        else if (authority == AccountingAuthorityValues.ExternalProvider && !providerConnected)
        {
            issues.Add(new("accounting_provider_not_connected", "The authoritative accounting provider is not connected. Reconnect it before provider-authoritative work continues."));
        }
        else if (authority is not AccountingAuthorityValues.InternalLedger and not AccountingAuthorityValues.ExternalProvider)
        {
            issues.Add(new("accounting_authority_missing", "No supported accounting authority applies to the requested date."));
        }

        var postingSource = migrationInProgress || !setupReady
            ? "none"
            : authority == AccountingAuthorityValues.InternalLedger
                ? "internal"
                : providerConnected ? "provider" : "none";
        var nextAction = issues.FirstOrDefault()?.Explanation ??
            (authority == AccountingAuthorityValues.InternalLedger
                ? "Operational finance uses the internal ledger for this date."
                : $"Operational finance uses {providerKey} as the connected accounting authority for this date.");

        return new FinanceOperatingModeDecisionDto(
            query.CompanyId,
            asOfDate,
            authority,
            period?.Id,
            providerKey,
            setupReady,
            migrationInProgress,
            providerConnected,
            simulationFeatureEnabled,
            simulationActive,
            FinanceDataSources.Operational,
            postingSource,
            postingSource != "none",
            nextAction,
            issues);
    }
}
