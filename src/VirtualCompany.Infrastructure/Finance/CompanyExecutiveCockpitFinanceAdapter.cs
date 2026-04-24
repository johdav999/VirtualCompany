using System.Globalization;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions InsightSerializerOptions = new(JsonSerializerDefaults.Web);
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
        var insightDashboard = await BuildInsightDashboardAsync(companyId, cancellationToken);

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
            insightDashboard.FinancialHealth,
            insightDashboard.TopActions,
            insightDashboard.FeedItems,
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

    private async Task<FinanceInsightDashboardProjection> BuildInsightDashboardAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var activeInsights = await _dbContext.FinanceAgentInsights
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == FinanceInsightStatus.Active)
            .ToListAsync(cancellationToken);

        if (activeInsights.Count == 0)
        {
            return new FinanceInsightDashboardProjection(
                new ExecutiveCockpitFinancialHealthDto(
                    "healthy",
                    "Financial health is stable",
                    "No active finance insights are currently persisted for this company.",
                    0,
                    0,
                    0,
                    null),
                [],
                []);
        }

        // Persisted condition keys are the stable identity for the underlying condition.
        var groupedInsights = activeInsights
            .GroupBy(BuildDashboardInsightGroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildDashboardInsightFeedItem(companyId, group))
            .OrderByDescending(x => GetSeverityRank(x.Severity))
            .ThenByDescending(x => x.LatestUpdatedUtc)
            .ThenBy(x => x.GroupKey, StringComparer.Ordinal)
            .ToArray();

        var criticalCount = activeInsights.Count(x => x.Severity == FinancialCheckSeverity.Critical);
        var highCount = activeInsights.Count(x => x.Severity == FinancialCheckSeverity.High);
        var overallStatus = activeInsights.Any(x => x.Severity == FinancialCheckSeverity.Critical)
            ? "critical"
            : activeInsights.Any(x => x.Severity == FinancialCheckSeverity.High)
                ? "high"
                : activeInsights.Any(x => x.Severity == FinancialCheckSeverity.Medium)
                    ? "medium"
                    : "low";

        var title = overallStatus switch
        {
            "critical" => "Immediate finance attention required",
            "high" => "Finance follow-up is building",
            "medium" => "Finance watch items are active",
            _ => "Finance posture is manageable"
        };

        var summary = $"{activeInsights.Count} active insight(s), {criticalCount} critical and {highCount} high.";
        var lastUpdatedUtc = activeInsights.Max(x => x.UpdatedUtc);

        return new FinanceInsightDashboardProjection(
            new ExecutiveCockpitFinancialHealthDto(
                overallStatus,
                title,
                summary,
                activeInsights.Count,
                criticalCount,
                highCount,
                lastUpdatedUtc),
            groupedInsights.Take(3).ToArray(),
            groupedInsights);
    }

    private static ExecutiveCockpitFinanceInsightFeedItemDto BuildDashboardInsightFeedItem(
        Guid companyId,
        IGrouping<string, FinanceAgentInsight> group)
    {
        var representative = group
            .OrderByDescending(x => GetSeverityRank(x.Severity))
            .ThenByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .First();

        var primaryEntity = BuildPrimaryEntityReference(representative);
        var relatedEntities = group
            .Select(BuildPrimaryEntityReference)
            .Where(x => x is not null)
            .DistinctBy(x => $"{x!.EntityType}:{x.EntityId}", StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Cast<FinanceInsightEntityReferenceDto>()
            .ToArray();

        var entitySummary = relatedEntities.Length switch
        {
            0 => "Company-level condition",
            1 => relatedEntities[0].DisplayName ?? $"{FormatToken(relatedEntities[0].EntityType)} {relatedEntities[0].EntityId}",
            _ => $"{group.Count()} related records"
        };

        var presentation = FinanceInsightPresentation.BuildDashboardText(
            representative.CheckCode,
            primaryEntity?.DisplayName ?? representative.EntityDisplayName,
            representative.Message,
            representative.Recommendation,
            group.Count(),
            relatedEntities.Length);

        return new ExecutiveCockpitFinanceInsightFeedItemDto(
            group.Key,
            representative.Severity.ToStorageValue(),
            presentation.Title,
            presentation.Summary,
            presentation.Recommendation,
            group.Count(),
            entitySummary,
            group.Max(x => x.UpdatedUtc),
            BuildInsightRoute(companyId, primaryEntity));
    }

    private static string BuildDashboardInsightGroupKey(FinanceAgentInsight insight) =>
        FinanceInsightPresentation.BuildDashboardGroupKey(
            insight.CheckCode,
            insight.ConditionKey,
            insight.EntityType,
            insight.EntityId);

    private static FinanceInsightEntityReferenceDto? BuildPrimaryEntityReference(FinanceAgentInsight insight)
    {
        var affectedEntities = DeserializeInsightEntities(insight.AffectedEntitiesJson);
        return affectedEntities.FirstOrDefault(x => x.IsPrimary)
            ?? new FinanceInsightEntityReferenceDto(insight.EntityType, insight.EntityId, insight.EntityDisplayName, true);
    }

    private static IReadOnlyList<FinanceInsightEntityReferenceDto> DeserializeInsightEntities(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<FinanceInsightEntityReferenceDto>>(payload, InsightSerializerOptions) ?? [];
    }

    private static string? BuildInsightRoute(Guid companyId, FinanceInsightEntityReferenceDto? entityReference)
    {
        if (entityReference is null)
        {
            return null;
        }

        if (!Guid.TryParse(entityReference.EntityId, out var entityId))
        {
            return null;
        }

        return entityReference.EntityType.Trim().ToLowerInvariant() switch
        {
            "invoice" or "finance_invoice" => $"/finance/invoices/{entityId:D}?companyId={companyId:D}",
            "bill" or "finance_bill" => $"/finance/bills/{entityId:D}?companyId={companyId:D}",
            "payment" or "finance_payment" => $"/finance/payments/{entityId:D}?companyId={companyId:D}",
            "transaction" or "finance_transaction" => $"/finance/transactions/{entityId:D}?companyId={companyId:D}",
            "anomaly" or "finance_anomaly" => $"/finance/anomalies/{entityId:D}?companyId={companyId:D}",
            _ => null
        };
    }

    private static int GetSeverityRank(FinancialCheckSeverity severity) =>
        severity switch
        {
            FinancialCheckSeverity.Critical => 4,
            FinancialCheckSeverity.High => 3,
            FinancialCheckSeverity.Medium => 2,
            FinancialCheckSeverity.Low => 1,
            _ => 0
        };

    private static int GetSeverityRank(string severity) =>
        severity.Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

    private static string FormatToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Record"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private sealed record FinanceInsightDashboardProjection(
        ExecutiveCockpitFinancialHealthDto FinancialHealth,
        IReadOnlyList<ExecutiveCockpitFinanceInsightFeedItemDto> TopActions,
        IReadOnlyList<ExecutiveCockpitFinanceInsightFeedItemDto> FeedItems);
}
