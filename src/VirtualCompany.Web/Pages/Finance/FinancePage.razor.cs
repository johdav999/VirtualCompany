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
                Role = FinanceText["OverviewRoleFinanceManager"],
                Status = FinanceText["OverviewStatusActive"],
                Insights = BuildLauraInsights(companyId, cash, monthly, cashRiskAlert is not null, overdueInvoices, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies)
            },
            AttentionSummary = BuildAttentionSummary(companyId, currency, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies),
            AttentionItems = BuildAttentionItems(companyId, currency, supplierBillsDue, billsAwaitingApproval, paymentProposalsNeedingDecision, actionableReviews, paymentsNeedingAttention, openAnomalies),
            CashPosition = new FinanceCashPositionOverviewViewModel
            {
                Title = FinanceText["OverviewCashPlanSnapshot"],
                CurrentBalance = FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency),
                ComparisonText = cash?.EstimatedRunwayDays is int runway ? FinanceText["OverviewRunwayDays", LocalNumber.Integer(runway)] : FinanceText["OverviewRunwayUnavailable"],
                ContextTitle = FinanceText["OverviewPlanningContext"],
                ContextText = BuildCashPlanContext(hasValidMonthlySummary ? monthly : null, supplierBillsDue, paymentProposalsNeedingDecision, paymentsNeedingAttention, receivables, currency),
                RecommendedAction = BuildCashPlanRecommendedAction(cash?.RecommendedAction, supplierBillsDue, paymentProposalsNeedingDecision, paymentsNeedingAttention),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
                Tone = ResolveTone(cash?.RiskLevel)
            },
            MonthlySummary = new FinanceMonthlySummaryOverviewViewModel
            {
                IsAvailable = hasValidMonthlySummary,
                Period = FormatMonthlyPeriod(monthly),
                EmptyTitle = FinanceText["OverviewNoMonthlyReport"],
                EmptyMessage = FinanceText["OverviewNoMonthlyReportMessage"],
                TotalIncome = hasValidMonthlySummary ? FormatDashboardCurrency(monthly!.ProfitAndLoss.Revenue, monthly.ProfitAndLoss.Currency, currency) : string.Empty,
                TotalExpenses = hasValidMonthlySummary ? FormatDashboardCurrency(monthly!.ProfitAndLoss.Expenses, monthly.ProfitAndLoss.Currency, currency) : string.Empty,
                NetResult = hasValidMonthlySummary ? FormatDashboardCurrency(netResult, monthly!.ProfitAndLoss.Currency, currency) : string.Empty,
                CurrencyNote = hasValidMonthlySummary ? BuildCurrencyNote(monthly!.ProfitAndLoss.Currency, currency) : null,
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.MonthlySummary, companyId),
                ActionLabel = FinanceText["OverviewViewMonthlyReport"],
                Tone = netResult >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger
            },
            RecentActivity = recentActivity,
            HasNoFinanceActivity = !HasAnyFinanceActivity(cash, monthly, bills, billInbox, invoices, invoiceReviews, payments, transactions, anomalies)
        };
    }

    private FinanceCashRiskAlertViewModel? BuildCashRiskAlert(
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
            ? FinanceText[runway == 1 ? "RunwayDayOne" : "RunwayDaysMany", LocalNumber.Integer(runway)]
            : FinanceText["NotAvailable"];
        var currency = FirstNonEmpty(cash.Currency, fallbackCurrency, "SEK");
        var balance = FormatCurrency(cash.AvailableBalance, currency);
        var reason = cash.AvailableBalance <= 0m
            ? FinanceText["OverviewCashNoAvailableReason"]
            : cash.EstimatedRunwayDays <= 0
                ? FinanceText["OverviewCashZeroRunwayReason"]
                : string.IsNullOrWhiteSpace(cash.Rationale)
                    ? FinanceText["OverviewCashElevatedReason"]
                    : cash.Rationale;

        return new FinanceCashRiskAlertViewModel
        {
            Title = FinanceText["OverviewCashNeedsAttention"],
            Message = FinanceText["OverviewCashBalanceRunway", balance, runwayText],
            BalanceValue = balance,
            RunwayValue = runwayText,
            Reason = reason,
            SupportingText = receivables.OpenInvoiceCount > 0
                ? FinanceText["OverviewReceivablesNotCash", FormatReceivablesAmount(receivables)].Value
                : null,
            ActionLabel = FinanceText["OverviewReviewCashPlan"],
            Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.CashPosition, companyId),
            Tone = isCritical ? FinanceKpiTone.Danger : FinanceKpiTone.Warning
        };
    }

    private string FormatMonthlyPeriod(FinanceMonthlySummaryResponse? monthly)
    {
        if (monthly is null)
        {
            return FinanceText["OverviewNoMonthlyReport"];
        }

        var start = monthly.StartUtc;
        var endExclusive = monthly.EndUtc;
        if (!IsValidReportingDate(start) || !IsValidReportingDate(endExclusive) || endExclusive <= start)
        {
            return FinanceText["OverviewNoMonthlyReport"];
        }

        var endInclusive = endExclusive.AddDays(-1);
        if (start.Year == endInclusive.Year && start.Month == endInclusive.Month)
        {
            return LocalDateTime.Date(DateOnly.FromDateTime(start));
        }

        return FinanceText["FormattedDateRange", LocalDateTime.Date(DateOnly.FromDateTime(start)), LocalDateTime.Date(DateOnly.FromDateTime(endInclusive))];
    }

    private FinanceAttentionSummaryViewModel BuildAttentionSummary(
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
                Title = FinanceText["OverviewNothingUrgent"],
                Message = FinanceText["OverviewNoFinanceActions"],
                ActionLabel = FinanceText["OverviewReviewRecentActivity"],
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Transactions, companyId),
                Tone = FinanceKpiTone.Positive
            };
        }

        var areas = new List<string>();
        if (supplierItemCount > 0) areas.Add(FinanceText["OverviewSupplierWorkArea"]);
        if (paymentItemCount > 0) areas.Add(FinanceText["OverviewPaymentsArea"]);
        if (reviewItemCount > 0) areas.Add(FinanceText["OverviewInvoiceReviewsArea"]);
        if (issueItemCount > 0) areas.Add(FinanceText["OverviewIssuesArea"]);

        var route = supplierItemCount > 0
            ? FinanceRoutes.SupplierBills
            : paymentItemCount > 0
                ? FinanceRoutes.Payments
                : reviewItemCount > 0
                    ? FinanceRoutes.Reviews
                    : FinanceRoutes.Issues;

        return new FinanceAttentionSummaryViewModel
        {
            Title = FinanceText[totalCount == 1 ? "AttentionItemOne" : "AttentionItemsMany", LocalNumber.Integer(totalCount)],
            Message = FinanceText["OverviewAttentionAcross", FormatList(areas)],
            Amount = FormatCurrency(totalAmount, currency),
            ActionLabel = FinanceText["OverviewReviewQueue"],
            Href = FinanceRoutes.WithCompanyContext(route, companyId),
            Tone = supplierBillsDue.Any(IsOverdueBill) || openAnomalies.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning
        };
    }

    private IReadOnlyList<FinanceAttentionItemViewModel> BuildAttentionItems(
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
                Label = FinanceText["OverviewSupplierBillsDue"],
                Count = supplierBillsDue.Count,
                Amount = supplierBillsDue.Count == 0 ? null : FormatCurrency(supplierBillsDue.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = FinanceText["OverviewReviewDueBills"],
                Tone = ResolveSupplierBillDueTone(supplierBillsDue),
                Icon = "bill"
            },
            new()
            {
                Label = FinanceText["OverviewSupplierBillsApprove"],
                Count = billsAwaitingApproval.Count,
                Amount = billsAwaitingApproval.Count == 0 ? null : FormatCurrency(billsAwaitingApproval.Sum(item => item.Amount ?? 0m), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = FinanceText["OverviewReviewSupplierBills"],
                Tone = billsAwaitingApproval.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "bill"
            },
            new()
            {
                Label = FinanceText["OverviewPaymentProposals"],
                Count = paymentProposalsNeedingDecision.Count,
                Amount = paymentProposalsNeedingDecision.Count == 0 ? null : FormatCurrency(paymentProposalsNeedingDecision.Sum(item => item.PaymentProposal?.Amount ?? item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.SupplierBills, companyId),
                CtaLabel = FinanceText["OverviewReviewProposals"],
                Tone = paymentProposalsNeedingDecision.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "pay"
            },
            new()
            {
                Label = FinanceText["OverviewPaymentsAttention"],
                Count = paymentsNeedingAttention.Count,
                Amount = paymentsNeedingAttention.Count == 0 ? null : FormatCurrency(paymentsNeedingAttention.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Payments, companyId),
                CtaLabel = FinanceText["OverviewReviewPayments"],
                Tone = paymentsNeedingAttention.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "pay"
            },
            new()
            {
                Label = FinanceText["OverviewInvoicesReview"],
                Count = invoiceReviews.Count,
                Amount = invoiceReviews.Count == 0 ? null : FormatCurrency(invoiceReviews.Sum(item => item.Amount), currency),
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Reviews, companyId),
                CtaLabel = FinanceText["OverviewReviewInvoices"],
                Tone = invoiceReviews.Count > 0 ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
                Icon = "inv"
            },
            new()
            {
                Label = FinanceText["OverviewIssuesInvestigate"],
                Count = openAnomalies.Count,
                Href = FinanceRoutes.WithCompanyContext(FinanceRoutes.Issues, companyId),
                CtaLabel = FinanceText["OverviewViewIssues"],
                Tone = openAnomalies.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Positive,
                Icon = "risk"
            }
        };

        return items.Where(item => item.Count > 0).ToArray();
    }

    private IReadOnlyList<FinanceKpiViewModel> BuildOverviewKpis(
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
            CreateKpi(FinanceText["OverviewCashPosition"], FormatCurrency(cash?.AvailableBalance ?? 0m, cash?.Currency ?? currency), cash is null ? FinanceText["OverviewNoCashSnapshot"] : FinanceText["OverviewRiskComparison", FormatOverviewRisk(cash.RiskLevel)], FinanceRoutes.CashPosition, ResolveTone(cash?.RiskLevel), "$", companyId, FinanceKpiEmphasis.Primary)
        };

        if (hasValidMonthlySummary)
        {
            kpis.Add(CreateKpi(FinanceText["OverviewIncomingMonth"], FormatDashboardCurrency(monthly!.ProfitAndLoss.Revenue, monthly.ProfitAndLoss.Currency, currency), BuildCurrencyAwareLabel(FinanceText["OverviewRecognizedRevenue"], monthly.ProfitAndLoss.Currency, currency), FinanceRoutes.MonthlySummary, FinanceKpiTone.Positive, "in", companyId));
            kpis.Add(CreateKpi(FinanceText["OverviewOutgoingMonth"], FormatDashboardCurrency(monthly.ProfitAndLoss.Expenses, monthly.ProfitAndLoss.Currency, currency), BuildCurrencyAwareLabel(FinanceText["OverviewRecordedExpenses"], monthly.ProfitAndLoss.Currency, currency), FinanceRoutes.MonthlySummary, FinanceKpiTone.Warning, "out", companyId));
        }

        if (receivables.OpenInvoiceCount > 0)
        {
            kpis.Add(CreateKpi(
                FinanceText["OverviewReceivables"],
                LocalNumber.Integer(receivables.OpenInvoiceCount),
                BuildReceivablesKpiLabel(receivables),
                FinanceRoutes.Invoices,
                receivables.OverdueAmount > 0m ? FinanceKpiTone.Danger : FinanceKpiTone.Warning,
                "inv",
                companyId));
        }

        if (supplierWorkCount > 0)
        {
            kpis.Add(CreateKpi(FinanceText["OverviewSupplierWork"], LocalNumber.Integer(supplierWorkCount), FinanceText["OverviewSupplierWorkComparison", FormatCurrency(supplierWorkAmount, currency)], FinanceRoutes.SupplierBills, supplierWorkTone, "bill", companyId));
        }

        if (customerWorkCount > 0)
        {
            kpis.Add(CreateKpi(FinanceText["OverviewCustomerActions"], LocalNumber.Integer(customerWorkCount), BuildCustomerWorkKpiLabel(overdueInvoices, actionableReviews, customerWorkAmount, currency), overdueInvoices.Count > 0 ? FinanceRoutes.Invoices : FinanceRoutes.Reviews, ResolveCustomerWorkTone(overdueInvoices, actionableReviews), "!", companyId));
        }

        if (openAnomalies.Count > 0)
        {
            kpis.Add(CreateKpi(FinanceText["OverviewOpenIssues"], LocalNumber.Integer(openAnomalies.Count), FinanceText["OverviewNeedInvestigation"], FinanceRoutes.Issues, FinanceKpiTone.Danger, "risk", companyId));
        }

        return kpis;
    }

    private string BuildCustomerWorkKpiLabel(
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews,
        decimal amount,
        string currency)
    {
        var count = overdueInvoices.Count + invoiceReviews.Count;
        if (count == 0)
        {
            return FinanceText["OverviewNoCustomerActions"];
        }

        if (overdueInvoices.Count > 0 && invoiceReviews.Count > 0)
        {
            return FinanceText["OverviewCollectionsAndReviews", FormatCurrency(amount, currency)];
        }

        return overdueInvoices.Count > 0
            ? FinanceText["OverviewPastDueInvoices", FormatCurrency(amount, currency)]
            : FinanceText["OverviewInvoiceReviewsAmount", FormatCurrency(amount, currency)];
    }

    private string BuildReceivablesKpiLabel(ReceivablesSnapshot receivables)
    {
        var parts = new List<string>
        {
            FinanceText["OverviewOutstanding", FormatReceivablesAmount(receivables)]
        };

        parts.Add(receivables.OverdueAmount > 0m
            ? receivables.IsMixedCurrency
                ? FinanceText["OverviewSomeOverdue"]
                : FinanceText["OverviewOverdueAmount", FormatCurrency(receivables.OverdueAmount, receivables.Currency)]
            : FinanceText["OverviewNothingOverdue"]);

        if (receivables.PartiallyPaidInvoiceCount > 0)
        {
            parts.Add(FinanceText["OverviewIncludesPartialInvoices"]);
        }

        return string.Join("; ", parts);
    }

    private string FormatReceivablesAmount(ReceivablesSnapshot receivables) =>
        receivables.IsMixedCurrency
            ? FinanceText["OverviewMultipleCurrencies"]
            : FormatCurrency(receivables.OutstandingAmount, receivables.Currency);

    private string BuildCashPlanContext(
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
                    ? FinanceText["OverviewExpectedNotCurrentCash", FormatReceivablesAmount(receivables)]
                    : string.Empty;
                return FinanceText["OverviewSupplierSequenceMonthly", LocalNumber.Integer(supplierCount), FormatCurrency(supplierAmount, currency), incoming, outgoing, receivablesContext];
            }

            return FinanceText["OverviewCashCompare", incoming, outgoing];
        }

        if (supplierCount > 0)
        {
            var receivablesContext = receivables.OpenInvoiceCount > 0
                ? FinanceText["OverviewOutstandingNotCash", FormatReceivablesAmount(receivables)]
                : string.Empty;
            return FinanceText["OverviewSupplierSequenceNoMonthly", LocalNumber.Integer(supplierCount), FormatCurrency(supplierAmount, currency), receivablesContext];
        }

        if (receivables.OpenInvoiceCount > 0)
        {
            return FinanceText["OverviewReceivablesContext", FormatReceivablesAmount(receivables)];
        }

        return FinanceText["OverviewNoMonthlyCashContext"];
    }

    private string BuildCashPlanRecommendedAction(
        string? recommendedAction,
        IReadOnlyList<FinanceBillResponse> supplierBillsDue,
        IReadOnlyList<FinanceBillResponse> paymentProposalsNeedingDecision,
        IReadOnlyList<FinancePaymentResponse> paymentsNeedingAttention)
    {
        if (supplierBillsDue.Count > 0 || paymentProposalsNeedingDecision.Count > 0 || paymentsNeedingAttention.Count > 0)
        {
            return FinanceText["OverviewCashPlanDecision"];
        }

        return FormatRecommendedCashAction(recommendedAction);
    }

    private ReceivablesSnapshot BuildReceivablesSnapshot(
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

    private decimal CalculateReceivableRemainingAmount(FinanceInvoiceResponse invoice)
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

    private bool IsClosedReceivableInvoice(FinanceInvoiceResponse invoice)
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

    private bool IsPartiallyPaidInvoice(FinanceInvoiceResponse invoice) =>
        Normalize(invoice.SettlementStatus) == "partially_paid" ||
        Normalize(invoice.Status) == "partially_paid" ||
        invoice.PaymentContext is { PaidAmount: > 0m, RemainingAmount: > 0m };

    private bool IsValidReportingDate(DateTime value) =>
        value.Year >= 1900;

    private bool HasValidMonthlySummary(FinanceMonthlySummaryResponse? monthly) =>
        monthly is not null &&
        IsValidReportingDate(monthly.StartUtc) &&
        IsValidReportingDate(monthly.EndUtc) &&
        monthly.EndUtc > monthly.StartUtc;

    private string FormatDashboardCurrency(decimal amount, string sourceCurrency, string dashboardCurrency)
    {
        var currency = FirstNonEmpty(sourceCurrency, dashboardCurrency, "SEK");
        return FormatCurrency(amount, currency);
    }

    private string BuildCurrencyAwareLabel(string label, string sourceCurrency, string dashboardCurrency) =>
        IsDifferentCurrency(sourceCurrency, dashboardCurrency)
            ? FinanceText["OverviewCurrencyNotConverted", label, sourceCurrency]
            : label;

    private string? BuildCurrencyNote(string sourceCurrency, string dashboardCurrency) =>
        IsDifferentCurrency(sourceCurrency, dashboardCurrency)
            ? FinanceText["OverviewMonthlyCurrencyNote", sourceCurrency, dashboardCurrency].Value
            : null;

    private bool IsDifferentCurrency(string sourceCurrency, string dashboardCurrency) =>
        !string.IsNullOrWhiteSpace(sourceCurrency) &&
        !string.IsNullOrWhiteSpace(dashboardCurrency) &&
        !string.Equals(sourceCurrency.Trim(), dashboardCurrency.Trim(), StringComparison.OrdinalIgnoreCase);

    private string FormatRecommendedCashAction(string? action)
    {
        var normalized = Normalize(action).Replace("-", "_", StringComparison.Ordinal);
        return normalized switch
        {
            "" => FinanceText["OverviewActionReviewCashPayables"],
            "review_cash_plan" => FinanceText["OverviewReviewCashPlan"],
            "monitor_cash" or "monitor_cash_position" => FinanceText["OverviewActionMonitorCash"],
            "reduce_outgoing_payments" => FinanceText["OverviewActionReviewOutgoing"],
            "collect_overdue_invoices" => FinanceText["OverviewActionCollectOverdue"],
            _ => FormatSentence(action)
        };
    }

    private async Task<T> UseFallbackWhenFinanceIsNotInitializedAsync<T>(
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

    private FinanceKpiViewModel CreateKpi(
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

    private IReadOnlyList<RecentFinanceActivityViewModel> BuildRecentActivity(
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
            DateText = LocalDateTime.Date(DateOnly.FromDateTime(transaction.TransactionUtc)),
            StatusText = BuildTransactionStatusText(transaction),
            Href = FinanceRoutes.BuildTransactionDetailPath(transaction.Id, companyId),
            Tone = transaction.Amount >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Danger,
            Icon = "txn",
            SortDateUtc = transaction.TransactionUtc
        })
        .Concat(invoices.Select(invoice => new RecentFinanceActivityViewModel
        {
            Title = BuildInvoiceActivityTitle(invoice),
            Detail = BuildDocumentActivityDetail(FinanceText["OverviewCustomerInvoice"], invoice.InvoiceNumber, invoice.CounterpartyName),
            Amount = FormatCurrency(invoice.Amount, invoice.Currency),
            DateText = LocalDateTime.Date(DateOnly.FromDateTime(invoice.IssuedUtc)),
            StatusText = FormatDocumentStatusLabel(invoice.PostingStatus, invoice.SettlementStatus, invoice.DueStatus, invoice.DocumentKind, invoice.Status),
            Href = FinanceRoutes.BuildInvoiceDetailPath(invoice.Id, companyId),
            Tone = IsClosedDocument(invoice.PostingStatus, invoice.SettlementStatus, invoice.DocumentKind, invoice.Status) ? FinanceKpiTone.Positive : FinanceKpiTone.Warning,
            Icon = "inv",
            SortDateUtc = invoice.IssuedUtc
        }))
        .Concat(bills.Select(bill => new RecentFinanceActivityViewModel
        {
            Title = BuildBillActivityTitle(bill),
            Detail = BuildDocumentActivityDetail(FinanceText["OverviewSupplierBill"], bill.BillNumber, bill.CounterpartyName),
            Amount = FormatCurrency(bill.Amount, bill.Currency),
            DateText = LocalDateTime.Date(DateOnly.FromDateTime(bill.ReceivedUtc)),
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
            DateText = LocalDateTime.Date(DateOnly.FromDateTime(payment.UpdatedUtc)),
            StatusText = FormatPaymentStatus(payment.Status),
            Href = FinanceRoutes.BuildPaymentDetailPath(payment.Id, companyId),
            Tone = IsPaymentAttentionStatus(payment.Status) ? FinanceKpiTone.Warning : FinanceKpiTone.Positive,
            Icon = "pay",
            SortDateUtc = payment.UpdatedUtc
        }))
        .OrderByDescending(item => item.SortDateUtc)
        .Take(6)
        .ToArray();

    private string BuildTransactionActivityTitle(FinanceTransactionResponse transaction)
    {
        var counterparty = FirstBusinessName(transaction.CounterpartyName);
        if (transaction.BillId.HasValue)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? FinanceText["OverviewSupplierPayment"]
                : FinanceText["OverviewSupplierBillPaymentTo", counterparty];
        }

        if (transaction.InvoiceId.HasValue)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? FinanceText["OverviewInvoicePaymentReceived"]
                : FinanceText["OverviewInvoicePaymentFrom", counterparty];
        }

        var isOutgoing = transaction.Amount < 0m || Normalize(transaction.TransactionType).Contains("out", StringComparison.Ordinal);
        if (isOutgoing)
        {
            return string.IsNullOrWhiteSpace(counterparty)
                ? FinanceText["OverviewOutgoingPayment"]
                : FinanceText["OverviewOutgoingPaymentTo", counterparty];
        }

        return string.IsNullOrWhiteSpace(counterparty)
            ? FinanceText["OverviewIncomingPayment"]
            : FinanceText["OverviewIncomingPaymentFrom", counterparty];
    }

    private string BuildTransactionActivityDetail(FinanceTransactionResponse transaction)
    {
        var parts = new List<string>
        {
            transaction.Amount < 0m ? FinanceText["OverviewOutgoingTransaction"] : FinanceText["OverviewIncomingTransaction"]
        };
        AddIfPresent(parts, FirstBusinessName(transaction.CounterpartyName));
        AddIfPresent(parts, transaction.AccountName);
        AddReferenceIfUseful(parts, transaction.ExternalReference);
        AddDescriptionIfUseful(parts, transaction.Description);
        return string.Join(" - ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private string BuildTransactionStatusText(FinanceTransactionResponse transaction)
    {
        if (transaction.IsFlagged || IsActionStatus(transaction.AnomalyState))
        {
            return FinanceText["OverviewNeedsAction"];
        }

        return FinanceText["OverviewCompleted"];
    }

    private string BuildInvoiceActivityTitle(FinanceInvoiceResponse invoice)
    {
        var counterparty = FirstUsefulText(invoice.CounterpartyName);
        return string.IsNullOrWhiteSpace(counterparty)
            ? FinanceText["OverviewCustomerInvoice"]
            : FinanceText["OverviewCustomerInvoiceFor", counterparty];
    }

    private string BuildBillActivityTitle(FinanceBillResponse bill)
    {
        var counterparty = FirstUsefulText(bill.CounterpartyName);
        return string.IsNullOrWhiteSpace(counterparty)
            ? FinanceText["OverviewSupplierBill"]
            : FinanceText["OverviewSupplierBillFrom", counterparty];
    }

    private string BuildDocumentActivityDetail(string documentLabel, string number, string counterparty)
    {
        var parts = new List<string> { documentLabel };
        if (!string.IsNullOrWhiteSpace(number))
        {
            parts.Add(FinanceText["OverviewDocumentNumber", number]);
        }

        var counterpartyName = FirstBusinessName(counterparty);
        if (!string.IsNullOrWhiteSpace(counterpartyName))
        {
            parts.Add(counterpartyName);
        }

        return string.Join(" - ", parts);
    }

    private string BuildPaymentActivityTitle(FinancePaymentResponse payment)
    {
        var counterparty = FirstBusinessName(payment.CounterpartyReference);
        return IsOutgoingPayment(payment)
            ? string.IsNullOrWhiteSpace(counterparty)
                ? FinanceText["OverviewSupplierPayment"]
                : FinanceText["OverviewSupplierPaymentTo", counterparty]
            : string.IsNullOrWhiteSpace(counterparty)
                ? FinanceText["OverviewInvoicePayment"]
                : FinanceText["OverviewInvoicePaymentFrom", counterparty];
    }

    private string BuildPaymentActivityDetail(FinancePaymentResponse payment)
    {
        var parts = new List<string>
        {
            IsOutgoingPayment(payment) ? FinanceText["OverviewOutgoingPayment"] : FinanceText["OverviewIncomingPayment"]
        };
        AddIfPresent(parts, FirstBusinessName(payment.CounterpartyReference));
        AddReferenceIfUseful(parts, payment.CounterpartyReference);
        if (!string.IsNullOrWhiteSpace(payment.Method))
        {
            AddIfPresent(parts, FormatLabel(payment.Method));
        }

        return string.Join(" - ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private bool IsOutgoingPayment(FinancePaymentResponse payment)
    {
        var paymentType = Normalize(payment.PaymentType);
        return paymentType.Contains("out", StringComparison.Ordinal) ||
            paymentType.Contains("supplier", StringComparison.Ordinal) ||
            paymentType.Contains("payable", StringComparison.Ordinal);
    }

    private string FormatPaymentStatus(string? status)
    {
        var normalized = Normalize(status);
        if (normalized.Contains("fail", StringComparison.Ordinal) || normalized.Contains("error", StringComparison.Ordinal))
        {
            return FinanceText["OverviewFailed"];
        }

        if (normalized.Contains("pending", StringComparison.Ordinal) || normalized.Contains("awaiting", StringComparison.Ordinal))
        {
            return FinanceText["OverviewPending"];
        }

        if (normalized.Contains("settled", StringComparison.Ordinal) ||
            normalized.Contains("completed", StringComparison.Ordinal) ||
            normalized.Contains("paid", StringComparison.Ordinal) ||
            normalized.Contains("succeeded", StringComparison.Ordinal))
        {
            return FinanceText["OverviewCompleted"];
        }

        return string.IsNullOrWhiteSpace(status) ? FinanceText["OverviewNeedsAction"] : FormatLabel(status);
    }

    private IReadOnlyList<FinanceInsightItemViewModel> BuildLauraInsights(
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
            var nextStepPrefix = cashRiskAlertVisible ? FinanceText["OverviewAfterCash"].Value : string.Empty;
            var explanation = FinanceText["OverviewSupplierQueueMessage", nextStepPrefix].Value;
            if (paymentsNeedingAttention.Count > 0 || paymentProposalsNeedingDecision.Count > 0)
            {
                explanation += FinanceText["OverviewPaymentFollowUpMessage"];
            }

            insights.Add(CreateInsight(FinanceText["OverviewSupplierQueueTitle"], explanation, FinanceText["OverviewReviewSupplierQueue"], FinanceRoutes.SupplierBills, overdueCount > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning, "bill", companyId));
        }

        if (insights.Count == 0 && overdueInvoices.Count > 0)
        {
            insights.Add(CreateInsight(FinanceText["OverviewCollectionsTitle"], FinanceText["OverviewCollectionsMessage"], FinanceText["OverviewOpenInvoices"], FinanceRoutes.Invoices, FinanceKpiTone.Danger, "!", companyId));
        }

        if (insights.Count == 0 && openAnomalies.Count > 0)
        {
            insights.Add(CreateInsight(FinanceText["OverviewInvestigateTitle"], FinanceText["OverviewInvestigateMessage"], FinanceText["OverviewInvestigate"], FinanceRoutes.Issues, FinanceKpiTone.Danger, "risk", companyId));
        }

        if (insights.Count == 0 && paymentsNeedingAttention.Count > 0)
        {
            insights.Add(CreateInsight(FinanceText["OverviewPaymentsFollowUpTitle"], FinanceText["OverviewPaymentsFollowUpMessage"], FinanceText["OverviewOpenPayments"], FinanceRoutes.Payments, FinanceKpiTone.Warning, "pay", companyId));
        }

        if (insights.Count == 0 && cash is not null && IsRisky(cash.RiskLevel))
        {
            insights.Add(CreateInsight(FinanceText["OverviewNoOtherUrgentTitle"], FinanceText["OverviewNoOtherUrgentMessage"], FinanceText["OverviewOpenTransactions"], FinanceRoutes.Transactions, FinanceKpiTone.Warning, "txn", companyId));
        }

        if (insights.Count == 0 && monthly?.ProfitAndLoss is { } pnl)
        {
            var message = pnl.NetResult >= 0m
                ? FinanceText["OverviewProfitableMessage"].Value
                : FinanceText["OverviewLossMessage"].Value;
            insights.Add(CreateInsight(FinanceText["OverviewMonthPosture"], message, FinanceText["OverviewOpenMonthlySummary"], FinanceRoutes.MonthlySummary, pnl.NetResult >= 0m ? FinanceKpiTone.Positive : FinanceKpiTone.Warning, "ok", companyId));
        }

        if (insights.Count == 0)
        {
            insights.Add(CreateInsight(FinanceText["OverviewNoUrgentTitle"], FinanceText["OverviewNoUrgentMessage"], FinanceText["OverviewOpenTransactions"], FinanceRoutes.Transactions, FinanceKpiTone.Positive, "ok", companyId));
        }

        return insights.Take(1).ToArray();
    }

    private FinanceInsightItemViewModel CreateInsight(
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

    private bool HasAnyFinanceActivity(
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

    private string ResolveCurrency(
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

    private string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private string FirstUsefulText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private string FirstBusinessName(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && !IsReferenceOnly(value))?.Trim() ?? string.Empty;

    private bool IsReferenceOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.All(char.IsDigit) ||
            trimmed.All(character => char.IsDigit(character) || character is '-' or '_' or '/' or '.' or ' ');
    }

    private void AddIfPresent(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add(value.Trim());
        }
    }

    private void AddReferenceIfUseful(ICollection<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && IsReferenceOnly(value))
        {
            parts.Add(FinanceText["OverviewReference", value.Trim()]);
        }
    }

    private void AddDescriptionIfUseful(ICollection<string> parts, string? value)
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

    private string FormatCurrency(decimal amount, string currency) => LocalMoney.Format(amount, currency);

    private string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? FinanceText["OverviewNotAvailableShort"]
            : FormatSentence(value);

    private string FormatOverviewRisk(string? value) =>
        Normalize(value) switch
        {
            "low" => FinanceText["OverviewRiskLow"],
            "medium" or "warning" => FinanceText["OverviewRiskMedium"],
            "high" => FinanceText["OverviewRiskHigh"],
            "critical" => FinanceText["OverviewRiskCritical"],
            _ => FinanceText["OverviewRiskUnknown"]
        };

    private string FormatSentence(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FinanceText["OverviewNotAvailableShort"];
        }

        var text = string.Join(
            " ",
            value.Trim()
                .Replace("-", "_", StringComparison.Ordinal)
                .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return text.Length == 0
            ? FinanceText["OverviewNotAvailableShort"]
            : string.Concat(text[..1].ToUpperInvariant(), text.Length == 1 ? string.Empty : text[1..]);
    }

    private string FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return FinanceText["OverviewFinanceWork"];
        }

        if (values.Count == 1)
        {
            return values[0];
        }

        if (values.Count == 2)
        {
            return FinanceText["OverviewListTwo", values[0], values[1]];
        }

        return FinanceText["OverviewListMany", string.Join(", ", values.Take(values.Count - 1)), values[^1]];
    }

    private string FormatDocumentStatusLabel(
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

        if (kind is "credit_note" or "supplier_credit_note") return FinanceText["OverviewCreditNote"];
        if (posting == "cancelled" || fallback.Contains("cancel", StringComparison.Ordinal) || fallback == "void") return FinanceText["OverviewCancelled"];
        if (settlement == "paid" || fallback == "paid") return FinanceText["OverviewPaid"];
        if (settlement == "partially_paid") return FinanceText["OverviewPartiallyPaid"];
        if (settlement == "credited") return FinanceText["OverviewCredited"];
        if (due == "overdue" || fallback == "overdue") return FinanceText["OverviewStatusOverdue"];
        if (posting == "draft" || fallback is "draft" or "open") return FinanceText["OverviewDraft"];
        if (posting == "booked" || fallback == "approved") return FinanceText["OverviewBooked"];
        return string.IsNullOrWhiteSpace(fallbackStatus) ? FinanceText["OverviewUnknown"] : FormatLabel(fallbackStatus);
    }

    private bool IsOverdueDocument(FinanceInvoiceResponse invoice) =>
        Normalize(invoice.DueStatus) == "overdue" ||
        (invoice.DueUtc.Date < DateTime.UtcNow.Date &&
            !IsClosedDocument(invoice.PostingStatus, invoice.SettlementStatus, invoice.DocumentKind, invoice.Status));

    private bool IsClosedDocument(string? postingStatus, string? settlementStatus, string? documentKind, string? fallbackStatus)
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

    private bool IsSupplierBillDueAttention(FinanceBillResponse bill) =>
        (Normalize(bill.DueStatus) is "due_soon" or "overdue") &&
        !IsClosedDocument(bill.PostingStatus, bill.SettlementStatus, bill.DocumentKind, bill.Status);

    private bool IsOverdueBill(FinanceBillResponse bill) =>
        Normalize(bill.DueStatus) == "overdue";

    private FinanceKpiTone ResolveSupplierBillDueTone(IReadOnlyList<FinanceBillResponse> bills) =>
        bills.Count == 0
            ? FinanceKpiTone.Positive
            : bills.Any(IsOverdueBill)
                ? FinanceKpiTone.Danger
                : FinanceKpiTone.Warning;

    private FinanceKpiTone ResolveSupplierWorkTone(
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

    private FinanceKpiTone ResolveCustomerWorkTone(
        IReadOnlyList<FinanceInvoiceResponse> overdueInvoices,
        IReadOnlyList<FinanceInvoiceReviewListItemResponse> invoiceReviews)
    {
        if (overdueInvoices.Count == 0 && invoiceReviews.Count == 0)
        {
            return FinanceKpiTone.Positive;
        }

        return overdueInvoices.Count > 0 ? FinanceKpiTone.Danger : FinanceKpiTone.Warning;
    }

    private bool IsSupplierPaymentProposalAttention(FinanceBillResponse bill)
    {
        var status = Normalize(bill.PaymentProposal?.Status);
        var exportStatus = Normalize(bill.PaymentProposal?.ExportStatus);
        return status is "awaiting_approval" or "ready_for_payment" ||
            exportStatus is "export_requested" or "failed";
    }

    private bool IsRisky(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Contains("high", StringComparison.Ordinal) ||
            normalized.Contains("critical", StringComparison.Ordinal) ||
            normalized.Contains("medium", StringComparison.Ordinal) ||
            normalized.Contains("warning", StringComparison.Ordinal);
    }

    private bool IsClosedStatus(string? value)
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

    private bool IsActionStatus(string? value)
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

    private bool IsPaymentAttentionStatus(string? value)
    {
        var normalized = Normalize(value);
        return !string.IsNullOrWhiteSpace(normalized) &&
            !normalized.Contains("settled", StringComparison.Ordinal) &&
            !normalized.Contains("completed", StringComparison.Ordinal) &&
            !normalized.Contains("paid", StringComparison.Ordinal) &&
            !normalized.Contains("succeeded", StringComparison.Ordinal);
    }

    private FinanceKpiTone ResolveTone(string? value)
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

    private string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
