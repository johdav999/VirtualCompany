using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceReadService : IFinanceReadService
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const string MissingCounterpartyName = "Unknown counterparty";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ICompanyDocumentService? _documentService;
    private readonly IKnowledgeAccessPolicyEvaluator? _accessPolicyEvaluator;
    private readonly IFinanceSeedingStateService? _financeSeedingStateService;

    public CompanyFinanceReadService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, null, null)
    {
    }

    public CompanyFinanceReadService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
        : this(dbContext, companyContextAccessor, null, null, null)
    {
    }

    public CompanyFinanceReadService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        ICompanyDocumentService? documentService,
        IKnowledgeAccessPolicyEvaluator? accessPolicyEvaluator,
        IFinanceSeedingStateService? financeSeedingStateService = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _documentService = documentService;
        _accessPolicyEvaluator = accessPolicyEvaluator;
        _financeSeedingStateService = financeSeedingStateService;
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

        if (linkedDocuments.TryGetValue(resolvedDocumentId, out var linkedDocument))
        {
            return new FinanceLinkedDocumentAccessDto(
                "available",
                "Linked document available.",
                true,
                new FinanceLinkedDocumentDto(
                    linkedDocument.Id,
                    linkedDocument.Title,
                    linkedDocument.OriginalFileName,
                    linkedDocument.ContentType));
        }

        var documentExists = await _dbContext.CompanyKnowledgeDocuments
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == resolvedDocumentId, cancellationToken);

        return documentExists
            ? new FinanceLinkedDocumentAccessDto(
                "inaccessible",
                "Linked document unavailable or you do not have access.",
                false,
                null)
            : new FinanceLinkedDocumentAccessDto(
                "missing",
                "Linked document is no longer available.",
                false,
                null);
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

        if (_companyContextAccessor is not null &&
            (!_companyContextAccessor.IsResolved ||
             _companyContextAccessor.CompanyId is not Guid currentCompanyId ||
             currentCompanyId != companyId))
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
}
