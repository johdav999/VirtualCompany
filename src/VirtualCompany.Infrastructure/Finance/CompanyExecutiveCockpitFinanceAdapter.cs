using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyExecutiveCockpitFinanceAdapter : IExecutiveCockpitFinanceAdapter
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceReadService _financeReadService;
    private readonly IFinanceCashPositionWorkflowService _cashPositionWorkflowService;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IAuthorizationService _authorizationService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    public CompanyExecutiveCockpitFinanceAdapter(
        VirtualCompanyDbContext dbContext,
        IFinanceReadService financeReadService,
        IFinanceCashPositionWorkflowService cashPositionWorkflowService,
        ICompanyMembershipContextResolver membershipContextResolver,
        IAuthorizationService authorizationService,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _financeReadService = financeReadService;
        _cashPositionWorkflowService = cashPositionWorkflowService;
        _membershipContextResolver = membershipContextResolver;
        _authorizationService = authorizationService;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    public async Task<ExecutiveCockpitFinanceDto?> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null || !await CanViewFinanceAsync(companyId, membership, cancellationToken))
        {
            return null;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var current = await _cashPositionWorkflowService.EvaluateAsync(
            new EvaluateFinanceCashPositionWorkflowCommand(companyId),
            cancellationToken);
        var previous = await _financeReadService.GetCashPositionAsync(
            new GetFinanceCashPositionQuery(companyId, nowUtc.AddDays(-7)),
            cancellationToken);
        var trendAmount = Math.Round(current.AvailableBalance - previous.AvailableBalance, 2, MidpointRounding.AwayFromZero);
        var trendDirection = trendAmount switch
        {
            > 0m => ExecutiveCockpitTrendDirections.Up,
            < 0m => ExecutiveCockpitTrendDirections.Down,
            _ => ExecutiveCockpitTrendDirections.Flat
        };
        var runwayStatus = ExecutiveCockpitFinanceRunwayStatusClassifier.Classify(
            current.EstimatedRunwayDays,
            current.Thresholds.WarningRunwayDays,
            current.Thresholds.CriticalRunwayDays);
        var lowCashAlert = current.AlertState.AlertId.HasValue
            ? await GetAlertDetailAsync(companyId, current.AlertState.AlertId.Value, cancellationToken)
            : null;

        return new ExecutiveCockpitFinanceDto(
            new ExecutiveCockpitFinanceCashWidgetDto(
                current.AvailableBalance,
                current.Currency,
                FormatCurrency(current.AvailableBalance, current.Currency),
                trendDirection,
                trendAmount,
                FormatTrend(trendAmount, current.Currency, trendDirection),
                current.AsOfUtc,
                BuildCashPositionRoute(companyId)),
            new ExecutiveCockpitFinanceRunwayWidgetDto(
                current.EstimatedRunwayDays,
                current.EstimatedRunwayDays.HasValue ? Math.Round(current.EstimatedRunwayDays.Value / 30m, 1, MidpointRounding.AwayFromZero) : null,
                current.EstimatedRunwayDays.HasValue ? $"{current.EstimatedRunwayDays.Value} days" : "Runway unavailable",
                runwayStatus,
                runwayStatus.ToString(),
                BuildCashPositionRoute(companyId)),
            lowCashAlert,
            await BuildActionsAsync(companyId, membership.MembershipRole.ToStorageValue(), cancellationToken),
            BuildDeepLinks(companyId));
    }

    public async Task<ExecutiveCockpitFinanceAlertDetailDto?> GetAlertDetailAsync(
        Guid companyId,
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null || !await CanViewFinanceAsync(companyId, membership, cancellationToken))
        {
            throw new UnauthorizedAccessException("Finance alert detail requires finance access for the selected company.");
        }

        var alert = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == alertId, cancellationToken);
        if (alert is null ||
            string.IsNullOrWhiteSpace(alert.Fingerprint) ||
            !alert.Fingerprint.StartsWith("finance-cash-position:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ExecutiveCockpitFinanceAlertDetailDto(
            alert.Id,
            alert.Summary,
            alert.Severity.ToStorageValue(),
            alert.Status.ToStorageValue(),
            BuildContributingFactors(alert),
            await BuildActionsAsync(companyId, membership.MembershipRole.ToStorageValue(), cancellationToken),
            BuildDeepLinks(companyId),
            BuildAlertDetailRoute(companyId, alert.Id));
    }

    private async Task<IReadOnlyList<ExecutiveCockpitFinanceActionDto>> BuildActionsAsync(
        Guid companyId,
        string membershipRole,
        CancellationToken cancellationToken)
    {
        var canView = FinanceAccess.CanView(membershipRole) &&
                      await AuthorizeAsync(companyId, CompanyPolicies.FinanceView, cancellationToken);
        if (!canView)
        {
            return [];
        }

        var canApprove = FinanceAccess.CanApproveInvoices(membershipRole) &&
                         await AuthorizeAsync(companyId, CompanyPolicies.FinanceApproval, cancellationToken);
        var invoices = await _financeReadService.GetInvoicesAsync(
            new GetFinanceInvoicesQuery(companyId, null, null, 10),
            cancellationToken);
        var reviewInvoice = invoices.FirstOrDefault(invoice =>
            invoice.Status.Contains("open", StringComparison.OrdinalIgnoreCase) ||
            invoice.Status.Contains("pending", StringComparison.OrdinalIgnoreCase));

        var flaggedTransactions = await _financeReadService.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(companyId, null, null, 10, null, "flagged"),
            cancellationToken);
        var anomalyTransaction = flaggedTransactions.FirstOrDefault();

        var actions = new List<ExecutiveCockpitFinanceActionDto>(4);
        if (canApprove && reviewInvoice is not null)
        {
            actions.Add(new ExecutiveCockpitFinanceActionDto(
                "review_invoice",
                "Review invoice",
                true,
                BuildInvoiceReviewRoute(companyId, reviewInvoice.Id),
                $"/internal/companies/{companyId:D}/finance/invoices/{reviewInvoice.Id:D}/review-workflow",
                "POST",
                reviewInvoice.Id));
        }

        if (anomalyTransaction is not null)
        {
            actions.Add(new ExecutiveCockpitFinanceActionDto(
                "inspect_anomaly",
                "Inspect anomaly",
                true,
                BuildAnomalyWorkbenchRoute(companyId),
                $"/internal/companies/{companyId:D}/finance/transactions/{anomalyTransaction.Id:D}/anomaly-evaluation",
                "POST",
                anomalyTransaction.Id));
        }

        actions.Add(new ExecutiveCockpitFinanceActionDto(
            "view_cash_position",
            "View cash position",
            true,
            BuildCashPositionRoute(companyId),
            $"/internal/companies/{companyId:D}/finance/cash-position/evaluation",
            "POST",
            null));

        actions.Add(new ExecutiveCockpitFinanceActionDto(
            "open_finance_summary",
            "Open finance summary",
            true,
            BuildFinanceSummaryRoute(companyId),
            null,
            "GET",
            null));

        return actions;
    }

    private async Task<bool> CanViewFinanceAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        CancellationToken cancellationToken) =>
        FinanceAccess.CanView(membership.MembershipRole.ToStorageValue()) &&
        await AuthorizeAsync(companyId, CompanyPolicies.FinanceView, cancellationToken);

    private async Task<bool> AuthorizeAsync(
        Guid companyId,
        string policyName,
        CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        return (await _authorizationService.AuthorizeAsync(principal, companyId, policyName)).Succeeded;
    }

    private static IReadOnlyList<string> BuildContributingFactors(Alert alert)
    {
        var factors = new List<string>();
        AddCurrencyFactor(factors, alert.Evidence, "availableBalance", "currency", "Available cash");
        AddCurrencyFactor(factors, alert.Evidence, "averageMonthlyBurn", "currency", "Average monthly burn");

        if (TryGetInt(alert.Evidence, "estimatedRunwayDays", out var runwayDays))
        {
            factors.Add($"Estimated runway is {runwayDays} day(s).");
        }

        if (TryGetInt(alert.Evidence, "warningRunwayDays", out var warningRunway))
        {
            factors.Add($"Warning threshold starts at {warningRunway} day(s) of runway.");
        }

        if (TryGetInt(alert.Evidence, "criticalRunwayDays", out var criticalRunway))
        {
            factors.Add($"Critical threshold starts at {criticalRunway} day(s) of runway.");
        }

        var recommendation = TryGetString(alert.Metadata, "recommendedAction") ?? TryGetString(alert.Evidence, "recommendedAction");
        if (!string.IsNullOrWhiteSpace(recommendation))
        {
            factors.Add($"Recommended action: {recommendation}.");
        }

        return factors;
    }

    private static void AddCurrencyFactor(
        List<string> factors,
        IReadOnlyDictionary<string, JsonNode?> values,
        string amountKey,
        string currencyKey,
        string label)
    {
        if (TryGetDecimal(values, amountKey, out var amount))
        {
            factors.Add($"{label} is {FormatCurrency(amount, TryGetString(values, currencyKey) ?? "USD")}.");
        }
    }

    private static IReadOnlyList<ExecutiveCockpitDeepLinkDto> BuildDeepLinks(Guid companyId) =>
    [
        new ExecutiveCockpitDeepLinkDto("finance_workspace", "Finance workspace", $"/finance?companyId={companyId:D}"),
        new ExecutiveCockpitDeepLinkDto("finance_summary", "Finance summary", $"/finance/monthly-summary?companyId={companyId:D}"),
        new ExecutiveCockpitDeepLinkDto("anomaly_workbench", "Anomaly workbench", $"/finance/anomalies?companyId={companyId:D}"),
        new ExecutiveCockpitDeepLinkDto("cash_detail", "Cash detail", $"/finance/cash-position?companyId={companyId:D}")
    ];

    private static string BuildCashPositionRoute(Guid companyId) => $"/finance/cash-position?companyId={companyId:D}";
    private static string BuildAnomalyWorkbenchRoute(Guid companyId) => $"/finance/anomalies?companyId={companyId:D}";
    private static string BuildFinanceSummaryRoute(Guid companyId) => $"/finance/monthly-summary?companyId={companyId:D}";
    private static string BuildInvoiceReviewRoute(Guid companyId, Guid invoiceId) => $"/finance/reviews/{invoiceId:D}?companyId={companyId:D}";
    private static string BuildAlertDetailRoute(Guid companyId, Guid alertId) => $"/finance/alerts/{alertId:D}?companyId={companyId:D}";

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatTrend(decimal amount, string currency, string trendDirection) =>
        trendDirection == ExecutiveCockpitTrendDirections.Flat
            ? "No cash movement vs previous 7 days"
            : $"{(amount > 0m ? "+" : string.Empty)}{FormatCurrency(amount, currency)} vs previous 7 days";

    private static string? TryGetString(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        values is not null && values.TryGetValue(key, out var node) ? node?.ToString()?.Trim() : null;

    private static bool TryGetInt(IReadOnlyDictionary<string, JsonNode?>? values, string key, out int value) =>
        int.TryParse(TryGetString(values, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryGetDecimal(IReadOnlyDictionary<string, JsonNode?>? values, string key, out decimal value) =>
        decimal.TryParse(TryGetString(values, key), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
}
