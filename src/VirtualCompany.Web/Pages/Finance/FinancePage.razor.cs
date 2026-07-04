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
            var cashTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetCashPositionAsync(companyId, cancellationToken: cancellationToken),
                (FinanceCashPositionResponse?)null);
            var monthlyTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetMonthlySummaryAsync(companyId, cancellationToken: cancellationToken),
                (FinanceMonthlySummaryResponse?)null);
            var billsTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetBillsAsync(companyId, 50, cancellationToken),
                (IReadOnlyList<FinanceBillResponse>)[]);
            var billInboxTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetBillInboxAsync(companyId, 50, cancellationToken),
                (IReadOnlyList<FinanceBillInboxRowResponse>)[]);
            var invoicesTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetInvoicesAsync(companyId, limit: 50, cancellationToken: cancellationToken),
                (IReadOnlyList<FinanceInvoiceResponse>)[]);
            var invoiceReviewsTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetInvoiceReviewsAsync(companyId, limit: 50, cancellationToken: cancellationToken),
                (IReadOnlyList<FinanceInvoiceReviewListItemResponse>)[]);
            var paymentsTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetPaymentsAsync(companyId, limit: 50, cancellationToken: cancellationToken),
                (IReadOnlyList<FinancePaymentResponse>)[]);
            var transactionsTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetTransactionsAsync(companyId, limit: 50, cancellationToken: cancellationToken),
                (IReadOnlyList<FinanceTransactionResponse>)[]);
            var anomaliesTask = UseFallbackWhenFinanceIsNotInitializedAsync(
                FinanceApiClient.GetAnomalyWorkbenchAsync(companyId, pageSize: 25, cancellationToken: cancellationToken),
                new FinanceAnomalyWorkbenchResponse());

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
            .Where(invoice => IsOverdueDocument(invoice))
            .OrderBy(invoice => invoice.DueUtc)
            .ToArray();
        var supplierBillsDue = bills
            .Where(IsSupplierBillDueAttention)
            .OrderByDescending(bill => Normalize(bill.DueStatus) == "overdue")
            .ThenBy(bill => bill.DueUtc)
            .ToArray();
        var paymentProposalsNeedingDecision = bills
            .Where(IsSupplierPaymentProposalAttention)
            .OrderByDescending(bill => Normalize(bill.PaymentProposal?.Status) == "ready_for_payment")
            .ThenBy(bill => bill.DueUtc)
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
        var receivables = BuildReceivablesSnapshot(invoices, currency);
        var openAnomalies = anomalies.Items
            .Where(anomaly => !IsClosedStatus(anomaly.Status))
            .OrderByDescending(anomaly => anomaly.Confidence)
            .ThenByDescending(anomaly => anomaly.DetectedAtUtc)
            .ToArray();

        var netResult = monthly?.ProfitAndLoss?.NetResult ?? 0m;
        var hasValidMonthlySummary = HasValidMonthlySummary(monthly);
        var recentActivity = BuildRecentActivity(companyId, transactions, invoices, bills, payments);
        var cashRiskAlert = BuildCashRiskAlert(companyId, cash, currency, receivables);
        var supplierWorkCount = supplierBillsDue.Length + billsAwaitingApproval.Length + paymentProposalsNeedingDecision.Length + paymentsNeedingAttention.Length;
        var supplierWorkAmount =
            supplierBillsDue.Sum(item => item.Amount) +
            billsAwaitingApproval.Sum(item => item.Amount ?? 0m) +
            paymentProposalsNeedingDecision.Sum(item => item.PaymentProposal?.Amount ?? item.Amount) +
            paymentsNeedingAttention.Sum(item => item.Amount);
        var customerWorkCount = overdueInvoices.Length + actionableReviews.Length;
        var customerWorkAmount = overdueInvoices.Sum(item => item.Amount) + actionableReviews.Sum(item => item.Amount);

        return new FinanceOverviewViewModel
        {
            CashRiskAlert = cashRiskAlert,
            Kpis = BuildOverviewKpis(companyId, cash, monthly, hasValidMonthlySummary, currency, receivables, supplierWorkCount, supplierWorkAmount, ResolveSupplierWorkTone(supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, paymentsNeedingAttention), overdueInvoices, actionableReviews, customerWorkCount, customerWorkAmount, openAnomalies),
            ManagerInsight = new FinanceManagerInsightViewModel
            {
                Insights = BuildLauraInsights(companyId, cash, monthly, cashRiskAlert is not null, overdueInvoices, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies)
            },
            AttentionSummary = BuildAttentionSummary(companyId, currency, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies),
            AttentionItems = BuildAttentionItems(companyId, currency, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies),
            CashPosition = new FinanceCashPositionOverviewViewModel
            {
                Title = "Cash plan snapshot",
                CurrentBalance = FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency),
                ComparisonText = cash?.EstimatedRunwayDays is int runway ? $"{runway} days runway" : "Runway not available",
                ContextTitle = "Planning context",
                ContextText = BuildCashPlanContext(hasValidMonthlySummary ? monthly : null, supplierBillsDue, paymentProposalsNeedingDecision, paymentsNeedingAttention, receivables, currency),
                RecommendedAction = BuildCashPlanRecommendedAction(cash?.RecommendedAction, supplierBillsDue, paymentProposalsNeedingDecision, paymentsNeedingAttention),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
                Tone = ResolveTone(cash?.RiskLevel)
            },
            MonthlySummary = new FinanceMonthlySummaryOverviewViewModel
            {
                IsAvailable = hasValidMonthlySummary,
                Period = FormatMonthlyPeriod(monthly),
                EmptyTitle = "No monthly report available yet.",
                EmptyMessage = "A valid reporting period is not available yet. Sync finance data or open the monthly report to review source data.",
                TotalIncome = hasValidMonthlySummary ? FormatDashboardCurrency(monthly!.ProfitAndLoss.Revenue, monthly.ProfitAndLoss.Currency, currency) : string.Empty,
                TotalExpenses = hasValidMonthlySummary ? FormatDashboardCurrency(monthly!.ProfitAndLoss.Expenses, monthly.ProfitAndLoss.Currency, currency) : string.Empty,
                NetResult = hasValidMonthlySummary ? FormatDashboardCurrency(netResult, monthly!.ProfitAndLoss.Currency, currency) : string.Empty,
                CurrencyNote = hasValidMonthlySummary ? BuildCurrencyNote(monthly!.ProfitAndLoss.Currency, currency) : null,
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId),
                ActionLabel = "View monthly report",
                Tone = netResult >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger
            },
            RecentActivity = recentActivity,
            HasNoFinanceActivity = !HasAnyFinanceActivity(cash, monthly, bills, billInbox, invoices, invoiceReviews, payments, transactions, anomalies)
        };
    }

    private static FinanceCashRiskAlertViewModel? BuildCashRiskAlert(
        Guid companyId,
        FinanceCashPositionResponse? cash,
        string fallbackCurrency,
        ReceivablesSnapshot receivables)
    {
        if (cash is null)
        {
            return null;
        }

        var isCritical = cash.AvailableBalance <= 0m ||
            Normalize(cash.RiskLevel).Contains("critical", StringComparison.Ordinal) ||
            Normalize(cash.RiskLevel).Contains("high", StringComparison.Ordinal) ||
            cash.EstimatedRunwayDays <= 0;
        var isWarning = isCritical ||
            Normalize(cash.RiskLevel).Contains("warning", StringComparison.Ordinal) ||
            Normalize(cash.RiskLevel).Contains("medium", StringComparison.Ordinal);

        if (!isWarning)
        {
            return null;
        }

        var runwayText = cash.EstimatedRunwayDays is int runway
            ? $"{runway.ToString(CultureInfo.InvariantCulture)} day{(runway == 1 ? string.Empty : "s")}"
            : "not available";
        var currency = FirstNonEmpty(cash.Currency, fallbackCurrency, "SEK");
        var balance = FormatCurrency(cash.AvailableBalance, currency);
        var reason = cash.AvailableBalance <= 0m
            ? "There is no available cash for upcoming payments."
            : cash.EstimatedRunwayDays <= 0
                ? "Runway is at zero days based on current burn."
                : string.IsNullOrWhiteSpace(cash.Rationale)
                    ? "Cash risk is elevated based on balance and runway."
                    : cash.Rationale;

        return new FinanceCashRiskAlertViewModel
        {
            Title = "Cash needs attention",
            Message = $"Current balance is {balance} and runway is {runwayText}.",
            BalanceValue = balance,
            RunwayValue = runwayText,
            Reason = reason,
            SupportingText = receivables.OpenInvoiceCount > 0
                ? $"You have {FormatReceivablesAmount(receivables)} in customer invoices, but it is not cash until collected."
                : null,
            ActionLabel = "Review cash plan",
            Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
            Tone = isCritical ? FinanceKpiTone.Danger : FinanceKpiTone.Warning
        };
    }

    private static string FormatMonthlyPeriod(FinanceMonthlySummaryResponse? monthly)
    {
        if (monthly is null)
        {
            return "No monthly report available yet.";
        }

        var start = monthly.StartUtc;
        var endExclusive = monthly.EndUtc;
        if (!IsValidReportingDate(start) || !IsValidReportingDate(endExclusive) || endExclusive <= start)
        {
            return "No monthly report available yet.";
        }

        var endInclusive = endExclusive.AddDays(-1);
        if (start.Year == endInclusive.Year && start.Month == endInclusive.Month)
        {
            return start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        }

        return $"{start:MMM d, yyyy} to {endInclusive:MMM d, yyyy}";
    }

    private static FinanceAttentionSummaryViewModel BuildAttentionSummary(
        Guid companyId,
        string currency,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillInboxRowResponse> billsAwaitingApproval,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention,
        IReadOnlyList<FinanceAnomalyWorkbenchItemResponse> openAnomalies)
    {
        var supplierItemCount = supplierBillsDue.Count + billsAwaitingApproval.Count + paymentProposalsNeedingDecision.Count;
        var reviewItemCount = invoiceReviews.Count;
        var paymentItemCount = paymentsNeedingAttention.Count;
        var issueItemCount = openAnomalies.Count;
        var totalCount = supplierItemCount + reviewItemCount + paymentItemCount + issueItemCount;
        var totalAmount =
            supplierBillsDue.Sum(item => item.Amount) +
            billsAwaitingApproval.Sum(item => item.Amount ?? 0m) +
            paymentProposalsNeedingDecision.Sum(item => item.PaymentProposal?.Amount ?? item.Amount) +
            invoiceReviews.Sum(item => item.Amount) +
            paymentsNeedingAttention.Sum(item => item.Amount);

        if (totalCount == 0)
        {
            return new FinanceAttentionSummaryViewModel
            {
                Title = "Nothing urgent is waiting",
                Message = "No finance actions need attention right now.",
                ActionLabel = "Review recent activity",
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Transactions, companyId),
                Tone = FinanceKpiTone.Positive
            };
        }

        var areas = new List<string>();
        if (supplierItemCount > 0) areas.Add("supplier work");
        if (paymentItemCount > 0) areas.Add("payments");
        if (reviewItemCount > 0) areas.Add("invoice reviews");
        if (issueItemCount > 0) areas.Add("issues");

        var route = supplierItemCount > 0
            ? FinanceRoutes.SupplierBills
            : paymentItemCount > 0
                ? FinanceRoutes.Payments
                : reviewItemCount > 0
                    ? FinanceRoutes.Reviews
                    : FinanceRoutes.Issues;

        return new FinanceAttentionSummaryViewModel
        {
            Title = $"{totalCount.ToString(CultureInfo.InvariantCulture)} item{(totalCount == 1 ? string.Empty : "s")} need action",
            Message = $"Across {FormatList(areas)}. Start with the oldest overdue or blocked item.",
            Amount = FormatCurrency(totalAmount, currency),
            ActionLabel = "Review queue",
            Href = FinanceRoutes.WithCompanyContext(route, companyId),
            Tone = supplierBillsDue.Any(IsOverdueBill) || openAnomalies.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning
        };
    }

    private static IReadOnlyList<FinanceAttentionItemViewModel> BuildAttentionItems(
        Guid companyId,
        string currency,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillInboxRowResponse> billsAwaitingApproval,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention,
        IReadOnlyList<FinanceAnomalyWorkbenchItemResponse> openAnomalies)
    {
        var items = new List<FinanceAttentionItemViewModel>
        {
            new()
            {
                Label = "Supplier bills due",
                Count = supplierBillsDue.Count,
                Amount = supplierBillsDue.Count == 0 ? null : FormatCurrency(supplierBillsDue.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = "Review due bills",
                Tone = ResolveSupplierBillDueTone(supplierBillsDue),
                Icon = "bill"
            },
            new()
            {
                Label = "Supplier bills to approve",
                Count = billsAwaitingApproval.Count,
                Amount = billsAwaitingApproval.Count == 0 ? null : FormatCurrency(billsAwaitingApproval.Sum(item => item.Amount ?? 0m), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = "Review supplier bills",
                Tone = billsAwaitingApproval.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "bill"
            },
            new()
            {
                Label = "Payment proposals",
                Count = paymentProposalsNeedingDecision.Count,
                Amount = paymentProposalsNeedingDecision.Count == 0 ? null : FormatCurrency(paymentProposalsNeedingDecision.Sum(item => item.PaymentProposal?.Amount ?? item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = "Review proposals",
                Tone = paymentProposalsNeedingDecision.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "pay"
            },
            new()
            {
                Label = "Payments needing attention",
                Count = paymentsNeedingAttention.Count,
                Amount = paymentsNeedingAttention.Count == 0 ? null : FormatCurrency(paymentsNeedingAttention.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Payments, companyId),
                CtaLabel = "Review payments",
                Tone = paymentsNeedingAttention.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "pay"
            },
            new()
            {
                Label = "Invoices needing review",
                Count = invoiceReviews.Count,
                Amount = invoiceReviews.Count == 0 ? null : FormatCurrency(invoiceReviews.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Reviews, companyId),
                CtaLabel = "Review invoices",
                Tone = invoiceReviews.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "inv"
            },
            new()
            {
                Label = "Issues to investigate",
                Count = openAnomalies.Count,
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Issues, companyId),
                CtaLabel = "View issues",
                Tone = openAnomalies.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Positive,
                Icon = "risk"
            }
        };

        return items.Where(item => item.Count > 0).ToArray();
    }

    private static IReadOnlyList<FinanceKpiViewModel> BuildOverviewKpis(
        Guid companyId,
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        bool hasValidMonthlySummary,
        string currency,
        ReceivablesSnapshot receivables,
        int supplierWorkCount,
        decimal supplierWorkAmount,
        FinanceKpiTone supplierWorkTone,
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> actionableReviews,
        int customerWorkCount,
        decimal customerWorkAmount,
        IReadOnlyList<FinanceAnomalyWorkbenchItemResponse> openAnomalies)
    {
        var kpis = new List<FinanceKpiViewModel>
        {
            CreateKpi("Cash position", FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency), cash is null ? "No cash snapshot yet" : $"{FormatLabel(cash.RiskLevel)} risk", FinanceRoutes.CashPosition, ResolveTone(cash?.RiskLevel), "$", companyId, FinanceKpiEmphasis.Primary)
        };

        if (hasValidMonthlySummary)
        {
            kpis.Add(CreateKpi("Incoming this month", FormatDashboardCurrency(monthly!.ProfitAndLoss.Revenue, monthly.ProfitAndLoss.Currency, currency), BuildCurrencyAwareLabel("Recognized revenue", monthly.ProfitAndLoss.Currency, currency), FinanceRoutes.MonthlySummary, FinanceKpiTone.Positive, "in", companyId));
            kpis.Add(CreateKpi("Outgoing this month", FormatDashboardCurrency(monthly.ProfitAndLoss.Expenses, monthly.ProfitAndLoss.Currency, currency), BuildCurrencyAwareLabel("Recorded expenses", monthly.ProfitAndLoss.Currency, currency), FinanceRoutes.MonthlySummary, FinanceKpiTone.Warning, "out", companyId));
        }

        if (receivables.OpenInvoiceCount > 0)
        {
            kpis.Add(CreateKpi(
                "Receivables",
                receivables.OpenInvoiceCount.ToString(CultureInfo.InvariantCulture),
                BuildReceivablesKpiLabel(receivables),
                FinanceRoutes.Invoices,
                receivables.OverdueAmount > 0m ? FinanceKpiTone.Danger : FinanceKpiTone.Warning,
                "inv",
                companyId));
        }

        if (supplierWorkCount > 0)
        {
            kpis.Add(CreateKpi("Supplier work", supplierWorkCount.ToString(CultureInfo.InvariantCulture), $"{FormatCurrency(supplierWorkAmount, currency)} across bills and payments", FinanceRoutes.SupplierBills, supplierWorkTone, "bill", companyId));
        }

        if (customerWorkCount > 0)
        {
            kpis.Add(CreateKpi("Customer actions", customerWorkCount.ToString(CultureInfo.InvariantCulture), BuildCustomerWorkKpiLabel(overdueInvoices, actionableReviews, customerWorkAmount, currency), overdueInvoices.Count > 0 ? FinanceRoutes.Invoices : FinanceRoutes.Reviews, ResolveCustomerWorkTone(overdueInvoices, actionableReviews), "!", companyId));
        }

        if (openAnomalies.Count > 0)
        {
            kpis.Add(CreateKpi("Open issues", openAnomalies.Count.ToString(CultureInfo.InvariantCulture), "Need investigation", FinanceRoutes.Issues, FinanceKpiTone.Danger, "risk", companyId));
        }

        return kpis;
    }

    private static string BuildCustomerWorkKpiLabel(
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        decimal amount,
        string currency)
    {
        var count = overdueInvoices.Count + invoiceReviews.Count;
        if (count == 0)
        {
            return "No customer actions waiting";
        }

        if (overdueInvoices.Count > 0 && invoiceReviews.Count > 0)
        {
            return $"{FormatCurrency(amount, currency)} across collections and reviews";
        }

        return overdueInvoices.Count > 0
            ? $"{FormatCurrency(amount, currency)} in past-due invoices"
            : $"{FormatCurrency(amount, currency)} in invoice reviews needing action";
    }

    private static string BuildReceivablesKpiLabel(ReceivablesSnapshot receivables)
    {
        var parts = new List<string>
        {
            $"{FormatReceivablesAmount(receivables)} outstanding"
        };

        parts.Add(receivables.OverdueAmount > 0m
            ? receivables.IsMixedCurrency
                ? "Some overdue"
                : $"{FormatCurrency(receivables.OverdueAmount, receivables.Currency)} overdue"
            : "Nothing overdue");

        if (receivables.PartiallyPaidInvoiceCount > 0)
        {
            parts.Add("Includes partially paid invoices");
        }

        return string.Join("; ", parts);
    }

    private static string FormatReceivablesAmount(ReceivablesSnapshot receivables) =>
        receivables.IsMixedCurrency
            ? "multiple currencies outstanding"
            : FormatCurrency(receivables.OutstandingAmount, receivables.Currency);

    private static string BuildCashPlanContext(
        FinanceMonthlySummaryResponse? monthly,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention,
        ReceivablesSnapshot receivables,
        string currency)
    {
        var supplierAmount =
            supplierBillsDue.Sum(item => item.Amount) +
            paymentProposalsNeedingDecision.Sum(item => item.PaymentProposal?.Amount ?? item.Amount) +
            paymentsNeedingAttention.Sum(item => item.Amount);
        var supplierCount = supplierBillsDue.Count + paymentProposalsNeedingDecision.Count + paymentsNeedingAttention.Count;

        if (monthly?.ProfitAndLoss is { } pnl)
        {
            var sourceCurrency = FirstNonEmpty(pnl.Currency, currency);
            var incoming = FormatDashboardCurrency(pnl.Revenue, sourceCurrency, currency);
            var outgoing = FormatDashboardCurrency(pnl.Expenses, sourceCurrency, currency);
            if (supplierCount > 0)
            {
                var receivablesContext = receivables.OpenInvoiceCount > 0
                    ? $" {FormatReceivablesAmount(receivables)} in customer invoices is expected cash, not current cash."
                    : string.Empty;
                return $"{supplierCount} supplier payment item{(supplierCount == 1 ? string.Empty : "s")} worth {FormatCurrency(supplierAmount, currency)} need sequencing against {incoming} incoming and {outgoing} outgoing this month.{receivablesContext}";
            }

            return $"Use the cash plan to compare {incoming} incoming with {outgoing} outgoing before approving new payments.";
        }

        if (supplierCount > 0)
        {
            var receivablesContext = receivables.OpenInvoiceCount > 0
                ? $" Customer invoices show {FormatReceivablesAmount(receivables)} outstanding, but that is not cash until collected."
                : string.Empty;
            return $"{supplierCount} supplier payment item{(supplierCount == 1 ? string.Empty : "s")} worth {FormatCurrency(supplierAmount, currency)} need sequencing because the monthly report is unavailable.{receivablesContext}";
        }

        if (receivables.OpenInvoiceCount > 0)
        {
            return $"Customer invoices show {FormatReceivablesAmount(receivables)} outstanding, but open invoices are not included in cash until paid.";
        }

        return "No monthly report is available yet. Use the cash plan to confirm upcoming payables before approving new payments.";
    }

    private static string BuildCashPlanRecommendedAction(
        string? recommendedAction,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention)
    {
        if (supplierBillsDue.Count > 0 || paymentProposalsNeedingDecision.Count > 0 || paymentsNeedingAttention.Count > 0)
        {
            return "Use this plan to decide which supplier payments can safely move next.";
        }

        return FormatRecommendedCashAction(recommendedAction);
    }

    private static ReceivablesSnapshot BuildReceivablesSnapshot(
        IReadOnlyList<FinanceInvoiceResponse> invoices,
        string dashboardCurrency)
    {
        var rows = invoices
            .Select(invoice => new ReceivableInvoiceSnapshot(invoice, CalculateReceivableRemainingAmount(invoice)))
            .Where(row => row.RemainingAmount > 0m && !IsClosedReceivableInvoice(row.Invoice))
            .ToArray();

        if (rows.Length == 0)
        {
            return new ReceivablesSnapshot(0, 0m, 0m, 0, FirstNonEmpty(dashboardCurrency, "SEK"), false);
        }

        var currencies = rows
            .Select(row => FirstNonEmpty(row.Invoice.PaymentContext?.Currency, row.Invoice.Currency, dashboardCurrency, "SEK"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currency = currencies.Length == 1 ? currencies[0] : FirstNonEmpty(dashboardCurrency, "SEK");
        var isMixedCurrency = currencies.Length > 1;
        var overdueRows = rows.Where(row => IsOverdueDocument(row.Invoice)).ToArray();

        return new ReceivablesSnapshot(
            rows.Length,
            Math.Round(rows.Sum(row => row.RemainingAmount), 2, MidpointRounding.AwayFromZero),
            Math.Round(overdueRows.Sum(row => row.RemainingAmount), 2, MidpointRounding.AwayFromZero),
            rows.Count(row => IsPartiallyPaidInvoice(row.Invoice)),
            currency,
            isMixedCurrency);
    }

    private static decimal CalculateReceivableRemainingAmount(FinanceInvoiceResponse invoice)
    {
        if (IsClosedReceivableInvoice(invoice))
        {
            return 0m;
        }

        if (invoice.PaymentContext is { } paymentContext)
        {
            if (paymentContext.RemainingAmount > 0m)
            {
                return Math.Round(paymentContext.RemainingAmount, 2, MidpointRounding.AwayFromZero);
            }

            if (paymentContext.PaidAmount > 0m && paymentContext.TotalAmount > 0m)
            {
                return Math.Round(Math.Max(paymentContext.TotalAmount - paymentContext.PaidAmount, 0m), 2, MidpointRounding.AwayFromZero);
            }
        }

        return Math.Round(Math.Max(invoice.Amount, 0m), 2, MidpointRounding.AwayFromZero);
    }

    private static bool IsClosedReceivableInvoice(FinanceInvoiceResponse invoice)
    {
        var posting = Normalize(invoice.PostingStatus);
        var settlement = Normalize(invoice.SettlementStatus);
        var kind = Normalize(invoice.DocumentKind);
        var fallback = Normalize(invoice.Status);
        var providerStatus = Normalize(invoice.ProviderStatus);

        return settlement is "paid" or "credited" ||
            kind is "credit_note" or "supplier_credit_note" ||
            posting == "cancelled" ||
            fallback is "paid" or "settled" or "closed" or "resolved" or "cancelled" or "void" or "credited" ||
            providerStatus is "paid" or "settled" or "closed" or "cancelled" or "void" or "credited";
    }

    private static bool IsPartiallyPaidInvoice(FinanceInvoiceResponse invoice) =>
        Normalize(invoice.SettlementStatus) == "partially_paid" ||
        Normalize(invoice.Status) == "partially_paid" ||
        invoice.PaymentContext is { PaidAmount: > 0m, RemainingAmount: > 0m };

    private static bool IsValidReportingDate(DateTime value) =>
        value.Year >= 1900;

    private static bool HasValidMonthlySummary(FinanceMonthlySummaryResponse? monthly) =>
        monthly is not null &&
        IsValidReportingDate(monthly.StartUtc) &&
        IsValidReportingDate(monthly.EndUtc) &&
        monthly.EndUtc > monthly.StartUtc;

    private static string FormatDashboardCurrency(decimal amount, string sourceCurrency, string dashboardCurrency)
    {
        var currency = FirstNonEmpty(sourceCurrency, dashboardCurrency, "SEK");
        return FormatCurrency(amount, currency);
    }

    private static string BuildCurrencyAwareLabel(string label, string sourceCurrency, string dashboardCurrency) =>
        IsDifferentCurrency(sourceCurrency, dashboardCurrency)
            ? $"{label} ({sourceCurrency}, not converted)"
            : label;

    private static string? BuildCurrencyNote(string sourceCurrency, string dashboardCurrency) =>
        IsDifferentCurrency(sourceCurrency, dashboardCurrency)
            ? $"This monthly report is in {sourceCurrency}. Amounts are shown in source currency and are not converted to {dashboardCurrency}."
            : null;

    private static bool IsDifferentCurrency(string sourceCurrency, string dashboardCurrency) =>
        !string.IsNullOrWhiteSpace(sourceCurrency) &&
        !string.IsNullOrWhiteSpace(dashboardCurrency) &&
        !string.Equals(sourceCurrency.Trim(), dashboardCurrency.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string FormatRecommendedCashAction(string? action)
    {
        var normalized = Normalize(action).Replace("-", "_", StringComparison.Ordinal);
        return normalized switch
        {
            "" => "Review cash and upcoming payables.",
            "review_cash_plan" => "Review cash plan.",
            "monitor_cash" or "monitor_cash_position" => "Monitor cash position.",
            "reduce_outgoing_payments" => "Review outgoing payments.",
            "collect_overdue_invoices" => "Follow up overdue invoices.",
            _ => FormatSentence(action)
        };
    }

    private static async Task<T> UseFallbackWhenFinanceIsNotInitializedAsync<T>(
        Task<T> task,
        T fallback)
    {
        try
        {
            return await task;
        }
        catch (FinanceNotInitializedApiException)
        {
            return fallback;
        }
    }

    private static FinanceKpiViewModel CreateKpi(
        string label,
        string value,
        string comparisonText,
        string route,
        FinanceKpiTone tone,
        string icon,
        Guid companyId,
        FinanceKpiEmphasis emphasis = FinanceKpiEmphasis.Standard) =>
        new()
        {
            Label = label,
            Value = value,
            ComparisonText = comparisonText,
            Href = FinanceRoutes.WithCompanyContext(route, companyId),
            Tone = tone,
            Emphasis = emphasis,
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
            Title = BuildTransactionActivityTitle(transaction),
            Detail = BuildTransactionActivityDetail(transaction),
            Amount = FormatCurrency(transaction.Amount, transaction.Currency),
            DateText = transaction.TransactionUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = BuildTransactionStatusText(transaction),
            Href = FinanceRoutes.BuildTransactionDetailPath(transaction.Id, companyId),
            Tone = transaction.Amount >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger,
            Icon = "txn",
            SortDateUtc = transaction.TransactionUtc
        })
        .Concat(invoices.Select(invoice => new RecentFinanceActivityViewModel
        {
            Title = BuildInvoiceActivityTitle(invoice),
            Detail = BuildDocumentActivityDetail("Customer invoice", invoice.InvoiceNumber, invoice.CounterpartyName),
            Amount = FormatCurrency(invoice.Amount, invoice.Currency),
            DateText = invoice.IssuedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatDocumentStatusLabel(invoice.PostingStatus, invoice.SettlementStatus, invoice.DueStatus, invoice.DocumentKind, invoice.Status),
            Href = FinanceRoutes.BuildInvoiceDetailPath(invoice.Id, companyId),
            Tone = IsClosedDocument(invoice.PostingStatus, invoice.SettlementStatus, invoice.DocumentKind, invoice.Status) ? FinanceKpiTone.Positive : FinanceKpiTone.Warning,
            Icon = "inv",
            SortDateUtc = invoice.IssuedUtc
        }))
        .Concat(bills.Select(bill => new RecentFinanceActivityViewModel
        {
            Title = BuildBillActivityTitle(bill),
            Detail = BuildDocumentActivityDetail("Supplier bill", bill.BillNumber, bill.CounterpartyName),
            Amount = FormatCurrency(bill.Amount, bill.Currency),
            DateText = bill.ReceivedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatDocumentStatusLabel(bill.PostingStatus, bill.SettlementStatus, bill.DueStatus, bill.DocumentKind, bill.Status),
            Href = FinanceRoutes.BuildBillDetailPath(bill.Id, companyId),
            Tone = IsOverdueBill(bill)
                ? FinanceKpiTone.Danger
                : IsSupplierBillDueAttention(bill)
                    ? FinanceKpiTone.Warning
                    : IsClosedDocument(bill.PostingStatus, bill.SettlementStatus, bill.DocumentKind, bill.Status)
                        ? FinanceKpiTone.Positive
                        : FinanceKpiTone.Warning,
            Icon = "bill",
            SortDateUtc = bill.ReceivedUtc
        }))
        .Concat(payments.Select(payment => new RecentFinanceActivityViewModel
        {
            Title = BuildPaymentActivityTitle(payment),
            Detail = BuildPaymentActivityDetail(payment),
            Amount = FormatCurrency(payment.Amount, payment.Currency),
            DateText = payment.UpdatedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StatusText = FormatPaymentStatus(payment.Status),
            Href = FinanceRoutes.BuildPaymentDetailPath(payment.Id, companyId),
            Tone = IsPaymentAttentionStatus(payment.Status) ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
            Icon = "pay",
            SortDateUtc = payment.UpdatedUtc
        }))
        .OrderByDescending(item => item.SortDateUtc)
        .Take(6)
        .ToArray();

    private static string BuildTransactionActivityTitle(FinanceTransactionResponse transaction)
    {
        var counterparty = FirstBusinessName(transaction.CounterpartyName);
        if (transaction.BillId.HasValue)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? "Supplier payment"
                : $"Supplier bill payment to {counterparty}";
        }

        if (transaction.InvoiceId.HasValue)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? "Invoice payment received"
                : $"Invoice payment from {counterparty}";
        }

        var isOutgoing = transaction.Amount < 0m || Normalize(transaction.TransactionType).Contains("out", StringComparison.Ordinal);
        if (isOutgoing)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? "Outgoing payment"
                : $"Outgoing payment to {counterparty}";
        }

        return string.IsNullOrWhiteSpace(counterparty)
            ? "Incoming payment"
            : $"Incoming payment from {counterparty}";
    }

    private static string BuildTransactionActivityDetail(FinanceTransactionResponse transaction)
    {
        var direction = transaction.Amount < 0m ? "Outgoing" : "Incoming";
        var parts = new List<string> { $"{direction} transaction" };
        AddIfPresent(parts, FirstBusinessName(transaction.CounterpartyName));
        AddIfPresent(parts, transaction.AccountName);
        AddReferenceIfUseful(parts, transaction.ExternalReference);
        AddDescriptionIfUseful(parts, transaction.Description);
        return string.Join(" - ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildTransactionStatusText(FinanceTransactionResponse transaction)
    {
        if (transaction.IsFlagged || IsActionStatus(transaction.AnomalyState))
        {
            return "Needs action";
        }

        return "Completed";
    }

    private static string BuildInvoiceActivityTitle(FinanceInvoiceResponse invoice)
    {
        var counterparty = FirstUsefulText(invoice.CounterpartyName);
        return string.IsNullOrWhiteSpace(counterparty)
            ? "Customer invoice"
            : $"Customer invoice for {counterparty}";
    }

    private static string BuildBillActivityTitle(FinanceBillResponse bill)
    {
        var counterparty = FirstUsefulText(bill.CounterpartyName);
        return string.IsNullOrWhiteSpace(counterparty)
            ? "Supplier bill"
            : $"Supplier bill from {counterparty}";
    }

    private static string BuildDocumentActivityDetail(string documentLabel, string number, string counterparty)
    {
        var parts = new List<string> { documentLabel };
        if (!string.IsNullOrWhiteSpace(number))
        {
            parts.Add($"Document {number}");
        }

        var counterpartyName = FirstBusinessName(counterparty);
        if (!string.IsNullOrWhiteSpace(counterpartyName))
        {
            parts.Add(counterpartyName);
        }

        return string.Join(" - ", parts);
    }

    private static string BuildPaymentActivityTitle(FinancePaymentResponse payment)
    {
        var counterparty = FirstBusinessName(payment.CounterpartyReference);
        return IsOutgoingPayment(payment)
            ? string.IsNullOrWhiteSpace(counterparty)
                ? "Supplier payment"
                : $"Supplier payment to {counterparty}"
            : string.IsNullOrWhiteSpace(counterparty)
                ? "Invoice payment"
                : $"Invoice payment from {counterparty}";
    }

    private static string BuildPaymentActivityDetail(FinancePaymentResponse payment)
    {
        var direction = IsOutgoingPayment(payment) ? "Outgoing" : "Incoming";
        var parts = new List<string> { $"{direction} payment" };
        AddIfPresent(parts, FirstBusinessName(payment.CounterpartyReference));
        AddReferenceIfUseful(parts, payment.CounterpartyReference);
        if (!string.IsNullOrWhiteSpace(payment.Method))
        {
            AddIfPresent(parts, FormatLabel(payment.Method));
        }

        return string.Join(" - ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsOutgoingPayment(FinancePaymentResponse payment)
    {
        var paymentType = Normalize(payment.PaymentType);
        return paymentType.Contains("out", StringComparison.Ordinal) ||
            paymentType.Contains("supplier", StringComparison.Ordinal) ||
            paymentType.Contains("payable", StringComparison.Ordinal);
    }

    private static string FormatPaymentStatus(string? status)
    {
        var normalized = Normalize(status);
        if (normalized.Contains("fail", StringComparison.Ordinal) || normalized.Contains("error", StringComparison.Ordinal))
        {
            return "Failed";
        }

        if (normalized.Contains("pending", StringComparison.Ordinal) || normalized.Contains("awaiting", StringComparison.Ordinal))
        {
            return "Pending";
        }

        if (normalized.Contains("settled", StringComparison.Ordinal) ||
            normalized.Contains("completed", StringComparison.Ordinal) ||
            normalized.Contains("paid", StringComparison.Ordinal) ||
            normalized.Contains("succeeded", StringComparison.Ordinal))
        {
            return "Completed";
        }

        return string.IsNullOrWhiteSpace(status) ? "Needs action" : FormatLabel(status);
    }

    private static IReadOnlyList<FinanceInsightItemViewModel> BuildLauraInsights(
        Guid companyId,
        FinanceCashPositionResponse? cash,
        FinanceMonthlySummaryResponse? monthly,
        bool cashRiskAlertVisible,
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillInboxRowResponse> billsAwaitingApproval,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention,
        IReadOnlyList<FinanceAnomalyWorkbenchItemResponse> openAnomalies)
    {
        var insights = new List<FinanceInsightItemViewModel>();

        var supplierWorkCount = supplierBillsDue.Count + billsAwaitingApproval.Count + paymentProposalsNeedingDecision.Count;
        if (supplierWorkCount > 0)
        {
            var overdueCount = supplierBillsDue.Count(IsOverdueBill);
            var nextStepPrefix = cashRiskAlertVisible ? "After cash, " : string.Empty;
            var explanation = $"{nextStepPrefix}supplier work is the next queue. Start with overdue bills, then review bills waiting to be posted.";
            if (paymentsNeedingAttention.Count > 0 || paymentProposalsNeedingDecision.Count > 0)
            {
                explanation += " Payment follow-up is for unsettled payments; payment proposals are decisions before money moves.";
            }

            insights.Add(CreateInsight("Supplier queue is next", explanation, "Review supplier queue", FinanceRoutes.SupplierBills, overdueCount > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning, "bill", companyId));
        }

        if (insights.Count == 0 && overdueInvoices.Count > 0)
        {
            insights.Add(CreateInsight("Collections need focus", "Customer work is the next queue. Start with past-due invoices before reviewing newer invoice drafts.", "Open invoices", FinanceRoutes.Invoices, FinanceKpiTone.Danger, "!", companyId));
        }

        if (insights.Count == 0 && openAnomalies.Count > 0)
        {
            insights.Add(CreateInsight("Investigate open issues", "Resolve the highest-confidence finance issues before they affect reporting or payment decisions.", "Investigate", FinanceRoutes.Issues, FinanceKpiTone.Danger, "risk", companyId));
        }

        if (insights.Count == 0 && paymentsNeedingAttention.Count > 0)
        {
            insights.Add(CreateInsight("Payments need follow-up", "Payment follow-up is for payments that are not settled cleanly. Resolve those before creating more payment proposals.", "Open payments", FinanceRoutes.Payments, FinanceKpiTone.Warning, "pay", companyId));
        }

        if (insights.Count == 0 && cash is not null && IsRisky(cash.RiskLevel))
        {
            insights.Add(CreateInsight("No other urgent finance work", "Start with the cash plan above. After that, review recent activity for anything unexpected.", "Open transactions", FinanceRoutes.Transactions, FinanceKpiTone.Warning, "txn", companyId));
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
            insights.Add(CreateInsight("No urgent finance risks detected", "Review recent transactions to keep records clean.", "Open transactions", FinanceRoutes.Transactions, FinanceKpiTone.Positive, "✓", companyId));
        }

        return insights.Take(1).ToArray();
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
            bills.FirstOrDefault()?.Currency,
            payments.FirstOrDefault()?.Currency,
            invoices.FirstOrDefault()?.Currency,
            transactions.FirstOrDefault()?.Currency,
            monthly?.ProfitAndLoss?.Currency,
            "SEK");

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static string FirstUsefulText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string FirstBusinessName(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsReferenceOnly(value))?.Trim() ?? string.Empty;

    private static bool IsReferenceOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.All(char.IsDigit) ||
            trimmed.All(character => char.IsDigit(character) || character is '-' or '_' or '/' or '.' or ' ');
    }

    private static void AddIfPresent(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }

    private static void AddReferenceIfUseful(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsReferenceOnly(value))
        {
            parts.Add($"Reference {value.Trim()}");
        }
    }

    private static void AddDescriptionIfUseful(ICollection<string> parts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsReferenceOnly(value))
        {
            return;
        }

        var description = value.Trim();
        if (!description.Contains("transfer", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(description);
        }
    }

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : FormatSentence(value);

    private static string FormatSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        var text = string.Join(
            " ",
            value.Trim()
                .Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return text.Length == 0
            ? "n/a"
            : string.Concat(text[..1].ToUpperInvariant(), text.Length == 1 ? string.Empty : text[1..]);
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "finance work";
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        if (values.Count == 2)
        {
            return $"{values[0]} and {values[1]}";
        }

        return $"{string.Join(", ", values.Take(values.Count - 1))}, and {values[^1]}";
    }

    private static string FormatDocumentStatusLabel(
        string? postingStatus,
        string? settlementStatus,
        string? dueStatus,
        string? documentKind,
        string? fallbackStatus)
    {
        var kind = Normalize(documentKind);
        var posting = Normalize(postingStatus);
        var settlement = Normalize(settlementStatus);
        var due = Normalize(dueStatus);
        var fallback = Normalize(fallbackStatus);

        if (kind is "credit_note" or "supplier_credit_note") return "Credit note";
        if (posting == "cancelled" || fallback.Contains("cancel", StringComparison.Ordinal) || fallback == "void") return "Cancelled";
        if (settlement == "paid" || fallback == "paid") return "Paid";
        if (settlement == "partially_paid") return "Partially paid";
        if (settlement == "credited") return "Credited";
        if (due == "overdue" || fallback == "overdue") return "Overdue";
        if (posting == "draft" || fallback is "draft" or "open") return "Draft";
        if (posting == "booked" || fallback == "approved") return "Booked";
        return string.IsNullOrWhiteSpace(fallbackStatus) ? "Unknown" : FormatLabel(fallbackStatus);
    }

    private static bool IsOverdueDocument(FinanceInvoiceResponse invoice) =>
        Normalize(invoice.DueStatus) == "overdue" ||
        (invoice.DueUtc.Date < DateTime.UtcNow.Date &&
            !IsClosedDocument(invoice.PostingStatus, invoice.SettlementStatus, invoice.DocumentKind, invoice.Status));

    private static bool IsClosedDocument(string? postingStatus, string? settlementStatus, string? documentKind, string? fallbackStatus)
    {
        var posting = Normalize(postingStatus);
        var settlement = Normalize(settlementStatus);
        var kind = Normalize(documentKind);
        var fallback = Normalize(fallbackStatus);

        return settlement is "paid" or "credited" ||
            kind is "credit_note" or "supplier_credit_note" ||
            posting == "cancelled" ||
            fallback.Contains("paid", StringComparison.Ordinal) ||
            fallback.Contains("settled", StringComparison.Ordinal) ||
            fallback.Contains("closed", StringComparison.Ordinal) ||
            fallback.Contains("resolved", StringComparison.Ordinal) ||
            fallback.Contains("cancel", StringComparison.Ordinal);
    }

    private static bool IsSupplierBillDueAttention(FinanceBillResponse bill) =>
        (Normalize(bill.DueStatus) is "due_soon" or "overdue") &&
        !IsClosedDocument(bill.PostingStatus, bill.SettlementStatus, bill.DocumentKind, bill.Status);

    private static bool IsOverdueBill(FinanceBillResponse bill) =>
        Normalize(bill.DueStatus) == "overdue";

    private static FinanceKpiTone ResolveSupplierBillDueTone(IReadOnlyList<FinanceBillResponse> bills) =>
        bills.Count == 0
            ? FinanceKpiTone.Positive
            : bills.Any(IsOverdueBill)
                ? FinanceKpiTone.Danger
                : FinanceKpiTone.Warning;

    private static FinanceKpiTone ResolveSupplierWorkTone(
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillInboxRowResponse> billsAwaitingApproval,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention)
    {
        if (supplierBillsDue.Count == 0 &&
            billsAwaitingApproval.Count == 0 &&
            paymentProposalsNeedingDecision.Count == 0 &&
            paymentsNeedingAttention.Count == 0)
        {
            return FinanceKpiTone.Positive;
        }

        return supplierBillsDue.Any(IsOverdueBill)
            ? FinanceKpiTone.Danger
            : FinanceKpiTone.Warning;
    }

    private static FinanceKpiTone ResolveCustomerWorkTone(
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews)
    {
        if (overdueInvoices.Count == 0 && invoiceReviews.Count == 0)
        {
            return FinanceKpiTone.Positive;
        }

        return overdueInvoices.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning;
    }

    private static bool IsSupplierPaymentProposalAttention(FinanceBillResponse bill)
    {
        var status = Normalize(bill.PaymentProposal?.Status);
        var exportStatus = Normalize(bill.PaymentProposal?.ExportStatus);
        return status is "awaiting_approval" or "ready_for_payment" ||
            exportStatus is "export_requested" or "failed";
    }

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

    private sealed record ReceivablesSnapshot(
        int OpenInvoiceCount,
        decimal OutstandingAmount,
        decimal OverdueAmount,
        int PartiallyPaidInvoiceCount,
        string Currency,
        bool IsMixedCurrency);

    private sealed record ReceivableInvoiceSnapshot(
        FinanceInvoiceResponse Invoice,
        decimal RemainingAmount);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
