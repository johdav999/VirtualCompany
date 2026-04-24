namespace VirtualCompany.Application.Finance;

public static class FinanceInsightPresentation
{
    public static string BuildDashboardGroupKey(
        string checkCode,
        string? conditionKey,
        string entityType,
        string entityId)
    {
        var normalizedCheckCode = NormalizeToken(checkCode);
        var normalizedConditionKey = NormalizeOptionalToken(conditionKey);
        if (!string.IsNullOrWhiteSpace(normalizedConditionKey))
        {
            return $"{normalizedCheckCode}|{normalizedConditionKey}";
        }

        return $"{normalizedCheckCode}|{NormalizeEntityType(entityType)}|{NormalizeToken(entityId)}";
    }

    public static IReadOnlySet<string> BuildEntityTypeCandidates(string entityType)
    {
        var normalized = NormalizeEntityType(entityType);

        return normalized switch
        {
            "invoice" or "finance_invoice" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "invoice",
                "finance_invoice"
            },
            "bill" or "finance_bill" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "bill",
                "finance_bill"
            },
            "payment" or "finance_payment" => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "payment",
                "finance_payment"
            },
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                normalized
            }
        };
    }

    public static FinanceInsightDashboardText BuildDashboardText(
        string checkCode,
        string? entityDisplayName,
        string rawMessage,
        string rawRecommendation,
        int occurrenceCount,
        int relatedEntityCount)
    {
        var normalizedCheckCode = NormalizeToken(checkCode);
        var entityLabel = string.IsNullOrWhiteSpace(entityDisplayName) ? "this record" : entityDisplayName.Trim();
        var occurrenceSuffix = occurrenceCount > 1
            ? $" Seen on {occurrenceCount} items."
            : relatedEntityCount > 1
                ? $" Affects {relatedEntityCount} related records."
                : string.Empty;

        return normalizedCheckCode switch
        {
            "overdue_receivables" => new FinanceInsightDashboardText(
                "Collections need attention",
                occurrenceCount > 1
                    ? $"Overdue receivables are building across related customer records.{occurrenceSuffix}"
                    : $"{entityLabel} has overdue receivables that need follow-up.{occurrenceSuffix}",
                "Review overdue invoices and assign collections follow-up."),
            "payables_pressure" => new FinanceInsightDashboardText(
                "Supplier payments need attention",
                occurrenceCount > 1
                    ? $"Supplier payment pressure is affecting multiple payables.{occurrenceSuffix}"
                    : $"{entityLabel} needs payment follow-up.{occurrenceSuffix}",
                "Review due dates and schedule the next supplier payment."),
            "cash_risk" => new FinanceInsightDashboardText(
                "Cash position needs review",
                occurrenceCount > 1
                    ? $"Cash pressure is showing up across related records.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Review cash commitments and near-term collections."),
            "transaction_anomaly" => new FinanceInsightDashboardText(
                occurrenceCount > 1 ? "Transactions need review" : "Transaction needs review",
                occurrenceCount > 1
                    ? $"Related transaction exceptions are still open.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Review the flagged transaction activity and confirm the next action."),
            "top_expense_concentration" => new FinanceInsightDashboardText(
                "Expense concentration is elevated",
                occurrenceCount > 1
                    ? $"Expense concentration is elevated across multiple spending signals.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Review the dominant expense category or supplier concentration and confirm it is intentional."),
            "revenue_trend" => new FinanceInsightDashboardText(
                "Revenue trend softened",
                occurrenceCount > 1
                    ? $"Revenue trend signals are deteriorating across the current dashboard scope.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Review recent invoice activity and collections to understand the short-term revenue decline."),
            "burn_runway_risk" => new FinanceInsightDashboardText(
                "Cash runway needs attention",
                occurrenceCount > 1
                    ? $"Burn and runway signals indicate near-term cash pressure.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Review burn drivers, collections timing, and payable sequencing to protect runway."),
            "overdue_customer_concentration" => new FinanceInsightDashboardText(
                "Customer concentration risk is elevated",
                occurrenceCount > 1
                    ? $"Overdue balances are concentrated across a small number of customers.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Prioritize collections outreach for the largest overdue customer exposures."),
            "near_term_liquidity_pressure" => new FinanceInsightDashboardText(
                "Near-term liquidity is under pressure",
                occurrenceCount > 1
                    ? $"Upcoming or overdue payables are creating liquidity pressure.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Sequence due-soon obligations against available cash and expected collections."),
            "approval_needed_finance_events" => new FinanceInsightDashboardText(
                "Finance approvals are pending",
                occurrenceCount > 1
                    ? $"Multiple finance approval tasks are still pending or escalated.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Clear the oldest finance approvals first so threshold-driven work does not stall."),
            "threshold_breach_finance_events" => new FinanceInsightDashboardText(
                "Threshold breaches need verification",
                occurrenceCount > 1
                    ? $"Several recent finance events crossed configured approval thresholds.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Verify threshold-breaching items have the correct approval and audit trail coverage."),
            "summary_consistency_anomaly" => new FinanceInsightDashboardText(
                "Finance summary does not fully reconcile",
                occurrenceCount > 1
                    ? $"Finance summary consistency checks found multiple reconciliation mismatches.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Inspect ledger, cash, and statement summary inputs before relying on downstream metrics."),
            "sparse_data_coverage" => new FinanceInsightDashboardText(
                "Analytics confidence is limited",
                occurrenceCount > 1
                    ? $"Some analytics are operating with sparse finance coverage.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Treat trend-oriented signals as directional until more finance history is available."),
            "forecast_gap" => new FinanceInsightDashboardText(
                "Forecast coverage is missing",
                occurrenceCount > 1
                    ? $"Forecast coverage is missing across the current planning period.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Add or refresh forecasts so planning and variance views stay actionable."),
            "budget_gap" => new FinanceInsightDashboardText(
                "Budget coverage is missing",
                occurrenceCount > 1
                    ? $"Budget coverage is missing across the current planning period.{occurrenceSuffix}"
                    : $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                "Create a budget baseline for the current period so actual-vs-target analysis is grounded."),
            _ => new FinanceInsightDashboardText(
                FinancialCheckDefinitions.Resolve(checkCode).Name,
                $"{NormalizeSentence(rawMessage)}{occurrenceSuffix}",
                NormalizeSentence(rawRecommendation))
        };
    }

    private static string NormalizeEntityType(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('-', '_').ToLowerInvariant();

    private static string NormalizeToken(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    private static string? NormalizeOptionalToken(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeToken(value);

    private static string NormalizeSentence(string value)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".", StringComparison.Ordinal) ||
               trimmed.EndsWith("!", StringComparison.Ordinal) ||
               trimmed.EndsWith("?", StringComparison.Ordinal)
            ? trimmed
            : $"{trimmed}.";
    }
}

public sealed record FinanceInsightDashboardText(
    string Title,
    string Summary,
    string Recommendation);
