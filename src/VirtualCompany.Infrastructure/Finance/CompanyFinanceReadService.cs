using System.Globalization;
using System.Text.Json.Nodes;
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
    private const string MissingCounterpartyName = "Unknown counterparty";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ICompanyDocumentService? _documentService;
    private readonly IKnowledgeAccessPolicyEvaluator? _accessPolicyEvaluator;
    private readonly IFinanceSeedingStateService? _financeSeedingStateService;
    private readonly IDistributedCache? _insightSnapshotCache;
    private readonly TimeProvider? _timeProvider;

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
        IFinanceSeedingStateService? financeSeedingStateService = null,
        IDistributedCache? insightSnapshotCache = null,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _documentService = documentService;
        _accessPolicyEvaluator = accessPolicyEvaluator;
        _financeSeedingStateService = financeSeedingStateService;
        _insightSnapshotCache = insightSnapshotCache;
        _timeProvider = timeProvider;
    }

    public async Task<FinanceCashBalanceDto> GetCashBalanceAsync(
        GetFinanceCashBalanceQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;

        var accountBalances = await BuildAccountBalancesAsync(query.CompanyId, asOfUtc, cancellationToken);
        var cashAccounts = accountBalances
            .Where(x => IsCashAccount(x.AccountName, x.AccountCode))
            .ToList();

        if (cashAccounts.Count == 0)
        {
            cashAccounts = accountBalances
                .Where(x => string.Equals(x.AccountType, "asset", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (cashAccounts.Count == 0)
        {
            cashAccounts = accountBalances;
        }

        var currency = cashAccounts.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? cashAccounts[0].Currency
            : "MIXED";

        return new FinanceCashBalanceDto(
            query.CompanyId,
            asOfUtc,
            cashAccounts.Sum(x => x.Amount),
            currency,
            cashAccounts
                .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    public async Task<FinanceCashPositionDto> GetCashPositionAsync(
        GetFinanceCashPositionQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        var cashBalance = await GetCashBalanceAsync(
            new GetFinanceCashBalanceQuery(query.CompanyId, asOfUtc),
            cancellationToken);
        var averageMonthlyBurn = query.AverageMonthlyBurn ?? await CalculateAverageMonthlyBurnAsync(
            query.CompanyId,
            asOfUtc,
            query.BurnLookbackDays,
            cancellationToken);
        var policy = await LoadPolicyAsync(query.CompanyId, cancellationToken);

        var estimatedRunwayDays = averageMonthlyBurn <= 0m
            ? (int?)null
            : (int)Math.Floor(cashBalance.Amount / averageMonthlyBurn * 30m);
        if (estimatedRunwayDays < 0)
        {
            estimatedRunwayDays = 0;
        }

        var warningCashAmount = averageMonthlyBurn > 0m
            ? Math.Round(averageMonthlyBurn / 30m * policy.CashRunwayWarningThresholdDays, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var criticalCashAmount = averageMonthlyBurn > 0m
            ? Math.Round(averageMonthlyBurn / 30m * policy.CashRunwayCriticalThresholdDays, 2, MidpointRounding.AwayFromZero)
            : (decimal?)null;

        var riskLevel = ResolveCashRiskLevel(
            cashBalance.Amount,
            estimatedRunwayDays,
            policy,
            warningCashAmount,
            criticalCashAmount);
        var isLowCash = riskLevel is "critical" or "high" or "medium";
        var existingAlert = await LoadExistingLowCashAlertAsync(query.CompanyId, cancellationToken);
        var rationale = BuildCashPositionRationale(cashBalance, averageMonthlyBurn, estimatedRunwayDays, policy, warningCashAmount, criticalCashAmount, riskLevel);
        var workflowOutput = FinanceWorkflowOutputSchemas.Create(
            isLowCash ? "low_cash_position" : "cash_position_healthy",
            riskLevel,
            isLowCash ? "review_cash_plan" : "monitor",
            rationale,
            averageMonthlyBurn > 0m ? 0.86m : 0.72m,
            "cash_position_monitoring");

        return new FinanceCashPositionDto(
            query.CompanyId,
            asOfUtc,
            cashBalance.Amount,
            cashBalance.Currency,
            Math.Round(averageMonthlyBurn, 2, MidpointRounding.AwayFromZero),
            estimatedRunwayDays,
            new FinanceCashPositionThresholdsDto(
                policy.CashRunwayWarningThresholdDays,
                policy.CashRunwayCriticalThresholdDays,
                warningCashAmount,
                criticalCashAmount,
                cashBalance.Currency),
            new FinanceCashPositionAlertStateDto(
                isLowCash,
                riskLevel,
                false,
                false,
                existingAlert?.Id,
                existingAlert?.Status.ToStorageValue(),
                rationale),
            workflowOutput);
    }

    private async Task<decimal> CalculateAverageMonthlyBurnAsync(
        Guid companyId,
        DateTime asOfUtc,
        int burnLookbackDays,
        CancellationToken cancellationToken)
    {
        var lookbackDays = Math.Max(1, burnLookbackDays <= 0 ? 90 : burnLookbackDays);
        var startUtc = asOfUtc.AddDays(-lookbackDays);
        var totalBurn = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TransactionUtc >= startUtc && x.TransactionUtc <= asOfUtc && x.Amount < 0)
            .SumAsync(x => (decimal?)Math.Abs(x.Amount), cancellationToken) ?? 0m;
        var months = Math.Max(1m, lookbackDays / 30m);
        return totalBurn / months;
    }

    public async Task<ProfitAndLossReportDto> GetProfitAndLossReportAsync(
        GetFinanceProfitAndLossReportQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var period = await LoadFiscalPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var snapshot = period.IsClosed
            ? await LoadLatestFinancialStatementSnapshotAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken)
            : null;
        if (snapshot is not null)
        {
            var snapshotLines = await LoadFinancialStatementSnapshotLinesAsync(snapshot.SnapshotId, cancellationToken);
            return BuildProfitAndLossReport(period, snapshotLines, true, MapSnapshotMetadata(snapshot));
        }

        var snapshotRows = period.IsClosed
            ? await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken)
            : [];
        var rows = snapshotRows.Count > 0
            ? snapshotRows
            : await LoadLedgerStatementRowsForPeriodAsync(query.CompanyId, period, FinancialStatementType.ProfitAndLoss, cancellationToken);
        var lines = rows
            .Select(MapStatementLine)
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return BuildProfitAndLossReport(period, lines, snapshotRows.Count > 0, null);
    }

    public async Task<IReadOnlyList<FinancialStatementSnapshotSummaryDto>> ListFinancialStatementSnapshotsAsync(
        ListFinancialStatementSnapshotsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var snapshots = _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (query.FiscalPeriodId.HasValue)
        {
            snapshots = snapshots.Where(x => x.FiscalPeriodId == query.FiscalPeriodId.Value);
        }

        if (query.StatementType.HasValue)
        {
            snapshots = snapshots.Where(x => x.StatementType == query.StatementType.Value);
        }

        return await snapshots
            .OrderByDescending(x => x.GeneratedAtUtc)
            .ThenByDescending(x => x.VersionNumber)
            .Select(x => new FinancialStatementSnapshotSummaryDto(
                x.Id,
                x.CompanyId,
                x.FiscalPeriodId,
                x.FiscalPeriod.Name,
                x.StatementType.ToStorageValue(),
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency,
                x.Lines.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancialStatementSnapshotDetailDto?> GetFinancialStatementSnapshotAsync(
        GetFinancialStatementSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var summary = await _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.SnapshotId)
            .Select(x => new FinancialStatementSnapshotSummaryDto(
                x.Id,
                x.CompanyId,
                x.FiscalPeriodId,
                x.FiscalPeriod.Name,
                x.StatementType.ToStorageValue(),
                x.VersionNumber,
                x.BalancesChecksum,
                x.GeneratedAtUtc,
                x.SourcePeriodStartUtc,
                x.SourcePeriodEndUtc,
                x.Currency,
                x.Lines.Count))
            .SingleOrDefaultAsync(cancellationToken);

        return summary is null
            ? null
            : new FinancialStatementSnapshotDetailDto(summary.SnapshotId, summary, await LoadFinancialStatementSnapshotLinesAsync(summary.SnapshotId, cancellationToken));
    }

    public async Task<FinancialStatementDrilldownDto> GetFinancialStatementDrilldownAsync(
        GetFinancialStatementDrilldownQuery query,
        CancellationToken cancellationToken)
    {
        if (query.SnapshotId.HasValue && query.SnapshotVersionNumber.HasValue)
        {
            throw new ArgumentException("Specify either snapshotId or snapshotVersionNumber when requesting statement drilldown.", nameof(query));
        }

        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var resolution = query.SnapshotId.HasValue || query.SnapshotVersionNumber.HasValue
            ? await ResolveSnapshotStatementLineAsync(query, cancellationToken)
            : await ResolveLiveStatementLineAsync(
                query,
                await LoadFiscalPeriodAsync(
                    query.CompanyId,
                    query.FiscalPeriodId ?? throw new ArgumentException("FiscalPeriodId is required for live statement drilldown.", nameof(query)),
                    cancellationToken),
                cancellationToken);
        var drilldownEntries = await LoadDrilldownEntriesAsync(
            query.CompanyId,
            resolution.Period,
            resolution.StatementType,
            resolution.ContributionRules,
            cancellationToken);
        var journalLineTotal = Math.Round(drilldownEntries.Sum(x => x.TotalContributionAmount), 2, MidpointRounding.AwayFromZero);
        var reconciliationTotal = Math.Round(resolution.OpeningBalanceAdjustment + journalLineTotal, 2, MidpointRounding.AwayFromZero);
        var reconciliationDelta = Math.Round(resolution.Amount - reconciliationTotal, 2, MidpointRounding.AwayFromZero);

        return new FinancialStatementDrilldownDto(
            query.CompanyId,
            resolution.Period.FiscalPeriodId,
            resolution.Period.Name,
            resolution.StatementType.ToStorageValue(),
            resolution.SourceMode,
            resolution.Snapshot,
            new FinancialStatementDrilldownLineDto(
                resolution.LineCode,
                resolution.LineName,
                resolution.ReportSection.ToStorageValue(),
                resolution.LineClassification.ToStorageValue(),
                resolution.Amount,
                resolution.Currency),
            resolution.OpeningBalanceAdjustment,
            journalLineTotal,
            reconciliationTotal,
            reconciliationDelta,
            drilldownEntries);
    }

    public async Task<BalanceSheetReportDto> GetBalanceSheetReportAsync(
        GetFinanceBalanceSheetReportQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var period = await LoadFiscalPeriodAsync(query.CompanyId, query.FiscalPeriodId, cancellationToken);
        var snapshot = period.IsClosed
            ? await LoadLatestFinancialStatementSnapshotAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.BalanceSheet, cancellationToken)
            : null;
        if (snapshot is not null)
        {
            var snapshotLines = await LoadFinancialStatementSnapshotLinesAsync(snapshot.SnapshotId, cancellationToken);
            return BuildBalanceSheetReport(period, snapshotLines, true, MapSnapshotMetadata(snapshot));
        }

        var snapshotRows = period.IsClosed
            ? await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.BalanceSheet, cancellationToken)
            : [];
        var usedSnapshot = snapshotRows.Count > 0;
        var statementRows = usedSnapshot
            ? snapshotRows
            : await LoadBalanceSheetRowsAsync(query.CompanyId, period.EndUtc, cancellationToken);
        var currentEarnings = usedSnapshot
            ? CalculateProfitAndLossTotal(await LoadSnapshotStatementRowsAsync(query.CompanyId, query.FiscalPeriodId, FinancialStatementType.ProfitAndLoss, cancellationToken))
            : await CalculateCurrentEarningsAsync(query.CompanyId, period.EndUtc, cancellationToken);

        var lines = BuildLiveBalanceSheetLines(statementRows, currentEarnings);
        return BuildBalanceSheetReport(period, lines, usedSnapshot, null);
    }

    private static FinancialStatementSnapshotMetadataDto MapSnapshotMetadata(FinancialStatementSnapshotHeaderRow row) =>
        new(row.SnapshotId, row.VersionNumber, row.BalancesChecksum, row.GeneratedAtUtc, row.SourcePeriodStartUtc, row.SourcePeriodEndUtc, row.Currency);

    public async Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
        GetFinanceMonthlyProfitAndLossQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.Month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Month must be between 1 and 12.");
        }

        var startUtc = new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = startUtc.AddMonths(1);

        var invoices = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IssuedUtc >= startUtc && x.IssuedUtc < endUtc)
            .Select(x => new FinanceAmountRow(x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var expenseTransactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.TransactionUtc >= startUtc && x.TransactionUtc < endUtc && x.Amount < 0)
            .Select(x => new FinanceAmountRow(x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var revenue = invoices.Sum(x => x.Amount);
        var expenses = expenseTransactions.Sum(x => Math.Abs(x.Amount));
        var currency = ResolveCurrency(invoices.Concat(expenseTransactions));

        return new FinanceMonthlyProfitAndLossDto(
            query.CompanyId,
            query.Year,
            query.Month,
            startUtc,
            endUtc,
            revenue,
            expenses,
            revenue - expenses,
            currency);
    }

    public async Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
        GetFinanceExpenseBreakdownQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var startUtc = NormalizeUtc(query.StartUtc) ?? throw new ArgumentException("Start date is required.", nameof(query));
        var endUtc = NormalizeUtc(query.EndUtc) ?? throw new ArgumentException("End date is required.", nameof(query));
        if (startUtc >= endUtc)
        {
            throw new ArgumentException("Start date must be before end date.", nameof(query));
        }

        var expenses = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.TransactionUtc >= startUtc && x.TransactionUtc < endUtc && x.Amount < 0)
            .Select(x => new FinanceExpenseRow(x.TransactionType, x.Amount, x.Currency))
            .ToListAsync(cancellationToken);

        var categories = expenses
            .GroupBy(x => NormalizeCategory(x.TransactionType), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var currency = ResolveCurrency(group.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
                return new FinanceExpenseCategoryDto(
                    group.Key,
                    group.Sum(x => Math.Abs(x.Amount)),
                    currency);
            })
            .OrderByDescending(x => x.Amount)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FinanceExpenseBreakdownDto(
            query.CompanyId,
            startUtc,
            endUtc,
            categories.Sum(x => x.Amount),
            ResolveCurrency(expenses.Select(x => new FinanceAmountRow(x.Amount, x.Currency))),
            categories);
    }

    public async Task<IReadOnlyList<FinancePaymentDto>> GetPaymentsAsync(
        GetFinancePaymentsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var normalizedType = string.IsNullOrWhiteSpace(query.PaymentType)
            ? null
            : PaymentTypes.Normalize(query.PaymentType);
        if (normalizedType is not null && !PaymentTypes.IsSupported(normalizedType))
        {
            throw new ArgumentException($"Unsupported payment type '{query.PaymentType}'.", nameof(query));
        }

        var limit = NormalizeLimit(query.Limit);
        var payments = _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (normalizedType is not null)
        {
            payments = payments.Where(x => x.PaymentType == normalizedType);
        }

        return await payments
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(limit)
            .Select(x => new FinancePaymentDto(
                x.Id,
                x.CompanyId,
                x.PaymentType,
                x.Amount,
                x.Currency,
                x.PaymentDate,
                x.Method,
                x.Status,
                x.CounterpartyReference,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancePaymentDto?> GetPaymentDetailAsync(
        GetFinancePaymentDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(query));
        }

        return await _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.PaymentId)
            .Select(x => new FinancePaymentDto(
                x.Id,
                x.CompanyId,
                x.PaymentType,
                x.Amount,
                x.Currency,
                x.PaymentDate,
                x.Method,
                x.Status,
                x.CounterpartyReference,
                x.CreatedUtc,
                x.UpdatedUtc))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByPaymentAsync(
        GetFinancePaymentAllocationsByPaymentQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.PaymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(query));
        }

        var exists = await _dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.Id == query.PaymentId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance payment was not found.");
        }

        return await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.PaymentId == query.PaymentId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByInvoiceAsync(
        GetFinanceInvoiceAllocationsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.InvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(query));
        }

        var exists = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance invoice was not found.");
        }

        return await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.InvoiceId == query.InvoiceId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<FinancePaymentAllocationDto>> GetAllocationsByBillAsync(
        GetFinanceBillAllocationsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.BillId == Guid.Empty)
        {
            throw new ArgumentException("Bill id is required.", nameof(query));
        }

        var exists = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == query.CompanyId && x.Id == query.BillId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Finance bill was not found.");
        }

        return await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BillId == query.BillId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ContinueWith(task => (IReadOnlyList<FinancePaymentAllocationDto>)task.Result.Select(MapPaymentAllocation).ToList(), cancellationToken);
    }

    public async Task<FinancePaymentAllocationTraceDto?> GetAllocationTraceAsync(
        GetFinancePaymentAllocationTraceQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AllocationId == Guid.Empty)
        {
            throw new ArgumentException("Allocation id is required.", nameof(query));
        }

        var allocation = await _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSingleQuery()
            .Include(x => x.SourceSimulationEventRecord)
            .Include(x => x.PaymentSourceSimulationEventRecord)
            .Include(x => x.TargetSourceSimulationEventRecord)
            .Include(x => x.Payment)
                .ThenInclude(x => x.SourceSimulationEventRecord)
            .Include(x => x.Invoice)
                .ThenInclude(x => x!.SourceSimulationEventRecord)
            .Include(x => x.Bill)
                .ThenInclude(x => x!.SourceSimulationEventRecord)
            .SingleOrDefaultAsync(
                x => x.CompanyId == query.CompanyId && x.Id == query.AllocationId,
                cancellationToken);

        if (allocation is null)
        {
            return null;
        }

        var targetDocument = allocation.Invoice is not null
            ? new FinanceAllocationTargetDocumentDto(
                "invoice",
                allocation.Invoice.Id,
                allocation.Invoice.InvoiceNumber,
                allocation.Invoice.Amount,
                allocation.Invoice.Currency,
                allocation.Invoice.Status,
                allocation.Invoice.SourceSimulationEventRecordId)
            : new FinanceAllocationTargetDocumentDto(
                "bill",
                allocation.Bill!.Id,
                allocation.Bill.BillNumber,
                allocation.Bill.Amount,
                allocation.Bill.Currency,
                allocation.Bill.Status,
                allocation.Bill.SourceSimulationEventRecordId);

        return new FinancePaymentAllocationTraceDto(
            allocation.Id,
            allocation.CompanyId,
            MapPayment(allocation.Payment),
            targetDocument,
            MapSimulationEventReference(allocation.PaymentSourceSimulationEventRecord ?? allocation.Payment.SourceSimulationEventRecord),
            MapSimulationEventReference(allocation.TargetSourceSimulationEventRecord ?? allocation.Invoice?.SourceSimulationEventRecord ?? allocation.Bill?.SourceSimulationEventRecord),
            MapSimulationEventReference(allocation.SourceSimulationEventRecord ?? allocation.PaymentSourceSimulationEventRecord ?? allocation.Payment.SourceSimulationEventRecord));
    }

    public async Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
        GetFinanceTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var category = NormalizeOptionalText(query.Category);
        var flaggedState = NormalizeFlaggedState(query.FlaggedState);
        var limit = NormalizeLimit(query.Limit);

        var transactions = _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            transactions = transactions.Where(x => x.TransactionUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            transactions = transactions.Where(x => x.TransactionUtc < endUtc.Value);
        }

        var rows = await transactions
            .OrderByDescending(x => x.TransactionUtc)
            .ThenBy(x => x.ExternalReference)
            .Take(MaxLimit)
            .Select(x => new FinanceTransactionRow(
                x.Id,
                x.AccountId,
                x.Account.Name,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.DocumentId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference))
            .ToListAsync(cancellationToken);

        var anomalyLookup = await LoadTransactionAnomalyLookupAsync(
            query.CompanyId,
            rows.Select(x => x.Id),
            cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);

        return rows
            .Where(x => category is null || string.Equals(x.TransactionType, category, StringComparison.OrdinalIgnoreCase))
            .Where(x => MatchesFlaggedState(flaggedState, anomalyLookup.ContainsKey(x.Id)))
            .Take(limit)
            .Select(x => new FinanceTransactionDto(
                x.Id,
                x.AccountId,
                x.AccountName,
                x.CounterpartyId,
                x.CounterpartyName,
                x.InvoiceId,
                x.BillId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference,
                MapLinkedDocument(x.DocumentId, linkedDocuments),
                anomalyLookup.ContainsKey(x.Id),
                ResolveTransactionAnomalyState(anomalyLookup.GetValueOrDefault(x.Id))))
            .ToList();
    }

    public async Task<FinanceTransactionDetailDto?> GetTransactionDetailAsync(
        GetFinanceTransactionDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.TransactionId == Guid.Empty)
        {
            throw new ArgumentException("Transaction id is required.", nameof(query));
        }

        var row = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.TransactionId)
            .Select(x => new FinanceTransactionRow(
                x.Id,
                x.AccountId,
                x.Account.Name,
                x.CounterpartyId,
                x.Counterparty == null ? null : x.Counterparty.Name,
                x.InvoiceId,
                x.BillId,
                x.DocumentId,
                x.TransactionUtc,
                x.TransactionType,
                x.Amount,
                x.Currency,
                x.Description,
                x.ExternalReference))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var anomalyLookup = await LoadTransactionAnomalyLookupAsync(query.CompanyId, [row.Id], cancellationToken);
        var anomalies = anomalyLookup.GetValueOrDefault(row.Id) ?? [];
        var linkedDocuments = await LoadLinkedDocumentsAsync(query.CompanyId, [row.DocumentId], cancellationToken);
        var documentAccess = await BuildDocumentAccessAsync(query.CompanyId, row.DocumentId, linkedDocuments, cancellationToken);

        return new FinanceTransactionDetailDto(
            row.Id,
            row.AccountId,
            row.AccountName,
            row.CounterpartyId,
            row.CounterpartyName,
            row.InvoiceId,
            row.BillId,
            row.TransactionUtc,
            row.TransactionType,
            row.Amount,
            row.Currency,
            row.Description,
            row.ExternalReference,
            anomalies.Count > 0,
            ResolveTransactionAnomalyState(anomalies),
            anomalies.Select(x => NormalizeCategory(x.AnomalyType)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            BuildActionPermissions(),
            documentAccess);
    }

    public async Task<FinanceInvoiceDetailDto?> GetInvoiceDetailAsync(
        GetFinanceInvoiceDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.InvoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(query));
        }

        var row = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.InvoiceId)
            .Select(x => new FinanceInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.InvoiceNumber,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.DocumentId))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var linkedDocuments = await LoadLinkedDocumentsAsync(query.CompanyId, [row.DocumentId], cancellationToken);
        var documentAccess = await BuildDocumentAccessAsync(query.CompanyId, row.DocumentId, linkedDocuments, cancellationToken);

        return new FinanceInvoiceDetailDto(
            row.Id,
            row.CounterpartyId,
            row.CounterpartyName,
            row.InvoiceNumber,
            row.IssuedUtc,
            row.DueUtc,
            row.Amount,
            row.Currency,
            row.Status,
            null,
            BuildActionPermissions(),
            documentAccess);
    }

    private async Task<Dictionary<Guid, List<FinanceSeedAnomalyDto>>> LoadTransactionAnomalyLookupAsync(
        Guid companyId,
        IEnumerable<Guid> transactionIds,
        CancellationToken cancellationToken)
    {
        var ids = transactionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var lookup = new Dictionary<Guid, List<FinanceSeedAnomalyDto>>();
        foreach (var anomaly in anomalies.Select(MapSeedAnomaly))
        {
            foreach (var affectedRecordId in anomaly.AffectedRecordIds.Where(ids.Contains))
            {
                if (!lookup.TryGetValue(affectedRecordId, out var items))
                {
                    items = [];
                    lookup[affectedRecordId] = items;
                }

                items.Add(anomaly);
            }
        }

        return lookup;
    }

    public async Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
        GetFinanceInvoicesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var limit = NormalizeLimit(query.Limit);

        var invoices = _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            invoices = invoices.Where(x => x.IssuedUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            invoices = invoices.Where(x => x.IssuedUtc < endUtc.Value);
        }

        var rows = await invoices
            .OrderByDescending(x => x.IssuedUtc)
            .ThenBy(x => x.InvoiceNumber)
            .Take(limit)
            .Select(x => new FinanceInvoiceRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.InvoiceNumber,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.DocumentId))
            .ToListAsync(cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);

        return rows
            .Select(x => new FinanceInvoiceDto(
                x.Id,
                x.CounterpartyId,
                x.CounterpartyName,
                x.InvoiceNumber,
                x.IssuedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                MapLinkedDocument(x.DocumentId, linkedDocuments)))
            .ToList();
    }

    public async Task<IReadOnlyList<FinanceCounterpartyDto>> GetCounterpartiesAsync(
        GetFinanceCounterpartiesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var normalizedType = NormalizeCounterpartyType(query.CounterpartyType);
        var rows = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && MatchesCounterpartyType(x.CounterpartyType, normalizedType))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedUtc)
            .Take(NormalizeLimit(query.Limit))
            .Select(x => new FinanceCounterpartyRow(
                x.Id,
                x.CompanyId,
                NormalizeCounterpartyType(x.CounterpartyType),
                x.Name,
                x.Email,
                x.PaymentTerms,
                x.TaxId,
                x.CreditLimit,
                x.PreferredPaymentMethod,
                x.DefaultAccountMapping,
                x.CreatedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        return rows.Select(MapCounterparty).ToList();
    }

    public async Task<FinanceCounterpartyDto?> GetCounterpartyAsync(
        GetFinanceCounterpartyQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        var normalizedType = NormalizeCounterpartyType(query.CounterpartyType);
        var row = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.Id == query.CounterpartyId && MatchesCounterpartyType(x.CounterpartyType, normalizedType))
            .Select(x => new FinanceCounterpartyRow(x.Id, x.CompanyId, NormalizeCounterpartyType(x.CounterpartyType), x.Name, x.Email, x.PaymentTerms, x.TaxId, x.CreditLimit, x.PreferredPaymentMethod, x.DefaultAccountMapping, x.CreatedUtc, x.UpdatedUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : MapCounterparty(row);
    }

    public async Task<IReadOnlyList<FinanceSeedAnomalyDto>> GetSeedAnomaliesAsync(
        GetFinanceSeedAnomaliesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var limit = NormalizeLimit(query.Limit);
        var anomalyType = string.IsNullOrWhiteSpace(query.AnomalyType)
            ? null
            : query.AnomalyType.Trim();

        if (query.AffectedRecordId == Guid.Empty)
        {
            throw new ArgumentException("Affected record id cannot be empty.", nameof(query));
        }

        var anomalies = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var filtered = anomalies
            .Select(MapSeedAnomaly)
            .Where(x =>
                anomalyType is null ||
                string.Equals(x.AnomalyType, anomalyType, StringComparison.OrdinalIgnoreCase));

        if (query.AffectedRecordId is Guid affectedRecordId)
        {
            filtered = filtered.Where(x => x.AffectedRecordIds.Contains(affectedRecordId));
        }

        return filtered
            .Take(limit)
            .ToList();
    }

    public async Task<FinanceSeedAnomalyDto?> GetSeedAnomalyByIdAsync(
        GetFinanceSeedAnomalyByIdQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AnomalyId == Guid.Empty)
        {
            throw new ArgumentException("Anomaly id is required.", nameof(query));
        }

        var anomaly = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId && x.Id == query.AnomalyId, cancellationToken);

        return anomaly is null
            ? null
            : MapSeedAnomaly(anomaly);
    }

    public async Task<FinanceAnomalyWorkbenchResultDto> GetAnomalyWorkbenchAsync(
        GetFinanceAnomalyWorkbenchQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        var normalizedType = NormalizeFilterToken(query.AnomalyType);
        var normalizedStatus = NormalizeFilterToken(query.Status);
        var normalizedSupplier = NormalizeOptionalText(query.Supplier);
        var confidenceMin = NormalizeConfidence(query.ConfidenceMin);
        var confidenceMax = NormalizeConfidence(query.ConfidenceMax);
        var dateFromUtc = NormalizeUtc(query.DateFromUtc);
        var dateToUtc = NormalizeUtc(query.DateToUtc);
        var (page, pageSize) = NormalizePagination(query.Page, query.PageSize);

        if (confidenceMin.HasValue && confidenceMax.HasValue && confidenceMin > confidenceMax)
        {
            (confidenceMin, confidenceMax) = (confidenceMax, confidenceMin);
        }

        var alerts = await LoadFinanceAnomalyAlertsAsync(query.CompanyId, cancellationToken);
        var transactions = await LoadFinanceAnomalyTransactionsAsync(query.CompanyId, alerts, cancellationToken);
        var invoices = await LoadFinanceAnomalyInvoicesAsync(query.CompanyId, transactions.Values, cancellationToken);
        var bills = await LoadFinanceAnomalyBillsAsync(query.CompanyId, transactions.Values, cancellationToken);
        var tasksByCorrelationId = await LoadFinanceAnomalyTasksByCorrelationIdAsync(query.CompanyId, alerts, cancellationToken);

        var filtered = alerts
            .Select(alert => MapFinanceAnomalyWorkbenchItem(alert, transactions, invoices, bills, tasksByCorrelationId))
            .Where(item => item is not null)
            .Cast<FinanceAnomalyWorkbenchItemDto>()
            .Where(item => normalizedType is null || string.Equals(NormalizeFilterToken(item.AnomalyType), normalizedType, StringComparison.OrdinalIgnoreCase))
            .Where(item => normalizedStatus is null || string.Equals(NormalizeFilterToken(item.Status), normalizedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(item => confidenceMin is null || item.Confidence >= confidenceMin.Value)
            .Where(item => confidenceMax is null || item.Confidence <= confidenceMax.Value)
            .Where(item =>
                normalizedSupplier is null ||
                (!string.IsNullOrWhiteSpace(item.SupplierName) &&
                 item.SupplierName.Contains(normalizedSupplier, StringComparison.OrdinalIgnoreCase)))
            .Where(item => dateFromUtc is null || item.DetectedAtUtc >= dateFromUtc.Value)
            .Where(item => dateToUtc is null || item.DetectedAtUtc < dateToUtc.Value)
            .OrderByDescending(item => item.DetectedAtUtc)
            .ThenBy(item => item.AffectedRecordReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id)
            .ToList();

        var totalCount = filtered.Count;
        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new FinanceAnomalyWorkbenchResultDto(totalCount, page, pageSize, items);
    }

    public async Task<FinanceAnomalyDetailDto?> GetAnomalyDetailAsync(
        GetFinanceAnomalyDetailQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        if (query.AnomalyId == Guid.Empty)
        {
            throw new ArgumentException("Anomaly id is required.", nameof(query));
        }

        var alert = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == query.CompanyId &&
                     x.Id == query.AnomalyId &&
                     x.Type == AlertType.Anomaly &&
                     x.CorrelationId.StartsWith("fin-anom:"),
                cancellationToken);

        if (alert is null)
        {
            return null;
        }

        var transactions = await LoadFinanceAnomalyTransactionsAsync(query.CompanyId, [alert], cancellationToken);
        var invoices = await LoadFinanceAnomalyInvoicesAsync(query.CompanyId, transactions.Values, cancellationToken);
        var bills = await LoadFinanceAnomalyBillsAsync(query.CompanyId, transactions.Values, cancellationToken);
        var tasksByCorrelationId = await LoadFinanceAnomalyTasksByCorrelationIdAsync(query.CompanyId, [alert], cancellationToken);

        var transactionId = ExtractGuid(alert.Evidence, "transactionId");
        var transaction = transactionId.HasValue ? transactions.GetValueOrDefault(transactionId.Value) : null;
        var invoice = transaction?.InvoiceId is Guid invoiceId ? invoices.GetValueOrDefault(invoiceId) : null;
        var bill = transaction?.BillId is Guid billId ? bills.GetValueOrDefault(billId) : null;
        var tasks = tasksByCorrelationId.GetValueOrDefault(alert.CorrelationId) ?? [];
        var latestTask = tasks
            .OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .FirstOrDefault();

        var anomalyType = ExtractString(alert.Metadata, "anomalyType")
            ?? ExtractString(alert.Evidence, "anomalyType")
            ?? "unknown";
        var confidence = ExtractDecimal(alert.Metadata, "confidence")
            ?? ExtractDecimal(alert.Evidence, "confidence")
            ?? 0m;
        var supplierName = NormalizeOptionalText(
            invoice?.CounterpartyName
            ?? bill?.CounterpartyName
            ?? transaction?.CounterpartyName
            ?? ExtractString(alert.Evidence, "counterpartyName"));
        var affectedRecord = transaction is null
            ? null
            // The detail card shows the primary transaction summary while related record links expose drill-down targets.
            : new FinanceAnomalyRelatedRecordDto(
                transaction.Id,
                transaction.ExternalReference,
                transaction.TransactionUtc,
                transaction.Amount,
                transaction.Currency,
                transaction.CounterpartyName);

        return new FinanceAnomalyDetailDto(
            alert.Id,
            anomalyType,
            latestTask?.Status.ToStorageValue() ?? alert.Status.ToStorageValue(),
            confidence,
            supplierName,
            alert.Summary,
            ExtractString(alert.Metadata, "recommendedAction")
                ?? ExtractString(alert.Evidence, "recommendedAction")
                ?? string.Empty,
            alert.LastDetectedUtc ?? alert.CreatedUtc,
            BuildDeduplicationMetadata(alert),
            affectedRecord,
            invoice?.Id,
            invoice?.InvoiceNumber,
            bill?.Id,
            bill?.BillNumber,
            BuildFinanceAnomalyRecordLinks(transaction, invoice, bill),
            tasks
                .OrderByDescending(x => x.UpdatedUtc)
                .ThenByDescending(x => x.CreatedUtc)
                .Select(x => new FinanceAnomalyFollowUpTaskDto(
                    x.Id,
                    x.Title,
                    x.Status.ToStorageValue(),
                    x.CreatedUtc,
                    x.DueUtc,
                    x.UpdatedUtc))
                .ToList());
    }

    public async Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var startUtc = NormalizeUtc(query.StartUtc);
        var endUtc = NormalizeUtc(query.EndUtc);
        var limit = NormalizeLimit(query.Limit);

        var bills = _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId);

        if (startUtc is not null)
        {
            bills = bills.Where(x => x.ReceivedUtc >= startUtc.Value);
        }

        if (endUtc is not null)
        {
            bills = bills.Where(x => x.ReceivedUtc < endUtc.Value);
        }

        var rows = await bills
            .OrderByDescending(x => x.ReceivedUtc)
            .ThenBy(x => x.BillNumber)
            .Take(limit)
            .Select(x => new FinanceBillRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty == null ? MissingCounterpartyName : x.Counterparty.Name,
                x.BillNumber,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.DocumentId))
            .ToListAsync(cancellationToken);

        var linkedDocuments = await LoadLinkedDocumentsAsync(
            query.CompanyId,
            rows.Select(x => x.DocumentId),
            cancellationToken);

        return rows
            .Select(x => new FinanceBillDto(
                x.Id,
                x.CounterpartyId,
                x.CounterpartyName,
                x.BillNumber,
                x.ReceivedUtc,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                MapLinkedDocument(x.DocumentId, linkedDocuments)))
            .ToList();
    }

    public async Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);
        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        return await BuildAccountBalancesAsync(query.CompanyId, asOfUtc, cancellationToken);
    }

    public async Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
        GetFinanceAgentQueryQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        await EnsureFinanceInitializedAsync(query.CompanyId, cancellationToken);

        if (!FinanceAgentQueryRouting.TryResolveIntent(query.QueryText, out var intent))
        {
            throw new ArgumentException(
                $"Unsupported finance agent query '{query.QueryText}'. Supported queries: {string.Join(", ", FinanceAgentQueryRouting.SupportedPhrases)}.",
                nameof(query));
        }

        var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? DateTime.UtcNow;
        var timeZone = await ResolveCompanyTimeZoneAsync(query.CompanyId, cancellationToken);

        return intent switch
        {
            var value when string.Equals(value, FinanceAgentQueryIntents.WhatShouldIPayThisWeek, StringComparison.Ordinal) =>
                await ResolveWhatShouldIPayThisWeekAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken),
            var value when string.Equals(value, FinanceAgentQueryIntents.WhichCustomersAreOverdue, StringComparison.Ordinal) =>
                await ResolveWhichCustomersAreOverdueAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken),
            _ => await ResolveWhyIsCashDownThisMonthAsync(query.CompanyId, query.QueryText, asOfUtc, timeZone, cancellationToken)
        };
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhatShouldIPayThisWeekAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var weekWindow = ResolveCurrentWeekWindow(asOfUtc, timeZone);
        var completedAllocations = await LoadBillAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Outgoing,
            null,
            asOfUtc.AddTicks(1),
            cancellationToken);
        var scheduledAllocations = await LoadBillAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Pending,
            PaymentTypes.Outgoing,
            weekWindow.WindowStartUtc,
            weekWindow.WindowEndUtc,
            cancellationToken);

        var rows = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AgentBillQueryRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.BillNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var localAsOfDate = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, timeZone).Date;
        var items = rows
            .Where(x => IsIncludedPayable(x.Status, x.SettlementStatus))
            .Select(row =>
            {
                var completed = completedAllocations.GetValueOrDefault(row.Id);
                var scheduled = scheduledAllocations.GetValueOrDefault(row.Id);
                var remaining = CalculateRemainingBalance(row.Amount, completed.Amount);
                if (remaining <= 0m || row.DueUtc >= weekWindow.WindowEndUtc)
                {
                    return null;
                }

                var localDueDate = TimeZoneInfo.ConvertTimeFromUtc(row.DueUtc, timeZone).Date;
                var daysOverdue = row.DueUtc < asOfUtc
                    ? Math.Max(0, (localAsOfDate - localDueDate).Days)
                    : (int?)null;
                var sourceRecordIds = DistinctIds([row.Id, .. completed.SourceRecordIds, .. scheduled.SourceRecordIds]);
                return new FinanceAgentQueryItemDto(
                    row.Id,
                    "bill",
                    row.CounterpartyId,
                    row.CounterpartyName,
                    row.BillNumber,
                    row.DueUtc,
                    remaining,
                    row.Currency,
                    daysOverdue.HasValue
                        ? $"Overdue by {daysOverdue.Value} day(s)."
                        : "Due within the current company week.",
                    0,
                    daysOverdue,
                    null,
                    sourceRecordIds,
                    [
                        new FinanceAgentMetricComponentDto("original_amount", "Original amount", row.Amount, null, row.Amount, row.Currency, [row.Id]),
                        new FinanceAgentMetricComponentDto("completed_outgoing_allocations", "Completed outgoing allocations", completed.Amount, null, completed.Amount, row.Currency, completed.SourceRecordIds),
                        new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", remaining, null, remaining, row.Currency, [row.Id, .. completed.SourceRecordIds]),
                        new FinanceAgentMetricComponentDto("scheduled_outgoing_this_week", "Scheduled outgoing this week", scheduled.Amount, null, scheduled.Amount, row.Currency, scheduled.SourceRecordIds)
                    ]);
            })
            .Where(x => x is not null)
            .OrderBy(x => x!.DaysOverdue.HasValue ? 0 : 1)
            .ThenBy(x => x!.DueUtc)
            .ThenByDescending(x => x!.Amount)
            .ThenBy(x => x!.RecordId)
            .Select((item, index) => item! with { SortOrder = index + 1 })
            .ToArray();

        var currency = ResolveCurrency(items.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
        var sourceRecordIds = DistinctIds(items.SelectMany(x => x.SourceRecordIds));
        var totalAmount = items.Sum(x => x.Amount);
        var overdueCount = items.Count(x => x.DaysOverdue.HasValue);

        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhatShouldIPayThisWeek,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            $"Selected {items.Length} payable item(s) totaling {totalAmount:0.00} {currency} for the current company week; {overdueCount} item(s) are already overdue.",
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(
                asOfUtc,
                weekWindow.WindowStartUtc,
                weekWindow.WindowEndUtc,
                null,
                null,
                timeZone.Id),
            items,
            [
                new FinanceAgentMetricComponentDto("recommended_payables_total", "Recommended payables total", totalAmount, null, totalAmount, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("recommended_payables_count", "Recommended payables count", items.Length, null, items.Length, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("overdue_payables_count", "Overdue payables count", overdueCount, null, overdueCount, currency, DistinctIds(items.Where(x => x.DaysOverdue.HasValue).SelectMany(x => x.SourceRecordIds)))
            ],
            sourceRecordIds);
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhichCustomersAreOverdueAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var completedAllocations = await LoadInvoiceAllocationSummariesAsync(
            companyId,
            PaymentStatuses.Completed,
            PaymentTypes.Incoming,
            null,
            asOfUtc.AddTicks(1),
            cancellationToken);

        var rows = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AgentInvoiceQueryRow(
                x.Id,
                x.CounterpartyId,
                x.Counterparty.Name,
                x.InvoiceNumber,
                x.DueUtc,
                x.Amount,
                x.Currency,
                x.Status,
                x.SettlementStatus))
            .ToListAsync(cancellationToken);

        var localAsOfDate = TimeZoneInfo.ConvertTimeFromUtc(asOfUtc, timeZone).Date;
        var items = rows
            .Where(x => IsIncludedReceivable(x.Status, x.SettlementStatus) && x.DueUtc < asOfUtc)
            .Select(row =>
            {
                var completed = completedAllocations.GetValueOrDefault(row.Id);
                var remaining = CalculateRemainingBalance(row.Amount, completed.Amount);
                if (remaining <= 0m)
                {
                    return null;
                }

                var daysOverdue = Math.Max(0, (localAsOfDate - TimeZoneInfo.ConvertTimeFromUtc(row.DueUtc, timeZone).Date).Days);
                var agingBucket = ResolveAgingBucket(daysOverdue);
                var sourceRecordIds = DistinctIds([row.Id, .. completed.SourceRecordIds]);
                return new FinanceAgentQueryItemDto(
                    row.Id,
                    "invoice",
                    row.CounterpartyId,
                    row.CounterpartyName,
                    row.InvoiceNumber,
                    row.DueUtc,
                    remaining,
                    row.Currency,
                    $"{agingBucket} overdue.",
                    0,
                    daysOverdue,
                    agingBucket,
                    sourceRecordIds,
                    [
                        new FinanceAgentMetricComponentDto("original_amount", "Original amount", row.Amount, null, row.Amount, row.Currency, [row.Id]),
                        new FinanceAgentMetricComponentDto("completed_incoming_allocations", "Completed incoming allocations", completed.Amount, null, completed.Amount, row.Currency, completed.SourceRecordIds),
                        new FinanceAgentMetricComponentDto("remaining_balance", "Remaining balance", remaining, null, remaining, row.Currency, [row.Id, .. completed.SourceRecordIds])
                    ]);
            })
            .Where(x => x is not null)
            .OrderByDescending(x => x!.DaysOverdue)
            .ThenByDescending(x => x!.Amount)
            .ThenBy(x => x!.RecordId)
            .Select((item, index) => item! with { SortOrder = index + 1 })
            .ToArray();

        var currency = ResolveCurrency(items.Select(x => new FinanceAmountRow(x.Amount, x.Currency)));
        var sourceRecordIds = DistinctIds(items.SelectMany(x => x.SourceRecordIds));
        var totalOutstanding = items.Sum(x => x.Amount);

        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhichCustomersAreOverdue,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            $"Selected {items.Length} overdue receivable item(s) totaling {totalOutstanding:0.00} {currency}.",
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(asOfUtc, null, asOfUtc, null, null, timeZone.Id),
            items,
            [
                new FinanceAgentMetricComponentDto("overdue_receivables_total", "Overdue receivables total", totalOutstanding, null, totalOutstanding, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("overdue_receivables_count", "Overdue receivables count", items.Length, null, items.Length, currency, sourceRecordIds)
            ],
            sourceRecordIds);
    }

    private async Task<FinanceAgentQueryResultDto> ResolveWhyIsCashDownThisMonthAsync(
        Guid companyId,
        string queryText,
        DateTime asOfUtc,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var monthWindow = ResolveMonthToDateWindow(asOfUtc, timeZone);
        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new AccountRow(
                x.Id,
                x.Code,
                x.Name,
                x.AccountType,
                x.OpeningBalance,
                x.Currency))
            .ToListAsync(cancellationToken);

        var cashAccountIds = accounts
            .Where(x => IsCashAccount(x.Name, x.Code) || string.Equals(x.AccountType, "asset", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id)
            .ToArray();

        var currentRows = await LoadCashMovementRowsAsync(companyId, cashAccountIds, monthWindow.WindowStartUtc, monthWindow.WindowEndUtc, cancellationToken);
        var comparisonRows = await LoadCashMovementRowsAsync(companyId, cashAccountIds, monthWindow.ComparisonStartUtc!.Value, monthWindow.ComparisonEndUtc!.Value, cancellationToken);

        var currency = ResolveCurrency(
            currentRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))
                .Concat(comparisonRows.Select(x => new FinanceAmountRow(x.Amount, x.Currency))));

        var netCurrent = Math.Round(currentRows.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var netPrevious = Math.Round(comparisonRows.Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var inflowsCurrent = Math.Round(currentRows.Where(x => x.Amount > 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var inflowsPrevious = Math.Round(comparisonRows.Where(x => x.Amount > 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var outflowsCurrent = Math.Round(-currentRows.Where(x => x.Amount < 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);
        var outflowsPrevious = Math.Round(-comparisonRows.Where(x => x.Amount < 0m).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero);

        var categoryComponents = BuildCashMovementCategoryComponents(currentRows, comparisonRows, currency)
            .OrderBy(x => x.Delta)
            .ThenBy(x => x.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        var items = categoryComponents
            .Where(x => x.Delta < 0m)
            .Select((component, index) => new FinanceAgentQueryItemDto(
                null,
                "cash_movement_category",
                null,
                null,
                component.ComponentKey,
                null,
                component.Delta,
                component.Currency,
                BuildCashMovementReason(component),
                index + 1,
                null,
                null,
                component.SourceRecordIds,
                [component]))
            .ToArray();

        var headlineDrivers = categoryComponents
            .Where(x => x.Delta < 0m)
            .Take(2)
            .Select(x => $"{x.Label} ({x.Delta:0.00} {x.Currency})")
            .ToArray();
        var netDelta = Math.Round(netCurrent - netPrevious, 2, MidpointRounding.AwayFromZero);
        var summary = headlineDrivers.Length == 0
            ? $"Net cash movement changed by {netDelta:0.00} {currency} month-to-date versus the same number of days in the prior month."
            : $"Net cash movement is down by {Math.Abs(Math.Min(netDelta, 0m)):0.00} {currency} month-to-date versus the same number of days in the prior month. Largest drivers: {string.Join(" and ", headlineDrivers)}.";

        var sourceRecordIds = DistinctIds(currentRows.Select(x => x.Id).Concat(comparisonRows.Select(x => x.Id)));
        return new FinanceAgentQueryResultDto(
            companyId,
            FinanceAgentQueryIntents.WhyIsCashDownThisMonth,
            FinanceAgentQueryRouting.NormalizeQueryText(queryText),
            summary,
            currency,
            asOfUtc,
            new FinanceAgentQueryPeriodDto(
                asOfUtc,
                monthWindow.WindowStartUtc,
                monthWindow.WindowEndUtc,
                monthWindow.ComparisonStartUtc,
                monthWindow.ComparisonEndUtc,
                timeZone.Id),
            items,
            new[]
            {
                new FinanceAgentMetricComponentDto("net_cash_movement", "Net cash movement", netCurrent, netPrevious, netDelta, currency, sourceRecordIds),
                new FinanceAgentMetricComponentDto("cash_inflows", "Cash inflows", inflowsCurrent, inflowsPrevious, inflowsCurrent - inflowsPrevious, currency, DistinctIds(currentRows.Where(x => x.Amount > 0m).Select(x => x.Id).Concat(comparisonRows.Where(x => x.Amount > 0m).Select(x => x.Id)))),
                new FinanceAgentMetricComponentDto("cash_outflows", "Cash outflows", -outflowsCurrent, -outflowsPrevious, outflowsPrevious - outflowsCurrent, currency, DistinctIds(currentRows.Where(x => x.Amount < 0m).Select(x => x.Id).Concat(comparisonRows.Where(x => x.Amount < 0m).Select(x => x.Id))))
            }.Concat(categoryComponents).ToArray(),
            sourceRecordIds);
    }

    private async Task EnsureFinanceInitializedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_financeSeedingStateService is null)
        {
            return;
        }

        var state = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(companyId, cancellationToken);
        if (state.State != FinanceSeedingState.Seeded)
        {
            throw new FinanceNotInitializedException(companyId, "Finance data has not been initialized for this company. Generate finance data before requesting finance records.");
        }
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
            .Select(x => new TransactionBalanceRow(x.AccountId, x.TransactionUtc, x.Amount))
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

                var postedAmount = accountTransactions.Sum(transaction => transaction.Amount);
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
                "none",
                "No linked document is attached.",
                false,
                null);
        }

        if (!linkedDocuments.TryGetValue(resolvedDocumentId, out var document))
        {
            return new FinanceLinkedDocumentAccessDto(
                "restricted",
                "The linked document could not be loaded for the current access context.",
                false,
                null);
        }

        return new FinanceLinkedDocumentAccessDto(
            "available",
            "Linked document is available.",
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
                invoice?.CounterpartyName
                ?? bill?.CounterpartyName
                ?? transaction?.CounterpartyName
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

    private static bool IsCashAccount(string name, string code) =>
        name.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        code.StartsWith("10", StringComparison.OrdinalIgnoreCase);

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
        var normalizedPageSize = pageSize switch
        {
            25 or 50 or 100 => pageSize,
            _ => 50
        };

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
        CancellationToken cancellationToken)
    {
        var rows = _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.InvoiceId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType);

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
        CancellationToken cancellationToken)
    {
        var rows = _dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.BillId.HasValue &&
                x.Payment.Status == paymentStatus &&
                x.Payment.PaymentType == paymentType);

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
        CancellationToken cancellationToken)
    {
        if (cashAccountIds.Count == 0)
        {
            return [];
        }

        return await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                cashAccountIds.Contains(x.AccountId) &&
                x.TransactionUtc >= startUtc &&
                x.TransactionUtc < endUtc)
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
                var currentValue = current.GetValueOrDefault(key).Amount;
                var previousValue = comparison.GetValueOrDefault(key).Amount;
                return new FinanceAgentMetricComponentDto(key, FormatCategoryLabel(key), currentValue, previousValue, currentValue - previousValue, currency, DistinctIds(current.GetValueOrDefault(key).SourceRecordIds.Concat(comparison.GetValueOrDefault(key).SourceRecordIds)));
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
        decimal Amount);

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
        string ExternalReference);

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
        Guid? DocumentId);

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
        string BillNumber,
        DateTime ReceivedUtc,
        DateTime DueUtc,
        decimal Amount,
        string Currency,
        string Status,
        Guid? DocumentId);

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
        new(payment.Id, payment.CompanyId, payment.PaymentType, payment.Amount, payment.Currency, payment.PaymentDate, payment.Method, payment.Status, payment.CounterpartyReference, payment.CreatedUtc, payment.UpdatedUtc);

    private static string NormalizeCounterpartyType(string value) =>
        FinanceCounterparty.NormalizeCounterpartyKind(value);

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
}
