using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class FinancePage : FinancePageBase, IDisposable
{
    [Inject] protected FinanceApiClient FinanceApiClient { get; set; } = default!;

    private CancellationTokenSource? _overviewLoadCts;
    private int _overviewLoadVersion;

    protected FinanceOverviewViewModel? Overview { get; private set; }
    protected bool IsOverviewLoading { get; private set; }
    protected bool IsOverviewEmpty { get; private set; }
    protected string? OverviewErrorMessage { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        CancelOverviewLoad();
        if (IsLoading || !AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            ResetOverview();
            return;
        }

        await LoadOverviewAsync(companyId);
    }

    protected Task ReloadOverviewAsync() =>
        AccessState.CompanyId is Guid companyId
            ? LoadOverviewAsync(companyId)
            : Task.CompletedTask;

    private async Task LoadOverviewAsync(Guid companyId)
    {
        IsOverviewLoading = true;
        ResetOverviewState();
        await InvokeAsync(StateHasChanged);

        var loadVersion = Interlocked.Increment(ref _overviewLoadVersion);
        var cancellationTokenSource = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _overviewLoadCts, cancellationTokenSource);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            var cancellationToken = cancellationTokenSource.Token;
            var cashTask = FinanceApiClient.GetCashPositionAsync(companyId, cancellationToken: cancellationToken);
            var monthlyTask = FinanceApiClient.GetMonthlySummaryAsync(companyId, cancellationToken: cancellationToken);
            var billsTask = FinanceApiClient.GetBillsAsync(companyId, 50, cancellationToken);
            var billInboxTask = FinanceApiClient.GetBillInboxAsync(companyId, 50, cancellationToken);
            var invoicesTask = FinanceApiClient.GetInvoicesAsync(companyId, limit: 50, cancellationToken: cancellationToken);
            var invoiceReviewsTask = FinanceApiClient.GetInvoiceReviewsAsync(companyId, limit: 50, cancellationToken: cancellationToken);
            var paymentsTask = FinanceApiClient.GetPaymentsAsync(companyId, limit: 50, cancellationToken: cancellationToken);
            var transactionsTask = FinanceApiClient.GetTransactionsAsync(companyId, limit: 50, cancellationToken: cancellationToken);
            var anomaliesTask = FinanceApiClient.GetAnomalyWorkbenchAsync(companyId, pageSize: 25, cancellationToken: cancellationToken);

            await Task.WhenAll(cashTask, monthlyTask, billsTask, billInboxTask, invoicesTask, invoiceReviewsTask, paymentsTask, transactionsTask, anomaliesTask);

            if (loadVersion != _overviewLoadVersion || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            Overview = BuildOverview(
                companyId,
                cashTask.Result,
                monthlyTask.Result,
                billsTask.Result,
                billInboxTask.Result,
                invoicesTask.Result,
                invoiceReviewsTask.Result,
                paymentsTask.Result,
                transactionsTask.Result,
                anomaliesTask.Result);
            IsOverviewEmpty = Overview.HasNoFinanceActivity;
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (loadVersion != _overviewLoadVersion || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            OverviewErrorMessage = ex.Message;
        }
        finally
        {
            if (loadVersion == _overviewLoadVersion)
            {
                IsOverviewLoading = false;
                await InvokeAsync(StateHasChanged);
            }

            if (ReferenceEquals(_overviewLoadCts, cancellationTokenSource))
            {
                _overviewLoadCts = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    private FinanceOverviewViewModel BuildOverview(
        Guid companyId,
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        IReadOnlyList<FinanceBillResponse> bills,
        IReadOnlyList<FinanceBillInboxRowResponse> billInbox,
        IReadOnlyList<FinanceInvoiceResponse> invoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> payments,
        IReadOnlyList<FinanceTransactionResponse> transactions,
        FinanceAnomalyWorkbenchResponse anomalies)
    {
        var currency = ResolveCurrency(cash, monthly, invoices, bills, payments, transactions);
        var now = DateTime.UtcNow;
        var overdueInvoices = invoices
            .Where(invoice => invoice.DueUtc.Date < now.Date && !IsClosedStatus(invoice.Status))
            .OrderBy(invoice => invoice.DueUtc)
            .ToArray();
        var billsAwaitingApproval = billInbox
            .Where(item => IsActionStatus(item.Status) || item.ValidationWarningCount > 0 || item.DuplicateWarningCount > 0)
            .OrderByDescending(item => item.ValidationWarningCount + item.DuplicateWarningCount)
            .ThenBy(item => item.DetectedUtc)
            .ToArray();
        var paymentsNeedingAttention = payments
            .Where(payment => IsPaymentAttentionStatus(payment.Status))
            .OrderByDescending(payment => payment.UpdatedUtc)
            .ToArray();
        var actionableReviews = invoiceReviews
            .Where(review => IsActionStatus(review.Status) || IsActionStatus(review.RecommendationStatus) || IsRisky(review.RiskLevel))
            .OrderByDescending(review => IsRisky(review.RiskLevel))
            .ThenByDescending(review => review.LastUpdatedUtc)
            .ToArray();
        var openAnomalies = anomalies.Items
            .Where(anomaly => !IsClosedStatus(anomaly.Status))
            .OrderByDescending(anomaly => anomaly.Confidence)
            .ThenByDescending(anomaly => anomaly.DetectedAtUtc)
            .ToArray();

        var netResult = monthly?.ProfitAndLoss?.NetResult ?? 0m;
        var recentActivity = BuildRecentActivity(companyId, transactions, invoices, bills, payments);

        return new FinanceOverviewViewModel
        {
            Kpis =
            [
                CreateKpi("Cash position", FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency), cash is null ? "No cash snapshot yet" : $"{FormatLabel(cash.RiskLevel)} risk", FinanceRoutes.CashPosition, ResolveTone(cash?.RiskLevel), "$", companyId),
                CreateKpi("Incoming this month", FormatCurrency(monthly?.ProfitAndLoss?.Revenue ?? 0m, monthly?.ProfitAndLoss?.Currency ?? currency), "Recognized revenue", FinanceRoutes.MonthlySummary, FinanceKpiTone.Positive, "in", companyId),
                CreateKpi("Outgoing this month", FormatCurrency(monthly?.ProfitAndLoss?.Expenses ?? 0m, monthly?.ProfitAndLoss?.Currency ?? currency), "Recorded expenses", FinanceRoutes.MonthlySummary, FinanceKpiTone.Warning, "out", companyId),
                CreateKpi("Overdue invoices", overdueInvoices.Length.ToString(CultureInfo.InvariantCulture), overdueInvoices.Length == 1 ? "Invoice past due" : "Invoices past due", FinanceRoutes.Invoices, overdueInvoices.Length > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Positive, "!", companyId),
                CreateKpi("Supplier bills to approve", billsAwaitingApproval.Length.ToString(CultureInfo.InvariantCulture), "Need review before posting", FinanceRoutes.SupplierBills, billsAwaitingApproval.Length > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive, "✓", companyId),
                CreateKpi("Open issues", openAnomalies.Length.ToString(CultureInfo.InvariantCulture), "Need investigation", FinanceRoutes.Issues, openAnomalies.Length > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Positive, "risk", companyId)
            ],
            ManagerInsight = new FinanceManagerInsightViewModel
            {
                Insights = BuildLauraInsights(companyId, cash, monthly, overdueInvoices, billsAwaitingApproval, actionableReviews, paymentsNeedingAttention, openAnomalies)
            },
            AttentionItems =
            [
                new FinanceAttentionItemViewModel
                {
                    Label = "Supplier bills to approve",
                    Count = billsAwaitingApproval.Length,
                    Amount = billsAwaitingApproval.Length == 0 ? null : FormatCurrency(billsAwaitingApproval.Sum(item => item.Amount ?? 0m), currency),
                    Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                    CtaLabel = "Review supplier bills",
                    Tone = billsAwaitingApproval.Length > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                    Icon = "bill"
                },
                new FinanceAttentionItemViewModel
                {
                    Label = "Invoices needing review",
                    Count = actionableReviews.Length,
                    Amount = actionableReviews.Length == 0 ? null : FormatCurrency(actionableReviews.Sum(item => item.Amount), currency),
                    Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Reviews, companyId),
                    CtaLabel = "Review invoices",
                    Tone = actionableReviews.Length > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                    Icon = "inv"
                },
                new FinanceAttentionItemViewModel
                {
                    Label = "Payments needing attention",
                    Count = paymentsNeedingAttention.Length,
                    Amount = paymentsNeedingAttention.Length == 0 ? null : FormatCurrency(paymentsNeedingAttention.Sum(item => item.Amount), currency),
                    Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Payments, companyId),
                    CtaLabel = "Review payments",
                    Tone = paymentsNeedingAttention.Length > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                    Icon = "pay"
                },
                new FinanceAttentionItemViewModel
                {
                    Label = "Issues to investigate",
                    Count = openAnomalies.Length,
                    Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Issues, companyId),
                    CtaLabel = "View issues",
                    Tone = openAnomalies.Length > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Positive,
                    Icon = "risk"
                }
            ],
            CashPosition = new FinanceCashPositionOverviewViewModel
            {
                CurrentBalance = FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency),
                ComparisonText = cash?.EstimatedRunwayDays is int runway ? $"{runway} days runway" : "Runway not available",
                RecommendedAction = string.IsNullOrWhiteSpace(cash?.RecommendedAction) ? "Review cash and upcoming payables." : cash!.RecommendedAction,
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
                Tone = ResolveTone(cash?.RiskLevel)
            },
            MonthlySummary = new FinanceMonthlySummaryOverviewViewModel
            {
                Period = monthly is null ? "No monthly summary yet" : $"{monthly.StartUtc:yyyy-MM-dd} to {monthly.EndUtc.AddDays(-1):yyyy-MM-dd}",
                TotalIncome = FormatCurrency(monthly?.ProfitAndLoss?.Revenue ?? 0m, monthly?.ProfitAndLoss?.Currency ?? currency),
                TotalExpenses = FormatCurrency(monthly?.ProfitAndLoss?.Expenses ?? 0m, monthly?.ProfitAndLoss?.Currency ?? currency),
                NetResult = FormatCurrency(netResult, monthly?.ProfitAndLoss?.Currency ?? currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId),
                Tone = netResult >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger
            },
            RecentActivity = recentActivity,
            HasNoFinanceActivity = !HasAnyFinanceActivity(cash, monthly, bills, billInbox, invoices, invoiceReviews, payments, transactions, anomalies)
        };
    }

    private static FinanceKpiViewModel CreateKpi(
        string label,
        string value,
        string comparisonText,
        string route,
        FinanceKpiTone tone,
        string icon,
        Guid companyId) =>
        new()
        {
            Label = label,
            Value = value,
            ComparisonText = comparisonText,
            Href = FinanceRoutes.WithCompanyContext(route, companyId),
            Tone = tone,
            Icon = icon
        };

    private static IReadOnlyList<RecentFinanceActivityViewModel> BuildRecentActivity(
        Guid companyId,
        IReadOnlyList<FinanceTransactionResponse> transactions,
        IReadOnlyList<FinanceInvoiceResponse> invoices,
        IReadOnlyList<FinanceBillResponse> bills,
        IReadOnlyList<FinancePaymentResponse> payments) =>
        transactions.Select(transaction => new RecentFinanceActivityViewModel
        {
            Title = string.IsNullOrWhiteSpace(transaction.Description) ? FormatLabel(transaction.TransactionType) : transaction.Description,
            Detail = transaction.CounterpartyName ?? transaction.AccountName,
            Amount = FormatCurrency(transaction.Amount, transaction.Currency),
            DateText = transaction.TransactionUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatLabel(transaction.TransactionType),
            Href = FinanceRoutes.BuildTransactionDetailPath(transaction.Id, companyId),
            Tone = transaction.Amount >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger,
            Icon = "txn",
            SortDateUtc = transaction.TransactionUtc
        })
        .Concat(invoices.Select(invoice => new RecentFinanceActivityViewModel
        {
            Title = $"Invoice {invoice.InvoiceNumber}",
            Detail = invoice.CounterpartyName,
            Amount = FormatCurrency(invoice.Amount, invoice.Currency),
            DateText = invoice.IssuedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatLabel(invoice.Status),
            Href = FinanceRoutes.BuildInvoiceDetailPath(invoice.Id, companyId),
            Tone = IsClosedStatus(invoice.Status) ? FinanceKpiTone.Positive : FinanceKpiTone.Warning,
            Icon = "inv",
            SortDateUtc = invoice.IssuedUtc
        }))
        .Concat(bills.Select(bill => new RecentFinanceActivityViewModel
        {
            Title = $"Bill {bill.BillNumber}",
            Detail = bill.CounterpartyName,
            Amount = FormatCurrency(bill.Amount, bill.Currency),
            DateText = bill.ReceivedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatLabel(bill.Status),
            Href = FinanceRoutes.BuildBillDetailPath(bill.Id, companyId),
            Tone = IsClosedStatus(bill.Status) ? FinanceKpiTone.Positive : FinanceKpiTone.Warning,
            Icon = "bill",
            SortDateUtc = bill.ReceivedUtc
        }))
        .Concat(payments.Select(payment => new RecentFinanceActivityViewModel
        {
            Title = payment.CounterpartyReference,
            Detail = FormatLabel(payment.PaymentType),
            Amount = FormatCurrency(payment.Amount, payment.Currency),
            DateText = payment.UpdatedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatLabel(payment.Status),
            Href = FinanceRoutes.BuildPaymentDetailPath(payment.Id, companyId),
            Tone = IsPaymentAttentionStatus(payment.Status) ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
            Icon = "pay",
            SortDateUtc = payment.UpdatedUtc
        }))
        .OrderByDescending(item => item.SortDateUtc)
        .Take(6)
        .ToArray();

    private static IReadOnlyList<FinanceInsightItemViewModel> BuildLauraInsights(
        Guid companyId,
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceBillInboxRowResponse> billsAwaitingApproval,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention,
        IReadOnlyList<FinanceAnomalyWorkbenchItemResponse> openAnomalies)
    {
        var insights = new List<FinanceInsightItemViewModel>();

        if (cash is not null && IsRisky(cash.RiskLevel))
        {
            insights.Add(CreateInsight("Cash needs attention", string.IsNullOrWhiteSpace(cash.Rationale) ? "Cash risk is elevated based on current balance and runway." : cash.Rationale, "Review cash plan", FinanceRoutes.CashPosition, FinanceKpiTone.Danger, "!", companyId));
        }

        if (overdueInvoices.Count > 0)
        {
            insights.Add(CreateInsight("Collections are slipping", $"{overdueInvoices.Count} invoice(s) are past due. Start with the oldest customer balance.", "Open invoices", FinanceRoutes.Invoices, FinanceKpiTone.Danger, "!", companyId));
        }

        if (billsAwaitingApproval.Count > 0)
        {
            insights.Add(CreateInsight("Supplier bills need decisions", $"{billsAwaitingApproval.Count} bill(s) are waiting for review before they can move forward.", "Review supplier bills", FinanceRoutes.SupplierBills, FinanceKpiTone.Warning, "bill", companyId));
        }

        if (openAnomalies.Count > 0)
        {
            insights.Add(CreateInsight("Issues need investigation", $"{openAnomalies.Count} finance issue(s) are still open. Resolve the highest-confidence items first.", "Investigate", FinanceRoutes.Issues, FinanceKpiTone.Danger, "risk", companyId));
        }

        if (paymentsNeedingAttention.Count > 0)
        {
            insights.Add(CreateInsight("Payments need follow-up", $"{paymentsNeedingAttention.Count} payment(s) are not settled cleanly.", "Open payments", FinanceRoutes.Payments, FinanceKpiTone.Warning, "pay", companyId));
        }

        if (insights.Count == 0 && monthly?.ProfitAndLoss is { } pnl)
        {
            var message = pnl.NetResult >= 0m
                ? "The month is profitable so far. Keep receivables moving and review upcoming bills before they age."
                : "The month is running at a loss so far. Review expense categories and upcoming cash needs.";
            insights.Add(CreateInsight("Month-to-date posture", message, "Open monthly summary", FinanceRoutes.MonthlySummary, pnl.NetResult >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Warning, "✓", companyId));
        }

        if (insights.Count == 0)
        {
            insights.Add(CreateInsight("No urgent finance risks detected", "Review recent activity to keep records clean.", "Open activity", FinanceRoutes.Activity, FinanceKpiTone.Positive, "✓", companyId));
        }

        return insights.Take(4).ToArray();
    }

    private static FinanceInsightItemViewModel CreateInsight(
        string title,
        string explanation,
        string actionLabel,
        string route,
        FinanceKpiTone tone,
        string icon,
        Guid companyId) =>
        new()
        {
            Title = title,
            Explanation = explanation,
            ActionLabel = actionLabel,
            Href = FinanceRoutes.WithCompanyContext(route, companyId),
            Tone = tone,
            Icon = icon
        };

    private static bool HasAnyFinanceActivity(
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        IReadOnlyList<FinanceBillResponse> bills,
        IReadOnlyList<FinanceBillInboxRowResponse> billInbox,
        IReadOnlyList<FinanceInvoiceResponse> invoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> payments,
        IReadOnlyList<FinanceTransactionResponse> transactions,
        FinanceAnomalyWorkbenchResponse anomalies) =>
        cash is not null ||
        monthly?.ProfitAndLoss is not null ||
        bills.Count > 0 ||
        billInbox.Count > 0 ||
        invoices.Count > 0 ||
        invoiceReviews.Count > 0 ||
        payments.Count > 0 ||
        transactions.Count > 0 ||
        anomalies.TotalCount > 0;

    private void ResetOverview()
    {
        ResetOverviewState();
        IsOverviewLoading = false;
    }

    private void ResetOverviewState() =>
        (Overview, IsOverviewEmpty, OverviewErrorMessage) = (null, false, null);

    private void CancelOverviewLoad()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _overviewLoadCts, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    public void Dispose() => CancelOverviewLoad();

    private static string ResolveCurrency(
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        IReadOnlyList<FinanceInvoiceResponse> invoices,
        IReadOnlyList<FinanceBillResponse> bills,
        IReadOnlyList<FinancePaymentResponse> payments,
        IReadOnlyList<FinanceTransactionResponse> transactions) =>
        FirstNonEmpty(
            cash?.Currency,
            monthly?.ProfitAndLoss?.Currency,
            invoices.FirstOrDefault()?.Currency,
            bills.FirstOrDefault()?.Currency,
            payments.FirstOrDefault()?.Currency,
            transactions.FirstOrDefault()?.Currency,
            "USD");

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool IsRisky(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("high", StringComparison.Ordinal) ||
            normalized.Contains("critical", StringComparison.Ordinal) ||
            normalized.Contains("medium", StringComparison.Ordinal) ||
            normalized.Contains("warning", StringComparison.Ordinal);
    }

    private static bool IsClosedStatus(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("paid", StringComparison.Ordinal) ||
            normalized.Contains("settled", StringComparison.Ordinal) ||
            normalized.Contains("closed", StringComparison.Ordinal) ||
            normalized.Contains("resolved", StringComparison.Ordinal) ||
            normalized.Contains("approved", StringComparison.Ordinal) ||
            normalized.Contains("rejected", StringComparison.Ordinal) ||
            normalized.Contains("cancel", StringComparison.Ordinal);
    }

    private static bool IsActionStatus(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("pending", StringComparison.Ordinal) ||
            normalized.Contains("review", StringComparison.Ordinal) ||
            normalized.Contains("approval", StringComparison.Ordinal) ||
            normalized.Contains("open", StringComparison.Ordinal) ||
            normalized.Contains("new", StringComparison.Ordinal) ||
            normalized.Contains("draft", StringComparison.Ordinal) ||
            normalized.Contains("needs", StringComparison.Ordinal);
    }

    private static bool IsPaymentAttentionStatus(string? value)
    {
        var normalized = Normalize(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
            !normalized.Contains("settled", StringComparison.Ordinal) &&
            !normalized.Contains("completed", StringComparison.Ordinal) &&
            !normalized.Contains("paid", StringComparison.Ordinal) &&
            !normalized.Contains("succeeded", StringComparison.Ordinal);
    }

    private static FinanceKpiTone ResolveTone(string? value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("critical", StringComparison.Ordinal) || normalized.Contains("high", StringComparison.Ordinal))
        {
            return FinanceKpiTone.Danger;
        }

        if (normalized.Contains("warning", StringComparison.Ordinal) || normalized.Contains("medium", StringComparison.Ordinal))
        {
            return FinanceKpiTone.Warning;
        }

        return FinanceKpiTone.Positive;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
