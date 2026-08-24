using System.Globalization;
using System.Data;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class CompanyFinanceReadService : IFinanceReadService, IFinancePaymentReadService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int MaxBurnLookbackDays = 3660;
    private const string MissingCounterpartyName = "Unknown counterparty";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ICompanyDocumentService? _documentService;
    private readonly IKnowledgeAccessPolicyEvaluator? _accessPolicyEvaluator;
    private readonly IFinanceSeedingStateService? _financeSeedingStateService;
    private readonly IDistributedCache? _insightSnapshotCache;
    private readonly TimeProvider? _timeProvider;
    private readonly IFinanceInsightPersistenceService _financeInsightPersistenceService;
    private readonly IReadOnlyList<IFinancialCheck> _financialChecks;
    private readonly FinanceRecordSourcePolicy _sourcePolicy;
    public CompanyFinanceReadService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, null, null, null, null, null)
    {
    }

    public CompanyFinanceReadService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
        : this(dbContext, companyContextAccessor, null, null, null, null, null)
    {
    }

    public CompanyFinanceReadService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        ICompanyDocumentService? documentService,
        IKnowledgeAccessPolicyEvaluator? accessPolicyEvaluator,
        IFinanceSeedingStateService? financeSeedingStateService,
        IDistributedCache? insightSnapshotCache,
        TimeProvider? timeProvider)
        : this(
            dbContext,
            companyContextAccessor,
            documentService,
            accessPolicyEvaluator,
            financeSeedingStateService,
            financialChecks: null,
            financeInsightPersistenceService: null,
            insightSnapshotCache,
            timeProvider)
    {
    }

    public CompanyFinanceReadService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        ICompanyDocumentService? documentService,
        IKnowledgeAccessPolicyEvaluator? accessPolicyEvaluator,
        IFinanceSeedingStateService? financeSeedingStateService = null,
        IEnumerable<IFinancialCheck>? financialChecks = null,
        IFinanceInsightPersistenceService? financeInsightPersistenceService = null,
        IDistributedCache? insightSnapshotCache = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _documentService = documentService;
        _accessPolicyEvaluator = accessPolicyEvaluator;
        _financeSeedingStateService = financeSeedingStateService;
        _insightSnapshotCache = insightSnapshotCache;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _sourcePolicy = new FinanceRecordSourcePolicy(_dbContext);
        _financeInsightPersistenceService = financeInsightPersistenceService ??
            new FinanceInsightPersistenceService(
                new FinanceAgentInsightRepository(_dbContext), _timeProvider);
        _financialChecks = financialChecks?.ToArray() ??
            [
                new CashRiskFinancialCheck((context, cancellationToken) =>
                    GetCashPositionAsync(new GetFinanceCashPositionQuery(context.CompanyId, context.AsOfUtc), cancellationToken)),
                new TransactionAnomalyFinancialCheck(_dbContext),
                new OverdueReceivablesFinancialCheck(_dbContext),
                new PayablesFinancialCheck(_dbContext),
                new SupplierBillDueMonitoringFinancialCheck(_dbContext)
            ];
    }

    private async Task EnsureFinanceInitializedAsync(
        Guid companyId,
        CancellationToken cancellationToken,
        string? sourceFilter = null)
    {
        if (FinanceDataSources.Normalize(sourceFilter) == FinanceDataSources.Fortnox)
        {
            return;
        }

        if (_financeSeedingStateService is null)
        {
            return;
        }

        // Native accounting setup is an authoritative finance dataset. It must not be hidden
        // behind the separate simulation-data bootstrap state once setup is ready.
        var hasReadyNativeAccounting = await _dbContext.AccountingConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.SetupState == AccountingSetupStateValues.Ready,
                cancellationToken);
        if (hasReadyNativeAccounting)
        {
            return;
        }

        var state = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(companyId, cancellationToken);
        if (state.State != FinanceSeedingState.Seeded)
        {
            var hasIntegrationFinanceData = await _dbContext.FinanceExternalReferences
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.CompanyId == companyId &&
                    x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                    x.EntityType != "sales_finance_handoff",
                    cancellationToken);
            if (!hasIntegrationFinanceData)
            {
                hasIntegrationFinanceData = await _dbContext.FinanceBills
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(x =>
                        x.CompanyId == companyId &&
                        (EF.Property<string>(x, "SourceType") == FinanceRecordSourceTypes.Fortnox ||
                         EF.Property<string?>(x, "ProviderKey") == FinanceIntegrationProviderKeys.Fortnox),
                        cancellationToken);
            }

            if (hasIntegrationFinanceData)
            {
                return;
            }

            throw new FinanceNotInitializedException(companyId, "Finance data has not been initialized for this company. Generate finance data before requesting finance records.");
        }
    }

    private async Task EnsureFinanceInitializedForRecordAsync(
        Guid companyId,
        string? sourceType,
        string? providerKey,
        bool hasFortnoxReference,
        CancellationToken cancellationToken)
    {
        if (string.Equals(sourceType, FinanceRecordSourceTypes.Fortnox, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) ||
            hasFortnoxReference)
        {
            return;
        }

        await EnsureFinanceInitializedAsync(companyId, cancellationToken);
    }

    private async Task<FiscalPeriodRow> LoadFiscalPeriodAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        if (fiscalPeriodId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal period id is required.", nameof(fiscalPeriodId));
        }

        var period = await _dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == fiscalPeriodId)
            .Select(x => new FiscalPeriodRow(
                x.Id,
                x.CompanyId,
                x.Name,
                x.StartUtc,
                x.EndUtc,
                x.IsClosed))
            .SingleOrDefaultAsync(cancellationToken);

        return period ?? throw new KeyNotFoundException("The requested fiscal period was not found in the active company.");
    }

    private async Task<Dictionary<Guid, StatementMappingRow>> LoadStatementMappingLookupAsync(
        Guid companyId,
        FinancialStatementType statementType,
        CancellationToken cancellationToken) =>
        await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.StatementType == statementType)
            .Select(x => new StatementMappingRow(
                x.FinanceAccountId,
                x.ReportSection,
                x.LineClassification,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.FinanceAccount.OpeningBalance,
                x.FinanceAccount.Currency))
            .ToDictionaryAsync(x => x.FinanceAccountId, cancellationToken);

    private async Task<FinancialStatementSnapshotHeaderRow?> LoadLatestFinancialStatementSnapshotAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        FinancialStatementType statementType,
        CancellationToken cancellationToken) =>
        await _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalPeriodId == fiscalPeriodId &&
                x.StatementType == statementType)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new FinancialStatementSnapshotHeaderRow(
                x.Id,
                x.FiscalPeriodId,
                x.StatementType,
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<List<FinanceStatementLineDto>> LoadFinancialStatementSnapshotLinesAsync(
        Guid snapshotId,
        CancellationToken cancellationToken) =>
        await _dbContext.FinancialStatementSnapshotLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.SnapshotId == snapshotId)
            .OrderBy(x => x.LineOrder)
            .ThenBy(x => x.LineCode)
            .Select(x => new FinanceStatementLineDto(
                x.FinanceAccountId,
                x.LineCode,
                x.LineName,
                x.ReportSection.ToStorageValue(),
                x.LineClassification.ToStorageValue(),
                x.Amount,
                x.Currency))
            .ToListAsync(cancellationToken);

    private async Task<List<LedgerStatementRow>> LoadSnapshotStatementRowsAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        FinancialStatementType statementType,
        CancellationToken cancellationToken)
    {
        var mappingLookup = await LoadStatementMappingLookupAsync(companyId, statementType, cancellationToken);
        if (mappingLookup.Count == 0)
        {
            return [];
        }

        var snapshots = await _dbContext.TrialBalanceSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId && mappingLookup.Keys.Contains(x.FinanceAccountId))
            .Select(x => new SnapshotBalanceRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.BalanceAmount,
                x.Currency))
            .ToListAsync(cancellationToken);

        return snapshots
            .Select(x =>
            {
                var mapping = mappingLookup[x.FinanceAccountId];
                return new LedgerStatementRow(
                    x.FinanceAccountId,
                    x.AccountCode,
                    x.AccountName,
                    mapping.ReportSection,
                    mapping.LineClassification,
                    x.BalanceAmount,
                    x.Currency);
            })
            .ToList();
    }

    private async Task<List<LedgerStatementRow>> LoadLedgerStatementRowsForPeriodAsync(
        Guid companyId,
        FiscalPeriodRow period,
        FinancialStatementType statementType,
        CancellationToken cancellationToken)
    {
        var mappingLookup = await LoadStatementMappingLookupAsync(companyId, statementType, cancellationToken);
        if (mappingLookup.Count == 0)
        {
            return [];
        }

        var ledgerRows = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc >= period.StartUtc &&
                x.LedgerEntry.EntryUtc < period.EndUtc &&
                mappingLookup.Keys.Contains(x.FinanceAccountId))
            .Select(x => new LedgerPostingRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.DebitAmount - x.CreditAmount,
                x.Currency))
            .ToListAsync(cancellationToken);

        return ledgerRows
            .GroupBy(x => new { x.FinanceAccountId, x.AccountCode, x.AccountName, x.Currency })
            .Select(group =>
            {
                var mapping = mappingLookup[group.Key.FinanceAccountId];
                return new LedgerStatementRow(
                    group.Key.FinanceAccountId,
                    group.Key.AccountCode,
                    group.Key.AccountName,
                    mapping.ReportSection,
                    mapping.LineClassification,
                    group.Sum(x => x.SignedAmount),
                    group.Key.Currency);
            })
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<LedgerStatementRow>> LoadBalanceSheetRowsAsync(
        Guid companyId,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var mappingLookup = await LoadStatementMappingLookupAsync(companyId, FinancialStatementType.BalanceSheet, cancellationToken);
        if (mappingLookup.Count == 0)
        {
            return [];
        }

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && mappingLookup.Keys.Contains(x.Id))
            .Select(x => new LedgerBalanceAccountRow(
                x.Id,
                x.Code,
                x.Name,
                x.OpeningBalance,
                x.Currency))
            .ToListAsync(cancellationToken);

        var postings = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc < endUtc &&
                mappingLookup.Keys.Contains(x.FinanceAccountId))
            .Select(x => new LedgerPostingAmountRow(
                x.FinanceAccountId,
                x.DebitAmount - x.CreditAmount))
            .ToListAsync(cancellationToken);

        var postingLookup = postings
            .GroupBy(x => x.FinanceAccountId)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.SignedAmount));

        return accounts
            .Select(account =>
            {
                var mapping = mappingLookup[account.AccountId];
                return new LedgerStatementRow(
                    account.AccountId,
                    account.AccountCode,
                    account.AccountName,
                    mapping.ReportSection,
                    mapping.LineClassification,
                    account.OpeningBalance + postingLookup.GetValueOrDefault(account.AccountId),
                    account.Currency);
            })
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<decimal> CalculateCurrentEarningsAsync(
        Guid companyId,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var rows = await LoadProfitAndLossRowsThroughAsync(companyId, endUtc, cancellationToken);
        return CalculateProfitAndLossTotal(rows);
    }

    private async Task<List<LedgerStatementRow>> LoadProfitAndLossRowsThroughAsync(
        Guid companyId,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var mappingLookup = await LoadStatementMappingLookupAsync(companyId, FinancialStatementType.ProfitAndLoss, cancellationToken);
        if (mappingLookup.Count == 0)
        {
            return [];
        }

        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && mappingLookup.Keys.Contains(x.Id))
            .Select(x => new LedgerBalanceAccountRow(
                x.Id,
                x.Code,
                x.Name,
                x.OpeningBalance,
                x.Currency))
            .ToListAsync(cancellationToken);

        var postings = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc < endUtc &&
                mappingLookup.Keys.Contains(x.FinanceAccountId))
            .Select(x => new LedgerPostingAmountRow(x.FinanceAccountId, x.DebitAmount - x.CreditAmount))
            .ToListAsync(cancellationToken);

        var postingLookup = postings
            .GroupBy(x => x.FinanceAccountId)
            .ToDictionary(x => x.Key, x => x.Sum(v => v.SignedAmount));

        return accounts
            .Select(account =>
            {
                var mapping = mappingLookup[account.AccountId];
                return new LedgerStatementRow(
                    account.AccountId,
                    account.AccountCode,
                    account.AccountName,
                    mapping.ReportSection,
                    mapping.LineClassification,
                    account.OpeningBalance + postingLookup.GetValueOrDefault(account.AccountId),
                    account.Currency);
            })
            .ToList();
    }

    private ProfitAndLossReportDto BuildProfitAndLossReport(
        FiscalPeriodRow period,
        IReadOnlyList<FinanceStatementLineDto> lines,
        bool usedSnapshot,
        FinancialStatementSnapshotMetadataDto? snapshot)
    {
        var revenueLines = lines
            .Where(x => IsProfitAndLossRevenueLine(x.ReportSection, x.LineClassification))
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expenseLines = lines
            .Where(x => !IsProfitAndLossRevenueLine(x.ReportSection, x.LineClassification))
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currency = ResolveCurrency(lines.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));

        return new ProfitAndLossReportDto(
            period.CompanyId,
            period.FiscalPeriodId,
            period.Name,
            period.StartUtc,
            period.EndUtc,
            period.IsClosed,
            usedSnapshot,
            currency,
            revenueLines,
            expenseLines,
            revenueLines.Sum(x => x.Amount),
            expenseLines.Sum(x => x.Amount),
            revenueLines.Sum(x => x.Amount) - expenseLines.Sum(x => x.Amount),
            snapshot);
    }

    private static List<FinanceStatementLineDto> BuildLiveBalanceSheetLines(
        IReadOnlyList<LedgerStatementRow> rows,
        decimal currentEarnings)
    {
        var lines = rows
            .Select(MapStatementLine)
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (currentEarnings != 0m)
        {
            lines.Add(new FinanceStatementLineDto(
                null,
                "current_earnings",
                "Current Earnings",
                FinancialStatementReportSection.BalanceSheetEquity.ToStorageValue(),
                FinancialStatementLineClassification.Equity.ToStorageValue(),
                currentEarnings,
                ResolveCurrency(lines.Select(x => new FinanceAmountRow(x.Amount, x.Currency)))));
        }

        return lines;
    }

    private BalanceSheetReportDto BuildBalanceSheetReport(
        FiscalPeriodRow period,
        IReadOnlyList<FinanceStatementLineDto> lines,
        bool usedSnapshot,
        FinancialStatementSnapshotMetadataDto? snapshot)
    {
        var currency = ResolveCurrency(lines.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
        var assets = lines.Where(x => string.Equals(x.ReportSection, FinancialStatementReportSection.BalanceSheetAssets.ToStorageValue(), StringComparison.Ordinal)).ToList();
        var liabilities = lines.Where(x => string.Equals(x.ReportSection, FinancialStatementReportSection.BalanceSheetLiabilities.ToStorageValue(), StringComparison.Ordinal)).ToList();
        var equity = lines.Where(x => string.Equals(x.ReportSection, FinancialStatementReportSection.BalanceSheetEquity.ToStorageValue(), StringComparison.Ordinal)).ToList();

        var totalAssets = assets.Sum(x => x.Amount);
        var totalLiabilities = liabilities.Sum(x => x.Amount);
        var totalEquity = equity.Sum(x => x.Amount);

        return new BalanceSheetReportDto(
            period.CompanyId,
            period.FiscalPeriodId,
            period.Name,
            period.StartUtc,
            period.EndUtc,
            period.IsClosed,
            usedSnapshot,
            currency,
            assets,
            liabilities,
            equity,
            totalAssets,
            totalLiabilities,
            totalEquity,
            totalAssets == totalLiabilities + totalEquity,
            snapshot);
    }

    private async Task<StatementLineResolution> ResolveSnapshotStatementLineAsync(
        GetFinancialStatementDrilldownQuery query,
        CancellationToken cancellationToken)
    {
        var snapshots = _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (query.SnapshotId.HasValue)
        {
            snapshots = snapshots.Where(x => x.Id == query.SnapshotId.Value);
        }
        else
        {
            if (!query.FiscalPeriodId.HasValue || !query.StatementType.HasValue || !query.SnapshotVersionNumber.HasValue)
            {
                throw new ArgumentException("Snapshot drilldown requires either snapshotId or fiscalPeriodId, statementType, and snapshotVersionNumber.", nameof(query));
            }

            snapshots = snapshots.Where(x =>
                x.FiscalPeriodId == query.FiscalPeriodId.Value &&
                x.StatementType == query.StatementType.Value &&
                x.VersionNumber == query.SnapshotVersionNumber.Value);
        }

        var snapshot = await snapshots
            .Select(x => new FinancialStatementSnapshotHeaderRow(
                x.Id,
                x.FiscalPeriodId,
                x.StatementType,
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency))
            .SingleOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            throw new KeyNotFoundException("The requested financial statement snapshot version was not found.");
        }

        var period = await LoadFiscalPeriodAsync(query.CompanyId, snapshot.FiscalPeriodId, cancellationToken);
        var line = await _dbContext.FinancialStatementSnapshotLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SnapshotId == snapshot.SnapshotId && x.LineCode == query.LineCode)
            .Select(x => new SnapshotStatementLineRow(
                x.FinanceAccountId,
                x.LineCode,
                x.LineName,
                x.ReportSection,
                x.LineClassification,
                x.Amount,
                x.Currency))
            .SingleOrDefaultAsync(cancellationToken);

        if (line is null)
        {
            throw new KeyNotFoundException("The requested snapshot report line was not found.");
        }

        var snapshotMetadata = MapSnapshotMetadata(snapshot);
        if (string.Equals(line.LineCode, "current_earnings", StringComparison.OrdinalIgnoreCase))
        {
            var rules = await LoadCurrentEarningsContributionRulesAsync(query.CompanyId, cancellationToken);
            return new StatementLineResolution(
                snapshot.StatementType,
                "snapshot",
                period,
                snapshotMetadata,
                line.LineCode,
                line.LineName,
                line.ReportSection,
                line.LineClassification,
                line.Amount,
                line.Currency,
                CalculateOpeningBalanceAdjustment(rules),
                rules);
        }

        if (!line.FinanceAccountId.HasValue)
        {
            throw new KeyNotFoundException("The requested snapshot report line is missing account metadata.");
        }

        var rule = await LoadAccountContributionRuleAsync(
            query.CompanyId,
            line.FinanceAccountId.Value,
            line.LineCode,
            line.LineName,
            line.ReportSection,
            line.LineClassification,
            query.StatementType == FinancialStatementType.BalanceSheet,
            cancellationToken);

        return new StatementLineResolution(
            snapshot.StatementType,
            "snapshot",
            period,
            snapshotMetadata,
            line.LineCode,
            line.LineName,
            line.ReportSection,
            line.LineClassification,
            line.Amount,
            line.Currency,
            CalculateOpeningBalanceAdjustment([rule]),
            [rule]);
    }

    private async Task<StatementLineResolution> ResolveLiveStatementLineAsync(
        GetFinancialStatementDrilldownQuery query,
        FiscalPeriodRow period,
        CancellationToken cancellationToken)
    {
        var statementType = query.StatementType
            ?? throw new ArgumentException("StatementType is required for live statement drilldown.", nameof(query));

        if (statementType == FinancialStatementType.BalanceSheet &&
            string.Equals(query.LineCode, "current_earnings", StringComparison.OrdinalIgnoreCase))
        {
            var currentEarnings = await CalculateCurrentEarningsAsync(query.CompanyId, period.EndUtc, cancellationToken);
            var rules = await LoadCurrentEarningsContributionRulesAsync(query.CompanyId, cancellationToken);
            return new StatementLineResolution(
                statementType,
                "live",
                period,
                null,
                "current_earnings",
                "Current Earnings",
                FinancialStatementReportSection.BalanceSheetEquity,
                FinancialStatementLineClassification.Equity,
                currentEarnings,
                ResolveCurrency(rules.Select(x => new FinanceAmountRow(x.OpeningBalance, x.Currency))),
                CalculateOpeningBalanceAdjustment(rules),
                rules);
        }

        if (statementType == FinancialStatementType.ProfitAndLoss)
        {
            var rows = await LoadLedgerStatementRowsForPeriodAsync(query.CompanyId, period, FinancialStatementType.ProfitAndLoss, cancellationToken);
            var row = rows.SingleOrDefault(x => string.Equals(x.AccountCode, query.LineCode, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("The requested live report line was not found.");
            var amount = NormalizeStatementAmount(row);
            var rule = await LoadAccountContributionRuleAsync(
                query.CompanyId,
                row.FinanceAccountId,
                row.AccountCode,
                row.AccountName,
                row.ReportSection,
                row.LineClassification,
                includeOpeningBalance: false,
                cancellationToken);

            return new StatementLineResolution(
                statementType,
                "live",
                period,
                null,
                row.AccountCode,
                row.AccountName,
                row.ReportSection,
                row.LineClassification,
                amount,
                row.Currency,
                0m,
                [rule]);
        }

        var balanceSheetRows = await LoadBalanceSheetRowsAsync(query.CompanyId, period.EndUtc, cancellationToken);
        var balanceSheetRow = balanceSheetRows.SingleOrDefault(x => string.Equals(x.AccountCode, query.LineCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("The requested live report line was not found.");
        var liveRule = await LoadAccountContributionRuleAsync(
            query.CompanyId,
            balanceSheetRow.FinanceAccountId,
            balanceSheetRow.AccountCode,
            balanceSheetRow.AccountName,
            balanceSheetRow.ReportSection,
            balanceSheetRow.LineClassification,
            includeOpeningBalance: true,
            cancellationToken);

        return new StatementLineResolution(
            statementType,
            "live",
            period,
            null,
            balanceSheetRow.AccountCode,
            balanceSheetRow.AccountName,
            balanceSheetRow.ReportSection,
            balanceSheetRow.LineClassification,
            NormalizeStatementAmount(balanceSheetRow),
            balanceSheetRow.Currency,
            CalculateOpeningBalanceAdjustment([liveRule]),
            [liveRule]);
    }

    private static FinancePaymentAllocationDto MapPaymentAllocation(PaymentAllocation allocation) =>
        new(
            allocation.Id,
            allocation.CompanyId,
            allocation.PaymentId,
            allocation.InvoiceId,
            allocation.BillId,
            allocation.AllocatedAmount,
            allocation.Currency,
            allocation.CreatedUtc,
            allocation.UpdatedUtc,
            allocation.SourceSimulationEventRecordId,
            allocation.PaymentSourceSimulationEventRecordId,
            allocation.TargetSourceSimulationEventRecordId);

    private async Task<ContributionRule> LoadAccountContributionRuleAsync(
        Guid companyId,
        Guid financeAccountId,
        string lineCode,
        string lineName,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        bool includeOpeningBalance,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == financeAccountId)
            .Select(x => new ContributionAccountRow(x.Id, x.Code, x.Name, x.OpeningBalance, x.Currency))
            .SingleAsync(cancellationToken);
        return new ContributionRule(
            account.AccountId,
            lineCode,
            lineName,
            account.AccountCode,
            account.AccountName,
            reportSection,
            lineClassification,
            ResolveContributionFactor(reportSection, lineClassification),
            includeOpeningBalance ? account.OpeningBalance : 0m,
            account.Currency);
    }

    private async Task<List<ContributionRule>> LoadCurrentEarningsContributionRulesAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var mappings = await LoadStatementMappingLookupAsync(companyId, FinancialStatementType.ProfitAndLoss, cancellationToken);
        return mappings.Values
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ContributionRule(
                x.FinanceAccountId,
                "current_earnings",
                "Current Earnings",
                x.AccountCode,
                x.AccountName,
                x.ReportSection,
                x.LineClassification,
                ResolveContributionFactor(x.ReportSection, x.LineClassification),
                x.OpeningBalance,
                x.Currency))
            .ToList();
    }

    private static decimal CalculateOpeningBalanceAdjustment(IEnumerable<ContributionRule> rules) =>
        Math.Round(rules.Sum(x => x.OpeningBalance * x.ContributionFactor), 2, MidpointRounding.AwayFromZero);

    private async Task<List<FinancialStatementDrilldownJournalEntryDto>> LoadDrilldownEntriesAsync(
        Guid companyId,
        FiscalPeriodRow period,
        FinancialStatementType statementType,
        IReadOnlyList<ContributionRule> rules,
        CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
        {
            return [];
        }

        var accountIds = rules.Select(x => x.FinanceAccountId).Distinct().ToArray();
        var factorByAccountId = rules.ToDictionary(x => x.FinanceAccountId, x => x.ContributionFactor);
        var postings = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                accountIds.Contains(x.FinanceAccountId) &&
                (statementType == FinancialStatementType.ProfitAndLoss
                    ? x.LedgerEntry.EntryUtc >= period.StartUtc && x.LedgerEntry.EntryUtc < period.EndUtc
                    : x.LedgerEntry.EntryUtc < period.EndUtc))
            .OrderBy(x => x.LedgerEntry.EntryUtc)
            .ThenBy(x => x.LedgerEntry.EntryNumber)
            .Select(x => new DrilldownPostingRow(
                x.LedgerEntryId,
                x.LedgerEntry.EntryNumber,
                x.LedgerEntry.EntryUtc,
                x.LedgerEntry.Description,
                x.Id,
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.DebitAmount,
                x.CreditAmount,
                x.Currency,
                x.Description))
            .ToListAsync(cancellationToken);

        return postings
            .GroupBy(x => new { x.LedgerEntryId, x.EntryNumber, x.EntryUtc, x.EntryDescription })
            .Select(group =>
            {
                var lines = group
                    .Select(line => new FinancialStatementDrilldownJournalLineDto(
                        line.LedgerEntryLineId,
                        line.FinanceAccountId,
                        line.AccountCode,
                        line.AccountName,
                        line.DebitAmount,
                        line.CreditAmount,
                        Math.Round((line.DebitAmount - line.CreditAmount) * factorByAccountId[line.FinanceAccountId], 2, MidpointRounding.AwayFromZero),
                        line.Currency,
                        line.LineDescription))
                    .Where(x => x.ContributionAmount != 0m)
                    .ToList();

                return new FinancialStatementDrilldownJournalEntryDto(
                    group.Key.LedgerEntryId,
                    group.Key.EntryNumber,
                    group.Key.EntryUtc,
                    group.Key.EntryDescription,
                    Math.Round(lines.Sum(x => x.ContributionAmount), 2, MidpointRounding.AwayFromZero),
                    lines);
            })
            .Where(x => x.Lines.Count > 0)
            .ToList();
    }

    private static decimal CalculateProfitAndLossTotal(IEnumerable<LedgerStatementRow> rows) =>
        rows.Sum(row => IsProfitAndLossRevenueLine(row.ReportSection.ToStorageValue(), row.LineClassification.ToStorageValue())
            ? NormalizeStatementAmount(row)
            : -NormalizeStatementAmount(row));

    private static FinanceStatementLineDto MapStatementLine(LedgerStatementRow row) =>
        new(
            row.FinanceAccountId,
            row.AccountCode,
            row.AccountName,
            row.ReportSection.ToStorageValue(),
            row.LineClassification.ToStorageValue(),
            NormalizeStatementAmount(row),
            row.Currency);

    private static decimal NormalizeStatementAmount(LedgerStatementRow row)
    {
        var amount = row.ReportSection switch
        {
            FinancialStatementReportSection.BalanceSheetAssets => row.BalanceAmount,
            FinancialStatementReportSection.BalanceSheetLiabilities => -row.BalanceAmount,
            FinancialStatementReportSection.BalanceSheetEquity => -row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossRevenue => -row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossCostOfSales => row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossOperatingExpenses => row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossTaxes => row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense when row.LineClassification == FinancialStatementLineClassification.NonOperatingIncome => -row.BalanceAmount,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense => row.BalanceAmount,
            _ => row.BalanceAmount
        };

        return Math.Abs(amount) < 0.0001m ? 0m : Math.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private static bool IsProfitAndLossRevenueLine(string reportSection, string lineClassification) =>
        string.Equals(reportSection, FinancialStatementReportSection.ProfitAndLossRevenue.ToStorageValue(), StringComparison.Ordinal) ||
        string.Equals(lineClassification, FinancialStatementLineClassification.NonOperatingIncome.ToStorageValue(), StringComparison.Ordinal);

    private static bool IsProfitAndLossRevenueLine(FinancialStatementReportSection reportSection, FinancialStatementLineClassification lineClassification) =>
        reportSection == FinancialStatementReportSection.ProfitAndLossRevenue ||
        lineClassification == FinancialStatementLineClassification.NonOperatingIncome;

    private static decimal ResolveContributionFactor(
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification) =>
        reportSection switch
        {
            FinancialStatementReportSection.BalanceSheetAssets => 1m,
            FinancialStatementReportSection.BalanceSheetLiabilities => -1m,
            FinancialStatementReportSection.BalanceSheetEquity => -1m,
            FinancialStatementReportSection.ProfitAndLossRevenue => -1m,
            FinancialStatementReportSection.ProfitAndLossCostOfSales => 1m,
            FinancialStatementReportSection.ProfitAndLossOperatingExpenses => 1m,
            FinancialStatementReportSection.ProfitAndLossTaxes => 1m,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense when lineClassification == FinancialStatementLineClassification.NonOperatingIncome => -1m,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense => 1m,
            _ => 1m
        };

    private async Task<List<FinanceAccountBalanceDto>> BuildAccountBalancesAsync(
        Guid companyId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AccountRow(x.Id, x.Code, x.Name, x.AccountType, x.OpeningBalance, x.Currency))
            .ToListAsync(cancellationToken);

        var balances = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AsOfUtc <= asOfUtc)
            .Select(x => new BalanceRow(x.AccountId, x.AsOfUtc, x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var latestBalanceByAccount = balances
            .GroupBy(x => x.AccountId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(balance => balance.AsOfUtc).First());

        var transactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc <= asOfUtc)
            .Select(x => new TransactionBalanceRow(
                x.AccountId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Description,
                x.ExternalReference))
            .ToListAsync(cancellationToken);

        var transactionsByAccount = transactions
            .GroupBy(x => x.AccountId)
            .ToDictionary(x => x.Key, x => x.ToList());

        return accounts
            .Select(account =>
            {
                var accountTransactions = transactionsByAccount.GetValueOrDefault(account.Id) ?? [];
                if (latestBalanceByAccount.TryGetValue(account.Id, out var balance))
                {
                    var postedSinceSnapshot = accountTransactions
                        .Where(transaction => transaction.TransactionUtc > balance.AsOfUtc)
                        .Where(IsCashMovementTransaction)
                        .Sum(transaction => transaction.Amount);

                    return new FinanceAccountBalanceDto(
                        account.Id,
                        account.Code,
                        account.Name,
                        account.AccountType,
                        balance.Amount + postedSinceSnapshot,
                        balance.Currency,
                        asOfUtc);
                }

                var postedAmount = accountTransactions
                    .Where(IsCashMovementTransaction)
                    .Sum(transaction => transaction.Amount);
                return new FinanceAccountBalanceDto(
                    account.Id,
                    account.Code,
                    account.Name,
                    account.AccountType,
                    account.OpeningBalance + postedAmount,
                    account.Currency,
                    asOfUtc);
            })
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<FinanceLinkedDocumentAccessDto> BuildDocumentAccessAsync(
        Guid companyId,
        Guid? documentId,
        IReadOnlyDictionary<Guid, FinanceLinkedDocumentRow> linkedDocuments,
        CancellationToken cancellationToken)
    {
        if (documentId is not Guid resolvedDocumentId)
        {
            return new FinanceLinkedDocumentAccessDto(
                "missing",
                "Linked document is no longer available.",
                false,
                null);
        }

        if (!linkedDocuments.TryGetValue(resolvedDocumentId, out var document))
        {
            return new FinanceLinkedDocumentAccessDto(
                "inaccessible",
                "Linked document unavailable or you do not have access.",
                false,
                null);
        }

        return new FinanceLinkedDocumentAccessDto(
            "available",
            "Linked document available.",
            true,
            new FinanceLinkedDocumentDto(document.Id, document.Title, document.OriginalFileName, document.ContentType));
    }

    private async Task<Dictionary<Guid, FinanceLinkedDocumentRow>> LoadLinkedDocumentsAsync(
        Guid companyId,
        IEnumerable<Guid?> documentIds,
        CancellationToken cancellationToken)
    {
        var ids = documentIds
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        if (_documentService is not null)
        {
            return await LoadLinkedDocumentsThroughKnowledgeServiceAsync(companyId, ids, cancellationToken);
        }

        var documents = await _dbContext.CompanyKnowledgeDocuments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var accessContext = BuildAccessContext(companyId);
        return documents
            .Where(document =>
                _accessPolicyEvaluator is null ||
                accessContext is null ||
                _accessPolicyEvaluator.CanAccess(accessContext, document))
            .Select(x => new FinanceLinkedDocumentRow(x.Id, x.Title, x.OriginalFileName, x.ContentType ?? string.Empty))
            .ToDictionary(x => x.Id);
    }

    private async Task<Dictionary<Guid, FinanceLinkedDocumentRow>> LoadLinkedDocumentsThroughKnowledgeServiceAsync(
        Guid companyId,
        IReadOnlyList<Guid> documentIds,
        CancellationToken cancellationToken)
    {
        var linkedDocuments = new Dictionary<Guid, FinanceLinkedDocumentRow>();
        foreach (var documentId in documentIds)
        {
            CompanyKnowledgeDocumentDto? document;
            try
            {
                document = await _documentService!.GetAsync(companyId, documentId, cancellationToken);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (document is null)
            {
                continue;
            }

            linkedDocuments[document.Id] = new FinanceLinkedDocumentRow(
                document.Id,
                document.Title,
                document.OriginalFileName,
                document.ContentType ?? string.Empty);
        }

        return linkedDocuments;
    }

    private static FinanceLinkedDocumentDto? MapLinkedDocument(Guid? documentId, IReadOnlyDictionary<Guid, FinanceLinkedDocumentRow> linkedDocuments) =>
        documentId is Guid id && linkedDocuments.TryGetValue(id, out var document)
            ? new FinanceLinkedDocumentDto(document.Id, document.Title, document.OriginalFileName, document.ContentType)
            : null;

    private FinanceActionPermissionsDto BuildActionPermissions()
    {
        var membershipRole = _companyContextAccessor?.Membership?.MembershipRole.ToStorageValue();

        return new FinanceActionPermissionsDto(
            FinanceAccess.CanEditTransactionCategory(membershipRole),
            FinanceAccess.CanApproveInvoices(membershipRole),
            FinanceAccess.CanManagePolicies(membershipRole));
    }

    private static FinanceSeedAnomalyDto MapSeedAnomaly(Domain.Entities.FinanceSeedAnomaly anomaly) =>
        new(
            anomaly.Id,
            anomaly.AnomalyType,
            anomaly.ScenarioProfile,
            anomaly.GetAffectedRecordIds(),
            anomaly.ExpectedDetectionMetadataJson);

    private static bool IsIncludedReceivable(string status, string settlementStatus)
    {
        if (string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("paid" or "cancelled" or "canceled" or "void" or "voided" or "written_off" or "rejected");
    }

    private static bool IsIncludedPayable(string status, string settlementStatus)
    {
        if (string.Equals(FinanceSettlementStatuses.Normalize(settlementStatus), FinanceSettlementStatuses.Paid, StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedStatus = NormalizeStatus(status);
        return normalizedStatus is not ("paid" or "cancelled" or "canceled" or "void" or "voided");
    }

    private static decimal CalculateRemainingBalance(decimal amount, decimal completedAllocatedAmount) =>
        Math.Round(Math.Max(0m, amount - completedAllocatedAmount), 2, MidpointRounding.AwayFromZero);

    private static string NormalizeStatus(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    private CompanyKnowledgeAccessContext? BuildAccessContext(Guid companyId)
    {
        var membership = _companyContextAccessor?.Membership;
        if (membership is null)
        {
            return null;
        }

        return new CompanyKnowledgeAccessContext(
            companyId,
            membership.MembershipId,
            membership.UserId,
            membership.MembershipRole.ToStorageValue(),
            Array.Empty<string>());
    }

    private static void EnsureCompanyId(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }
    }

    private void EnsureTenant(Guid companyId)
    {
        EnsureCompanyId(companyId);

        if (_companyContextAccessor is null)
        {
            return;
        }

        if (_companyContextAccessor.CompanyId is not Guid currentCompanyId ||
            currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance reads are scoped to the active company context.");
        }
    }

    private async Task<List<Alert>> LoadFinanceAnomalyAlertsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Type == AlertType.Anomaly &&
                x.CorrelationId.StartsWith("fin-anom:"))
            .OrderByDescending(x => x.LastDetectedUtc ?? x.CreatedUtc)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

    private async Task<Dictionary<Guid, FinanceAnomalyTransactionRow>> LoadFinanceAnomalyTransactionsAsync(
        Guid companyId,
        IEnumerable<Alert> alerts,
        CancellationToken cancellationToken)
    {
        var transactionIds = alerts
            .Select(alert => ExtractGuid(alert.Evidence, "transactionId"))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (transactionIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && transactionIds.Contains(x.Id))
            .Select(x => new FinanceAnomalyTransactionRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.TransactionUtc,
                x.ExternalReference,
                x.Amount,
                x.Currency))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, FinanceAnomalyInvoiceLinkRow>> LoadFinanceAnomalyInvoicesAsync(
        Guid companyId,
        IEnumerable<FinanceAnomalyTransactionRow> transactions,
        CancellationToken cancellationToken)
    {
        var invoiceIds = transactions
            .Select(x => x.InvoiceId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (invoiceIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && invoiceIds.Contains(x.Id))
            .Select(x => new FinanceAnomalyInvoiceLinkRow(
                x.Id,
                x.InvoiceNumber,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.IssuedUtc,
                x.Amount,
                x.Currency))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    private async Task<Dictionary<Guid, FinanceAnomalyBillLinkRow>> LoadFinanceAnomalyBillsAsync(
        Guid companyId,
        IEnumerable<FinanceAnomalyTransactionRow> transactions,
        CancellationToken cancellationToken)
    {
        var billIds = transactions
            .Select(x => x.BillId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (billIds.Length == 0)
        {
            return [];
        }

        return await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && billIds.Contains(x.Id))
            .Select(x => new FinanceAnomalyBillLinkRow(
                x.Id,
                x.BillNumber,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.ReceivedUtc,
                x.Amount,
                x.Currency))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    private async Task<Dictionary<string, IReadOnlyList<WorkTask>>> LoadFinanceAnomalyTasksByCorrelationIdAsync(
        Guid companyId,
        IEnumerable<Alert> alerts,
        CancellationToken cancellationToken)
    {
        var correlationIds = alerts
            .Select(x => x.CorrelationId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (correlationIds.Length == 0)
        {
            return new Dictionary<string, IReadOnlyList<WorkTask>>(StringComparer.OrdinalIgnoreCase);
        }

        var tasks = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CorrelationId != null && correlationIds.Contains(x.CorrelationId))
            .ToListAsync(cancellationToken);

        return tasks
            .GroupBy(x => x.CorrelationId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<WorkTask>)x
                    .OrderByDescending(task => task.UpdatedUtc)
                    .ThenByDescending(task => task.CreatedUtc)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private FinanceAnomalyWorkbenchItemDto? MapFinanceAnomalyWorkbenchItem(
        Alert alert,
        IReadOnlyDictionary<Guid, FinanceAnomalyTransactionRow> transactions,
        IReadOnlyDictionary<Guid, FinanceAnomalyInvoiceLinkRow> invoices,
        IReadOnlyDictionary<Guid, FinanceAnomalyBillLinkRow> bills,
        IReadOnlyDictionary<string, IReadOnlyList<WorkTask>> tasksByCorrelationId)
    {
        var transactionId = ExtractGuid(alert.Evidence, "transactionId");
        var transaction = transactionId.HasValue ? transactions.GetValueOrDefault(transactionId.Value) : null;
        var invoice = transaction?.InvoiceId is Guid invoiceId ? invoices.GetValueOrDefault(invoiceId) : null;
        var bill = transaction?.BillId is Guid billId ? bills.GetValueOrDefault(billId) : null;
        var tasks = tasksByCorrelationId.GetValueOrDefault(alert.CorrelationId) ?? [];
        var latestTask = tasks
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .FirstOrDefault();

        return new FinanceAnomalyWorkbenchItemDto(
            alert.Id,
            ExtractString(alert.Metadata, "anomalyType")
                ?? ExtractString(alert.Evidence, "anomalyType")
                ?? "unknown",
            latestTask?.Status.ToStorageValue() ?? alert.Status.ToStorageValue(),
            ExtractDecimal(alert.Metadata, "confidence")
                ?? ExtractDecimal(alert.Evidence, "confidence")
                ?? 0m,
            NormalizeOptionalText(
                transaction?.CounterpartyName
                ?? invoice?.CounterpartyName
                ?? bill?.CounterpartyName
                ?? ExtractString(alert.Evidence, "counterpartyName")),
            transaction?.Id,
            transaction?.ExternalReference
                ?? ExtractString(alert.Evidence, "transactionExternalReference")
                ?? alert.Title,
            alert.Summary,
            ExtractString(alert.Metadata, "recommendedAction")
                ?? ExtractString(alert.Evidence, "recommendedAction")
                ?? string.Empty,
            alert.LastDetectedUtc ?? alert.CreatedUtc,
            BuildDeduplicationMetadata(alert),
            latestTask?.Id,
            latestTask?.Status.ToStorageValue(),
            invoice?.Id,
            bill?.Id);
    }

    private static IReadOnlyList<FinanceAnomalyRecordLinkDto> BuildFinanceAnomalyRecordLinks(
        FinanceAnomalyTransactionRow? transaction,
        FinanceAnomalyInvoiceLinkRow? invoice,
        FinanceAnomalyBillLinkRow? bill)
    {
        var links = new List<FinanceAnomalyRecordLinkDto>(3);

        if (transaction is not null)
        {
            links.Add(new FinanceAnomalyRecordLinkDto(
                transaction.Id,
                "transaction",
                transaction.ExternalReference,
                transaction.TransactionUtc,
                transaction.Amount,
                transaction.Currency));
        }

        if (invoice is not null)
        {
            links.Add(new FinanceAnomalyRecordLinkDto(
                invoice.Id,
                "invoice",
                invoice.InvoiceNumber,
                invoice.IssuedUtc,
                invoice.Amount,
                invoice.Currency));
        }

        if (bill is not null)
        {
            links.Add(new FinanceAnomalyRecordLinkDto(bill.Id, "bill", bill.BillNumber, bill.ReceivedUtc, bill.Amount, bill.Currency));
        }

        return links;
    }

    private static FinanceAnomalyDeduplicationDto? BuildDeduplicationMetadata(Alert alert)
    {
        var key = NormalizeOptionalText(ExtractString(alert.Metadata, "dedupeKey"));
        var windowStartUtc = ExtractDateTime(alert.Metadata, "deduplicationWindowStartUtc")
            ?? ExtractDateTime(alert.Evidence, "deduplicationWindowStartUtc");
        var windowEndUtc = ExtractDateTime(alert.Metadata, "deduplicationWindowEndUtc")
            ?? ExtractDateTime(alert.Evidence, "deduplicationWindowEndUtc");

        return string.IsNullOrWhiteSpace(key) && windowStartUtc is null && windowEndUtc is null
            ? null
            : new FinanceAnomalyDeduplicationDto(key, windowStartUtc, windowEndUtc);
    }

    private static int NormalizeLimit(int limit) =>
        limit <= 0
            ? DefaultLimit
            : Math.Min(limit, MaxLimit);

    private static DateTime? NormalizeUtc(DateTime? value) =>
        value is null
            ? null
            : value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? NormalizeFlaggedState(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "all" => null,
            "flagged" => "flagged",
            "not_flagged" => "not_flagged",
            _ => throw new ArgumentException("Flagged state must be 'all', 'flagged', or 'not_flagged'.", nameof(value))
        };
    }

    private static bool MatchesFlaggedState(string? flaggedState, bool isFlagged) =>
        flaggedState switch
        {
            "flagged" => isFlagged,
            "not_flagged" => !isFlagged,
            _ => true
        };

    private static bool IsCashAccount(string name, string code, string accountType) =>
        (string.Equals(accountType, "cash", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(accountType, "asset", StringComparison.OrdinalIgnoreCase)) &&
        (code.StartsWith("19", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "1000", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("kassa", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("plusgiro", StringComparison.OrdinalIgnoreCase));

    private static bool IsCashMovementTransaction(TransactionBalanceRow transaction) =>
        !string.Equals(transaction.TransactionType, "voucher", StringComparison.OrdinalIgnoreCase) ||
        IsExplicitBankPaymentVoucher(transaction.Description, transaction.ExternalReference);

    private static bool IsExplicitBankPaymentVoucher(string description, string externalReference)
    {
        var text = string.Concat(description, " ", externalReference);
        var mentionsBank =
            text.Contains("bank", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("bankgiro", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("plusgiro", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("kassa", StringComparison.OrdinalIgnoreCase);
        var mentionsPayment =
            text.Contains("payment", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("betal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("inbetal", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("utbetal", StringComparison.OrdinalIgnoreCase);

        return mentionsBank && mentionsPayment;
    }

    private static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category)
            ? "Uncategorized"
            : category.Trim();

    private static string? NormalizeFilterToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()
                .Replace(" ", "_", StringComparison.Ordinal)
                .Replace("-", "_", StringComparison.Ordinal)
                .ToLowerInvariant();

    private static decimal? NormalizeConfidence(decimal? value) =>
        value.HasValue ? Math.Clamp(value.Value, 0m, 1m) : null;

    private static (int Page, int PageSize) NormalizePagination(int page, int pageSize)
    {
        var normalizedPage = page <= 0 ? 1 : page;
        var normalizedPageSize = pageSize <= 0 ? 50 : Math.Clamp(pageSize, 1, 100);

        return (normalizedPage, normalizedPageSize);
    }

    private static string? ExtractString(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        TryGetNode(values, key)?.ToString().Trim();

    private static Guid? ExtractGuid(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        Guid.TryParse(TryGetNode(values, key)?.ToString(), out var resolved) ? resolved : null;

    private static decimal? ExtractDecimal(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        decimal.TryParse(TryGetNode(values, key)?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var resolved)
            ? resolved
            : null;

    private static DateTime? ExtractDateTime(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        DateTime.TryParse(
            TryGetNode(values, key)?.ToString(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var resolved)
            ? resolved
            : null;

    private static JsonNode? TryGetNode(IReadOnlyDictionary<string, JsonNode?>? values, string key)
    {
        if (values is null || !values.TryGetValue(key, out var node))
        {
            return null;
        }

        return node;
    }

    private static string ResolveTransactionAnomalyState(IReadOnlyCollection<FinanceSeedAnomalyDto>? anomalies)
    {
        if (anomalies is not { Count: > 0 })
        {
            return "clear";
        }

        return anomalies.Any(x => string.Equals(x.AnomalyType, "missing_receipt", StringComparison.OrdinalIgnoreCase))
            ? "needs_review"
            : "flagged";
    }

    private static string ResolveTransactionAnomalyState(IReadOnlyCollection<FinanceSeedAnomalyDto>? anomalies, bool requiresDocumentReview)
    {
        var anomalyState = ResolveTransactionAnomalyState(anomalies);
        return requiresDocumentReview && IsClearTransactionAnomalyState(anomalyState)
            ? "needs_review"
            : anomalyState;
    }

    private static bool IsClearTransactionAnomalyState(string? anomalyState)
    {
        var normalized = NormalizeOptionalText(anomalyState)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized) || normalized is "clear" or "none" or "normal" or "resolved";
    }

    private async Task<Dictionary<Guid, TransactionDocumentReviewState>> LoadInvoiceReviewStatesAsync(
        Guid companyId,
        IEnumerable<Guid?> invoiceIds,
        CancellationToken cancellationToken)
    {
        var ids = invoiceIds
            .Where(x => x.HasValue && x.Value != Guid.Empty)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var states = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.SettlementStatus,
                x.PaidAmount,
                x.Amount,
                x.Currency,
                x.ProviderStatus
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => new TransactionDocumentReviewState(x.SettlementStatus, x.PaidAmount, x.Amount, x.Currency, x.ProviderStatus),
                cancellationToken);

        var allocatedAmounts = await LoadAllocatedAmountsByInvoiceAsync(companyId, ids, cancellationToken);
        foreach (var (invoiceId, allocatedAmount) in allocatedAmounts)
        {
            if (states.TryGetValue(invoiceId, out var state) &&
                !HasProviderBalanceStatus(state.ProviderStatus) &&
                allocatedAmount > state.PaidAmount)
            {
                states[invoiceId] = state with { PaidAmount = allocatedAmount };
            }
        }

        return states;
    }

    private async Task<IReadOnlyList<FinanceInvoiceRelatedTransactionDto>> LoadInvoiceRelatedTransactionsAsync(
        Guid companyId,
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoiceId)
            .OrderBy(x => x.TransactionUtc)
            .ThenBy(x => x.Description)
            .Select(x => new FinanceInvoiceRelatedTransactionDto(
                x.Id,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference))
            .ToListAsync(cancellationToken);

        return rows;
    }

    private async Task<IReadOnlyList<FinanceInvoiceRelatedTransactionDto>> LoadBillRelatedTransactionsAsync(
        Guid companyId,
        Guid billId,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BillId == billId)
            .OrderBy(x => x.TransactionUtc)
            .ThenBy(x => x.Description)
            .Select(x => new FinanceInvoiceRelatedTransactionDto(
                x.Id,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference))
            .ToListAsync(cancellationToken);

        return rows;
    }

    private async Task<Dictionary<Guid, TransactionDocumentReviewState>> LoadBillReviewStatesAsync(
        Guid companyId,
        IEnumerable<Guid?> billIds,
        CancellationToken cancellationToken)
    {
        var ids = billIds
            .Where(x => x.HasValue && x.Value != Guid.Empty)
            .Select(x => x!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var states = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.SettlementStatus,
                x.PaidAmount,
                x.Amount,
                x.Currency,
                x.ProviderStatus
            })
            .ToDictionaryAsync(
                x => x.Id,
                x => new TransactionDocumentReviewState(x.SettlementStatus, x.PaidAmount, x.Amount, x.Currency, x.ProviderStatus),
                cancellationToken);

        var allocatedAmounts = await LoadAllocatedAmountsByBillAsync(companyId, ids, cancellationToken);
        foreach (var (billId, allocatedAmount) in allocatedAmounts)
        {
            if (states.TryGetValue(billId, out var state) &&
                !HasProviderBalanceStatus(state.ProviderStatus) &&
                allocatedAmount > state.PaidAmount)
            {
                states[billId] = state with { PaidAmount = allocatedAmount };
            }
        }

        return states;
    }

    private async Task<Dictionary<Guid, decimal>> LoadAllocatedAmountsByInvoiceAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> invoiceIds,
        CancellationToken cancellationToken)
    {
        if (invoiceIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InvoiceId.HasValue && invoiceIds.Contains(x.InvoiceId.Value))
            .GroupBy(x => x.InvoiceId!.Value)
            .Select(x => new { Id = x.Key, Amount = x.Sum(allocation => allocation.AllocatedAmount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.Id,
            x => decimal.Round(Math.Abs(x.Amount), 2, MidpointRounding.AwayFromZero));
    }

    private async Task<Dictionary<Guid, decimal>> LoadAllocatedAmountsByBillAsync(
        Guid companyId,
        IReadOnlyCollection<Guid> billIds,
        CancellationToken cancellationToken)
    {
        if (billIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BillId.HasValue && billIds.Contains(x.BillId.Value))
            .GroupBy(x => x.BillId!.Value)
            .Select(x => new { Id = x.Key, Amount = x.Sum(allocation => allocation.AllocatedAmount) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.Id,
            x => decimal.Round(Math.Abs(x.Amount), 2, MidpointRounding.AwayFromZero));
    }

    private static bool RequiresLinkedDocumentReview(
        FinanceTransactionRow row,
        FortnoxVoucherAmountFallback? fallback,
        IReadOnlyDictionary<Guid, TransactionDocumentReviewState> invoiceReviewStates,
        IReadOnlyDictionary<Guid, TransactionDocumentReviewState> billReviewStates)
    {
        if (IsPaymentTransaction(row.TransactionType))
        {
            return false;
        }

        var invoiceId = row.InvoiceId ?? fallback?.InvoiceId;
        var billId = row.BillId ?? fallback?.BillId;

        if (invoiceId.HasValue &&
            invoiceReviewStates.TryGetValue(invoiceId.Value, out var invoiceState) &&
            IsPartiallyPaid(invoiceState))
        {
            return true;
        }

        return billId.HasValue &&
            billReviewStates.TryGetValue(billId.Value, out var billState) &&
            IsPartiallyPaid(billState);
    }

    private static bool IsPaymentTransaction(string? transactionType)
    {
        var normalized = NormalizeOptionalText(transactionType)?.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "customer_payment" or "supplier_payment" or "payment";
    }

    private async Task<bool> IsFortnoxPaymentSyncBlockedAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationSyncStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                    (x.EntityType == "invoice_payments" || x.EntityType == "supplier_invoice_payments") &&
                    x.Status == FinanceIntegrationSyncStatuses.Failed &&
                    x.LastErrorSummary != null &&
                    x.LastErrorSummary.Contains("payment") &&
                    x.LastErrorSummary.Contains("permission"),
                cancellationToken);

    private static bool RequiresFortnoxPaymentSyncReview(
        bool paymentSyncBlocked,
        bool hasFortnoxReference,
        FinanceTransactionRow row,
        FortnoxVoucherAmountFallback? fallback) =>
        paymentSyncBlocked &&
        hasFortnoxReference &&
        ((row.InvoiceId ?? fallback?.InvoiceId).HasValue ||
         (row.BillId ?? fallback?.BillId).HasValue ||
         IsFortnoxInvoiceLikeTransaction(row));

    private static bool IsFortnoxInvoiceLikeTransaction(FinanceTransactionRow row)
    {
        var description = NormalizeOptionalText(row.Description)?.ToLowerInvariant() ?? string.Empty;
        var reference = NormalizeOptionalText(row.ExternalReference)?.ToLowerInvariant() ?? string.Empty;
        return description.Contains("kundfaktura", StringComparison.Ordinal) ||
            description.Contains("customer invoice", StringComparison.Ordinal) ||
            description.Contains("supplier invoice", StringComparison.Ordinal) ||
            description.Contains("leverantÃ¶rsfaktura", StringComparison.Ordinal) ||
            reference.StartsWith("b-", StringComparison.Ordinal);
    }

    private static bool IsPartiallyPaid(TransactionDocumentReviewState state) =>
        string.Equals(FinanceSettlementStatuses.Normalize(state.SettlementStatus), FinanceSettlementStatuses.PartiallyPaid, StringComparison.Ordinal) ||
        state.PaidAmount > 0m && state.PaidAmount < Math.Abs(state.Amount);

    private static bool HasProviderBalanceStatus(string? providerStatus) =>
        !string.IsNullOrWhiteSpace(providerStatus) &&
        providerStatus
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.StartsWith("balance=", StringComparison.OrdinalIgnoreCase));

    private static FinanceTransactionPaymentContextDto? BuildPaymentContext(
        Guid? invoiceId,
        Guid? billId,
        IReadOnlyDictionary<Guid, TransactionDocumentReviewState> invoiceReviewStates,
        IReadOnlyDictionary<Guid, TransactionDocumentReviewState> billReviewStates)
    {
        var state = invoiceId.HasValue && invoiceReviewStates.TryGetValue(invoiceId.Value, out var invoiceState)
            ? invoiceState
            : billId.HasValue && billReviewStates.TryGetValue(billId.Value, out var billState)
                ? billState
                : null;

        if (state is null)
        {
            return null;
        }

        var totalAmount = decimal.Round(Math.Abs(state.Amount), 2, MidpointRounding.AwayFromZero);
        var paidAmount = decimal.Round(Math.Abs(state.PaidAmount), 2, MidpointRounding.AwayFromZero);
        var remainingAmount = Math.Max(0m, decimal.Round(totalAmount - paidAmount, 2, MidpointRounding.AwayFromZero));

        return new FinanceTransactionPaymentContextDto(
            IsPartiallyPaid(state),
            paidAmount,
            totalAmount,
            remainingAmount,
            string.IsNullOrWhiteSpace(state.Currency) ? "SEK" : state.Currency);
    }

    private static string ResolveCurrency(IEnumerable<FinanceAmountRow> rows)
    {
        var currencies = rows
            .Select(x => x.Currency)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return currencies.Count switch
        {
            0 => "USD",
            1 => currencies[0],
            _ => "MIXED"
        };
    }

    private async Task<FinancePolicyConfigurationDto> LoadPolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return configuration is null
            ? new FinancePolicyConfigurationDto(companyId, "USD", 10000m, 5000m, true, -10000m, 10000m, 90, 30)
            : new FinancePolicyConfigurationDto(
                configuration.CompanyId,
                configuration.ApprovalCurrency,
                configuration.InvoiceApprovalThreshold,
                configuration.BillApprovalThreshold,
                configuration.RequireCounterpartyForTransactions,
                configuration.AnomalyDetectionLowerBound,
                configuration.AnomalyDetectionUpperBound,
                configuration.CashRunwayWarningThresholdDays,
                configuration.CashRunwayCriticalThresholdDays);
    }

    private async Task<AlertRow?> LoadExistingLowCashAlertAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.Fingerprint == $"finance-cash-position:{companyId:N}:low-cash" &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new AlertRow(x.Id, x.Status))
            .FirstOrDefaultAsync(cancellationToken);

    private static string ResolveCashRiskLevel(
        decimal availableBalance,
        int? estimatedRunwayDays,
        FinancePolicyConfigurationDto policy,
        decimal? warningCashAmount,
        decimal? criticalCashAmount)
    {
        if (availableBalance <= 0m ||
            estimatedRunwayDays <= policy.CashRunwayCriticalThresholdDays ||
            criticalCashAmount.HasValue && availableBalance <= criticalCashAmount.Value)
        {
            return "critical";
        }

        if (estimatedRunwayDays <= policy.CashRunwayWarningThresholdDays ||
            warningCashAmount.HasValue && availableBalance <= warningCashAmount.Value)
        {
            return "medium";
        }

        return "low";
    }

    private async Task<TimeZoneInfo> ResolveCompanyTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var timezone = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == companyId)
            .Select(x => x.Timezone)
            .SingleOrDefaultAsync(cancellationToken);

        return ResolveTimezone(timezone);
    }

    private async Task<Dictionary<Guid, AllocationSummary>> LoadInvoiceAllocationSummariesAsync(
        Guid companyId,
        string paymentStatus,
        string paymentType,
        DateTime? paymentDateFromUtc,
        DateTime? paymentDateToExclusiveUtc,
        string sourceFilter,
        CancellationToken cancellationToken)
    {
        var rows = _sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType), companyId, sourceFilter);

        if (paymentDateFromUtc.HasValue)
        {
            rows = rows.Where(x => x.Payment.PaymentDate >= paymentDateFromUtc.Value);
        }

        if (paymentDateToExclusiveUtc.HasValue)
        {
            rows = rows.Where(x => x.Payment.PaymentDate < paymentDateToExclusiveUtc.Value);
        }

        return GroupAllocations(await rows
            .Select(x => new DocumentAllocationRow(x.InvoiceId!.Value, x.Id, x.AllocatedAmount))
            .ToListAsync(cancellationToken));
    }

    private async Task<Dictionary<Guid, AllocationSummary>> LoadBillAllocationSummariesAsync(
        Guid companyId,
        string paymentStatus,
        string paymentType,
        DateTime? paymentDateFromUtc,
        DateTime? paymentDateToExclusiveUtc,
        string sourceFilter,
        CancellationToken cancellationToken)
    {
        var rows = _sourcePolicy.ApplyPaymentAllocationFilter(_dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType), companyId, sourceFilter);

        if (paymentDateFromUtc.HasValue)
        {
            rows = rows.Where(x => x.Payment.PaymentDate >= paymentDateFromUtc.Value);
        }

        if (paymentDateToExclusiveUtc.HasValue)
        {
            rows = rows.Where(x => x.Payment.PaymentDate < paymentDateToExclusiveUtc.Value);
        }

        return GroupAllocations(await rows
            .Select(x => new DocumentAllocationRow(x.BillId!.Value, x.Id, x.AllocatedAmount))
            .ToListAsync(cancellationToken));
    }

    private async Task<IReadOnlyList<CashMovementQueryRow>> LoadCashMovementRowsAsync(
        Guid companyId,
        IReadOnlyList<Guid> cashAccountIds,
        DateTime startUtc,
        DateTime endUtc,
        string sourceFilter,
        CancellationToken cancellationToken)
    {
        if (cashAccountIds.Count == 0)
        {
            return [];
        }

        return await ApplySourceFilter(_dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                cashAccountIds.Contains(x.AccountId) &&
                x.TransactionUtc >= startUtc &&
                x.TransactionUtc < endUtc), companyId, sourceFilter, "voucher", "payment", "transaction")
            .Select(x => new CashMovementQueryRow(
                x.Id,
                NormalizeCategory(x.TransactionType),
                x.Amount,
                x.Currency,
                x.Description))
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyList<FinanceAgentMetricComponentDto> BuildCashMovementCategoryComponents(
        IReadOnlyList<CashMovementQueryRow> currentRows,
        IReadOnlyList<CashMovementQueryRow> comparisonRows,
        string currency)
    {
        var current = currentRows
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new AllocationSummary(
                    Math.Round(group.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero),
                    DistinctIds(group.Select(x => x.Id))),
                StringComparer.OrdinalIgnoreCase);
        var comparison = comparisonRows
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new AllocationSummary(
                    Math.Round(group.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero),
                    DistinctIds(group.Select(x => x.Id))),
                StringComparer.OrdinalIgnoreCase);

        return current.Keys
            .Concat(comparison.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                var currentSummary = current.TryGetValue(key, out var currentValue)
                    ? currentValue
                    : AllocationSummary.Empty;
                var comparisonSummary = comparison.TryGetValue(key, out var previousValue)
                    ? previousValue
                    : AllocationSummary.Empty;
                return new FinanceAgentMetricComponentDto(
                    key,
                    FormatCategoryLabel(key),
                    currentSummary.Amount,
                    comparisonSummary.Amount,
                    currentSummary.Amount - comparisonSummary.Amount,
                    currency,
                    DistinctIds(currentSummary.SourceRecordIds.Concat(comparisonSummary.SourceRecordIds)));
            })
            .Where(x => x.Delta != 0m)
            .ToArray();
    }

    private static string BuildCashPositionRationale(
        FinanceCashBalanceDto cashBalance,
        decimal averageMonthlyBurn,
        int? estimatedRunwayDays,
        FinancePolicyConfigurationDto policy,
        decimal? warningCashAmount,
        decimal? criticalCashAmount,
        string riskLevel)
    {
        var runwayText = estimatedRunwayDays.HasValue
            ? $"{estimatedRunwayDays.Value} day(s)"
            : "unavailable because average burn is zero";
        var thresholdText = warningCashAmount.HasValue && criticalCashAmount.HasValue
            ? $"Warning cash threshold is {warningCashAmount.Value:0.##} {cashBalance.Currency}; critical cash threshold is {criticalCashAmount.Value:0.##} {cashBalance.Currency}."
            : "Cash amount thresholds are unavailable because average burn is zero.";

        return $"Available cash is {cashBalance.Amount:0.##} {cashBalance.Currency}; average monthly burn is {averageMonthlyBurn:0.##} {cashBalance.Currency}; runway is {runwayText}. Warning runway threshold is {policy.CashRunwayWarningThresholdDays} day(s); critical runway threshold is {policy.CashRunwayCriticalThresholdDays} day(s). {thresholdText} Risk level is {riskLevel}.";
    }

    private static Dictionary<Guid, AllocationSummary> GroupAllocations(IReadOnlyList<DocumentAllocationRow> rows) =>
        rows.GroupBy(x => x.DocumentId)
            .ToDictionary(
                group => group.Key,
                group => new AllocationSummary(
                    Math.Round(group.Sum(x => x.AllocatedAmount), 2, MidpointRounding.AwayFromZero),
                    DistinctIds(group.Select(x => x.AllocationId))));

    private static IReadOnlyList<Guid> DistinctIds(IEnumerable<Guid> ids) =>
        ids.Where(x => x != Guid.Empty)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

    private static string ResolveAgingBucket(int daysOverdue) =>
        daysOverdue switch
        {
            <= 30 => "1-30",
            <= 60 => "31-60",
            <= 90 => "61-90",
            _ => "90+"
        };

    private static TimeZoneInfo ResolveTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeWindowResolution ResolveCurrentWeekWindow(DateTime asOfUtc, TimeZoneInfo zone)
    {
        var localAsOf = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, zone);
        var localDate = new DateTime(localAsOf.Year, localAsOf.Month, localAsOf.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var daysSinceMonday = ((int)localAsOf.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var localStart = localDate.AddDays(-daysSinceMonday);
        var localEnd = localStart.AddDays(7);
        return new TimeWindowResolution(
            TimeZoneInfo.ConvertTimeToUtc(localStart, zone),
            TimeZoneInfo.ConvertTimeToUtc(localEnd, zone),
            null,
            null);
    }

    private static TimeWindowResolution ResolveMonthToDateWindow(DateTime asOfUtc, TimeZoneInfo zone)
    {
        var localAsOf = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, zone);
        var monthStartLocal = new DateTime(localAsOf.Year, localAsOf.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var comparisonStartLocal = monthStartLocal.AddMonths(-1);
        var elapsedDays = (localAsOf.Date - monthStartLocal.Date).Days + 1;
        var comparisonEndLocal = comparisonStartLocal.AddDays(elapsedDays);

        return new TimeWindowResolution(
            TimeZoneInfo.ConvertTimeToUtc(monthStartLocal, zone),
            asOfUtc.AddTicks(1),
            TimeZoneInfo.ConvertTimeToUtc(comparisonStartLocal, zone),
            TimeZoneInfo.ConvertTimeToUtc(comparisonEndLocal, zone));
    }

    private static string FormatCategoryLabel(string category) =>
        string.IsNullOrWhiteSpace(category) ? "Uncategorized" : category.Replace("_", " ", StringComparison.Ordinal);

    private static string BuildCashMovementReason(FinanceAgentMetricComponentDto component)
    {
        var previous = component.PreviousValue ?? 0m;
        if (component.CurrentValue >= 0m && previous >= 0m)
        {
            return $"Cash inflows for {component.Label.ToLowerInvariant()} are down by {Math.Abs(component.Delta):0.00} {component.Currency} versus the prior comparable period.";
        }

        if (component.CurrentValue <= 0m && previous <= 0m)
        {
            return $"Cash outflows for {component.Label.ToLowerInvariant()} are up by {Math.Abs(component.Delta):0.00} {component.Currency} versus the prior comparable period.";
        }

        return $"Cash movement for {component.Label.ToLowerInvariant()} changed by {component.Delta:0.00} {component.Currency} versus the prior comparable period.";
    }

    private sealed record AlertRow(
        Guid Id,
        AlertStatus Status);

    private sealed record AccountRow(
        Guid Id,
        string Code,
        string Name,
        string AccountType,
        decimal OpeningBalance,
        string Currency);

    private sealed record BalanceRow(
        Guid AccountId,
        DateTime AsOfUtc,
        decimal Amount,
        string Currency);

    private sealed record TransactionBalanceRow(
        Guid AccountId,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string Description,
        string ExternalReference);

    private sealed record FinancePaymentSourceRow(
        Guid Id,
        Guid CompanyId,
        string PaymentType,
        decimal Amount,
        string Currency,
        DateTime PaymentDate,
        string Method,
        string Status,
        string CounterpartyReference,
        DateTime CreatedUtc,
        DateTime UpdatedUtc,
        string SourceType,
        string? ProviderKey,
        bool HasFortnoxReference);

    private sealed record FinanceTransactionRow(
        Guid Id,
        Guid AccountId,
        string AccountName,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? InvoiceId,
        Guid? BillId,
        Guid? DocumentId,
        DateTime TransactionUtc,
        string TransactionType,
        decimal Amount,
        string Currency,
        string Description,
        string ExternalReference,
        string SourceType,
        string? ProviderKey,
        bool HasFortnoxReference);

    private sealed record FortnoxVoucherAmountFallback(
        Guid CounterpartyId,
        string CounterpartyName,
        Guid? InvoiceId,
        Guid? BillId,
        decimal Amount,
        string Currency);

    private sealed record TransactionDocumentReviewState(
        string SettlementStatus,
        decimal PaidAmount,
        decimal Amount,
        string Currency,
        string? ProviderStatus);

    private sealed record FinanceInvoiceRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string InvoiceNumber,
        DateTime IssuedUtc,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string PostingStatus,
        string SettlementStatus,
        string DueStatus,
        string DocumentKind,
        string? ProviderStatus,
        string ProcessingStatus,
        Guid? DocumentId,
        string SourceType,
        string? ProviderKey,
        bool HasFortnoxReference);

    private sealed record FinanceCounterpartyRow(
        Guid Id,
        Guid CompanyId,
        string CounterpartyType,
        string Name,
        string? Email,
        string? PaymentTerms,
        string? TaxId,
        decimal? CreditLimit,
        string? PreferredPaymentMethod,
        string? DefaultAccountMapping,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);

    private sealed record FinanceBillRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string? CounterpartyDefaultAccountMapping,
        string BillNumber,
        DateTime ReceivedUtc,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string PostingStatus,
        string SettlementStatus,
        string DueStatus,
        string DocumentKind,
        string? ProviderStatus,
        string ProcessingStatus,
        Guid? DocumentId,
        string SourceType,
        string? ProviderKey,
        bool HasFortnoxReference);

    private sealed record FinanceLinkedDocumentRow(
        Guid Id,
        string Title,
        string OriginalFileName,
        string ContentType);

    private sealed record FinanceAmountRow(
        decimal Amount,
        string Currency);

    private sealed record FinanceExpenseRow(
        string TransactionType,
        decimal Amount,
        string Currency);

    private sealed record FinanceAnomalyTransactionRow(
        Guid Id,
        Guid? CounterpartyId,
        string? CounterpartyName,
        Guid? InvoiceId,
        Guid? BillId,
        DateTime TransactionUtc,
        string ExternalReference,
        decimal Amount,
        string Currency);

    private sealed record FinanceAnomalyInvoiceLinkRow(
        Guid Id,
        string InvoiceNumber,
        string CounterpartyName,
        DateTime IssuedUtc,
        decimal Amount,
        string Currency);

    private sealed record FinanceAnomalyBillLinkRow(
        Guid Id,
        string BillNumber,
        string CounterpartyName,
        DateTime ReceivedUtc,
        decimal Amount,
        string Currency);

    private sealed record FiscalPeriodRow(
        Guid FiscalPeriodId,
        Guid CompanyId,
        string Name,
        DateTime StartUtc,
        DateTime EndUtc,
        bool IsClosed);

    private sealed record StatementMappingRow(
        Guid FinanceAccountId,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        string AccountCode,
        string AccountName,
        decimal OpeningBalance,
        string Currency);

    private sealed record FinancialStatementSnapshotHeaderRow(
        Guid SnapshotId,
        Guid FiscalPeriodId,
        FinancialStatementType StatementType,
        int VersionNumber,
        string BalancesChecksum,
        DateTime GeneratedAtUtc,
        DateTime SourcePeriodStartUtc,
        DateTime SourcePeriodEndUtc,
        string Currency);

    private sealed record SnapshotStatementLineRow(
        Guid? FinanceAccountId,
        string LineCode,
        string LineName,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        decimal Amount,
        string Currency);

    private sealed record LedgerPostingRow(
        Guid FinanceAccountId,
        string AccountCode,
        string AccountName,
        decimal SignedAmount,
        string Currency);

    private sealed record ContributionAccountRow(
        Guid AccountId,
        string AccountCode,
        string AccountName,
        decimal OpeningBalance,
        string Currency);

    private sealed record ContributionRule(
        Guid FinanceAccountId,
        string LineCode,
        string LineName,
        string AccountCode,
        string AccountName,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        decimal ContributionFactor,
        decimal OpeningBalance,
        string Currency);

    private sealed record StatementLineResolution(
        FinancialStatementType StatementType,
        string SourceMode,
        FiscalPeriodRow Period,
        FinancialStatementSnapshotMetadataDto? Snapshot,
        string LineCode,
        string LineName,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        decimal Amount,
        string Currency,
        decimal OpeningBalanceAdjustment,
        IReadOnlyList<ContributionRule> ContributionRules);

    private sealed record DrilldownPostingRow(
        Guid LedgerEntryId,
        string EntryNumber,
        DateTime EntryUtc,
        string? EntryDescription,
        Guid LedgerEntryLineId,
        Guid FinanceAccountId,
        string AccountCode,
        string AccountName,
        decimal DebitAmount,
        decimal CreditAmount,
        string Currency,
        string? LineDescription);

    private sealed record LedgerPostingAmountRow(
        Guid FinanceAccountId,
        decimal SignedAmount);

    private sealed record SnapshotBalanceRow(
        Guid FinanceAccountId,
        string AccountCode,
        string AccountName,
        decimal BalanceAmount,
        string Currency);

    private sealed record LedgerBalanceAccountRow(
        Guid AccountId,
        string AccountCode,
        string AccountName,
        decimal OpeningBalance,
        string Currency);

    private sealed record LedgerStatementRow(
        Guid FinanceAccountId,
        string AccountCode,
        string AccountName,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        decimal BalanceAmount,
        string Currency);

    private static FinanceCounterpartyDto MapCounterparty(FinanceCounterpartyRow row) =>
        new(
            row.Id,
            row.CompanyId,
            row.CounterpartyType,
            row.Name,
            row.Email,
            row.PaymentTerms,
            row.TaxId,
            row.CreditLimit,
            row.PreferredPaymentMethod,
            row.DefaultAccountMapping,
            row.CreatedUtc,
            row.UpdatedUtc);

    private static FinanceSimulationEventReferenceDto? MapSimulationEventReference(SimulationEventRecord? record) =>
        record is null
            ? null
            : new FinanceSimulationEventReferenceDto(
                record.Id,
                record.EventType,
                record.SourceEntityType,
                record.SourceEntityId,
                record.SourceReference,
                record.ParentEventId,
                record.SimulationDateUtc,
                record.CashBefore,
                record.CashDelta,
                record.CashAfter);

    private static FinancePaymentDto MapPayment(Payment payment) =>
        new(payment.Id, payment.CompanyId, payment.PaymentType, payment.Amount, payment.Currency, payment.PaymentDate, payment.Method, payment.Status, payment.CounterpartyReference, payment.CreatedUtc, payment.UpdatedUtc, Array.Empty<NormalizedFinanceInsightDto>());

    private IQueryable<TEntity> ApplySourceFilter<TEntity>(
        IQueryable<TEntity> source,
        Guid companyId,
        string? sourceFilter,
        params string[] externalEntityTypes)
        where TEntity : class
        => _sourcePolicy.ApplyFilter(source, companyId, sourceFilter, externalEntityTypes);

    private async Task<HashSet<Guid>> LoadFortnoxReferenceIdsAsync(
        Guid companyId,
        IReadOnlyCollection<string> entityTypes,
        IEnumerable<Guid> internalRecordIds,
        CancellationToken cancellationToken)
        => await _sourcePolicy.LoadFortnoxReferenceIdsAsync(
            companyId,
            entityTypes,
            internalRecordIds,
            cancellationToken);

    private async Task<bool> HasFortnoxReferenceAsync(
        Guid companyId,
        IReadOnlyCollection<string> entityTypes,
        Guid internalRecordId,
        CancellationToken cancellationToken)
        => await _sourcePolicy.HasFortnoxReferenceAsync(
            companyId,
            entityTypes,
            internalRecordId,
            cancellationToken);

    private async Task<Dictionary<Guid, FortnoxVoucherAmountFallback>> LoadFortnoxVoucherAmountFallbacksAsync(
        Guid companyId,
        IEnumerable<FinanceTransactionRow> rows,
        IReadOnlySet<Guid> fortnoxReferenceIds,
        CancellationToken cancellationToken)
    {
        var candidateRows = rows
            .Where(row =>
                row.Amount == 0m &&
                string.Equals(row.TransactionType, "voucher", StringComparison.OrdinalIgnoreCase) &&
                IsFortnoxBacked(row, fortnoxReferenceIds))
            .Select(row => new
            {
                row.Id,
                DocumentNumber = ResolveVoucherDocumentNumber(row.Description)
            })
            .Where(row => row.DocumentNumber is not null)
            .ToArray();

        if (candidateRows.Length == 0)
        {
            return [];
        }

        var documentNumbers = candidateRows
            .Select(row => row.DocumentNumber!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var references = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reference =>
                reference.CompanyId == companyId &&
                reference.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                (reference.EntityType == "invoice" || reference.EntityType == "supplier_invoice") &&
                (documentNumbers.Contains(reference.ExternalId) ||
                    (reference.ExternalNumber != null && documentNumbers.Contains(reference.ExternalNumber))))
            .Select(reference => new
            {
                reference.EntityType,
                reference.InternalRecordId,
                reference.ExternalId,
                reference.ExternalNumber
            })
            .ToListAsync(cancellationToken);

        if (references.Count == 0)
        {
            return [];
        }

        var invoiceIds = references
            .Where(reference => reference.EntityType == "invoice")
            .Select(reference => reference.InternalRecordId)
            .Distinct()
            .ToArray();
        var billIds = references
            .Where(reference => reference.EntityType == "supplier_invoice")
            .Select(reference => reference.InternalRecordId)
            .Distinct()
            .ToArray();

        var invoiceRows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(invoice => invoice.CompanyId == companyId && invoiceIds.Contains(invoice.Id))
            .Select(invoice => new
            {
                invoice.Id,
                invoice.CounterpartyId,
                CounterpartyName = invoice.Counterparty.Name,
                invoice.Amount,
                invoice.Currency
            })
            .ToDictionaryAsync(invoice => invoice.Id, cancellationToken);

        var billRows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(bill => bill.CompanyId == companyId && billIds.Contains(bill.Id))
            .Select(bill => new
            {
                bill.Id,
                bill.CounterpartyId,
                CounterpartyName = bill.Counterparty.Name,
                bill.Amount,
                bill.Currency
            })
            .ToDictionaryAsync(bill => bill.Id, cancellationToken);

        var fallbackByDocumentNumber = new Dictionary<string, FortnoxVoucherAmountFallback>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            if (reference.EntityType == "invoice" && invoiceRows.TryGetValue(reference.InternalRecordId, out var invoice))
            {
                var fallback = new FortnoxVoucherAmountFallback(
                    invoice.CounterpartyId,
                    invoice.CounterpartyName,
                    invoice.Id,
                    null,
                    invoice.Amount,
                    invoice.Currency);
                AddVoucherFallback(fallbackByDocumentNumber, reference.ExternalId, fallback);
                AddVoucherFallback(fallbackByDocumentNumber, reference.ExternalNumber, fallback);
            }
            else if (reference.EntityType == "supplier_invoice" && billRows.TryGetValue(reference.InternalRecordId, out var bill))
            {
                var fallback = new FortnoxVoucherAmountFallback(
                    bill.CounterpartyId,
                    bill.CounterpartyName,
                    null,
                    bill.Id,
                    -Math.Abs(bill.Amount),
                    bill.Currency);
                AddVoucherFallback(fallbackByDocumentNumber, reference.ExternalId, fallback);
                AddVoucherFallback(fallbackByDocumentNumber, reference.ExternalNumber, fallback);
            }
        }

        var result = new Dictionary<Guid, FortnoxVoucherAmountFallback>();
        foreach (var candidate in candidateRows)
        {
            if (candidate.DocumentNumber is not null &&
                fallbackByDocumentNumber.TryGetValue(candidate.DocumentNumber, out var fallback))
            {
                result[candidate.Id] = fallback;
            }
        }

        return result;
    }

    private static void AddVoucherFallback(
        Dictionary<string, FortnoxVoucherAmountFallback> fallbacks,
        string? documentNumber,
        FortnoxVoucherAmountFallback fallback)
    {
        var normalized = NormalizeOptionalText(documentNumber);
        if (normalized is not null && !fallbacks.ContainsKey(normalized))
        {
            fallbacks.Add(normalized, fallback);
        }
    }

    private static bool IsFortnoxBacked(FinanceTransactionRow row, IReadOnlySet<Guid> fortnoxReferenceIds) =>
        fortnoxReferenceIds.Contains(row.Id) ||
        string.Equals(row.ProviderKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.SourceType, FinanceRecordSourceTypes.Fortnox, StringComparison.OrdinalIgnoreCase);

    private static decimal ResolveTransactionAmount(
        FinanceTransactionRow row,
        FortnoxVoucherAmountFallback? fallback) =>
        row.Amount == 0m && fallback is not null ? fallback.Amount : row.Amount;

    private static string? ResolveVoucherDocumentNumber(string? description)
    {
        var normalized = NormalizeOptionalText(description);
        if (normalized is null || normalized[^1] != ')')
        {
            return null;
        }

        var openingIndex = normalized.LastIndexOf('(');
        if (openingIndex < 0 || openingIndex == normalized.Length - 2)
        {
            return null;
        }

        return NormalizeOptionalText(normalized[(openingIndex + 1)..^1]);
    }

    private static string ResolveFinanceSource(string sourceType, string? providerKey, bool hasFortnoxReference) =>
        FinanceRecordSourcePolicy.ResolveSource(sourceType, providerKey, hasFortnoxReference);

    private static string NormalizeCounterpartyType(string value) =>
        FinanceCounterparty.NormalizeCounterpartyKind(value);

    private static IQueryable<FinanceCounterparty> FilterCounterpartiesByType(IQueryable<FinanceCounterparty> query, string expected) =>
        expected == "supplier"
            ? query.Where(x => x.CounterpartyType == "supplier" || x.CounterpartyType == "vendor")
            : query.Where(x => x.CounterpartyType == "customer");

    private static bool MatchesCounterpartyType(string actual, string expected) =>
        expected == "supplier"
            ? string.Equals(actual, "supplier", StringComparison.OrdinalIgnoreCase) || string.Equals(actual, "vendor", StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, "customer", StringComparison.OrdinalIgnoreCase);

    private sealed record AllocationSummary(
        decimal Amount,
        IReadOnlyList<Guid> SourceRecordIds)
    {
        public static AllocationSummary Empty { get; } = new(0m, []);
    }

    private sealed record DocumentAllocationRow(
        Guid DocumentId,
        Guid AllocationId,
        decimal AllocatedAmount);

    private sealed record AgentBillQueryRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string BillNumber,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record AgentInvoiceQueryRow(
        Guid Id,
        Guid CounterpartyId,
        string CounterpartyName,
        string InvoiceNumber,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        string SettlementStatus);

    private sealed record CashMovementQueryRow(
        Guid Id,
        string Category,
        decimal Amount,
        string Currency,
        string Description);

    private sealed record TimeWindowResolution(
        DateTime WindowStartUtc,
        DateTime WindowEndUtc,
        DateTime? ComparisonStartUtc,
        DateTime? ComparisonEndUtc);

    private static bool IsMissingPlanningSchemaTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (ContainsMissingPlanningTableName(current.Message) &&
                (current.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("invalid object name", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMissingPlanningTableName(string message) =>
        message.Contains("budgets", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("forecasts", StringComparison.OrdinalIgnoreCase);

    private static bool IsMissingLedgerReportingSchemaTable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (ContainsMissingLedgerReportingTableName(current.Message) &&
                (current.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("invalid object name", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsMissingLedgerReportingTableName(string message) =>
        message.Contains("finance_fiscal_periods", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("ledger_entries", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("ledger_entry_lines", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("ledger_entry_source_mappings", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("trial_balance_snapshots", StringComparison.OrdinalIgnoreCase);
}
