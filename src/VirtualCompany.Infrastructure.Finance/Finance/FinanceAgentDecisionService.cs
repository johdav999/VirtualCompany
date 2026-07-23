using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAgentDecisionService(
    VirtualCompanyDbContext db,
    IFinanceAgentAnalysisService analysis,
    IFinanceReadService financeRead,
    IFinanceCashPositionWorkflowService cashWorkflow,
    IReportingPeriodCloseService periodClose,
    IFinanceSupplierPaymentProposalService paymentProposals,
    IAgentReasoningGateway reasoning,
    IAgentHandoffService handoffs) : IFinanceAgentDecisionService
{
    public async Task<IReadOnlyList<FinanceClosePeriodOptionDto>> ListClosePeriodsAsync(Guid companyId,
        CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        return await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartUtc)
            .Take(36)
            .Select(x => new FinanceClosePeriodOptionDto(x.Id, x.Name, x.StartUtc, x.EndUtc,
                x.IsClosed ? "closed" : x.IsReportingLocked ? "reporting_locked" : "open",
                x.IsClosed, x.IsReportingLocked))
            .ToListAsync(ct);
    }

    public async Task<FinanceCashScenarioAnalysisResult> AnalyzeCashAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, FinanceCashScenarioAnalysisRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        EnsureNonNegative(request.UpsideAdditionalInflows, nameof(request.UpsideAdditionalInflows));
        EnsureNonNegative(request.DownsideDelayedInflows, nameof(request.DownsideDelayedInflows));
        EnsureNonNegative(request.DownsideAdditionalOutflows, nameof(request.DownsideAdditionalOutflows));
        var now = NormalizeUtc(request.AsOfUtc ?? DateTime.UtcNow);
        var position = await cashWorkflow.EvaluateAsync(
            new EvaluateFinanceCashPositionWorkflowCommand(companyId, AgentId: agentId,
                CorrelationId: $"finance-ai-cash:{agentId:N}:{now:yyyyMMddHH}"), ct);

        var data = await LoadProjectionDataAsync(companyId, now, horizon, ct);
        var projection = FinanceIntelligenceHeuristics.Evaluate(new FinanceIntelligenceInputDto(
            companyId, now, position.AvailableBalance, position.Currency, data.Invoices, data.Bills, [], data.History));
        var authoritative = horizon <= 7 ? projection.SevenDayProjection : projection.ThirtyDayProjection;
        if (horizon is not (7 or 30))
            authoritative = BuildProjection(position.AvailableBalance, position.Currency, data.Invoices, data.Bills, now, horizon);

        var sourceIds = data.SourceIds.Prepend($"finance-cash-position:{now:O}").ToArray();
        var baseline = Scenario("baseline", authoritative.StartingCash, authoritative.ProjectedInflows,
            authoritative.ProjectedOutflows, authoritative.EndingCash, 0m, position.Currency,
            ["Open invoices and bills due within the selected horizon are included at their authoritative outstanding amounts."], sourceIds);
        var upsideEnding = authoritative.EndingCash + request.UpsideAdditionalInflows;
        var upside = Scenario("upside", authoritative.StartingCash,
            authoritative.ProjectedInflows + request.UpsideAdditionalInflows, authoritative.ProjectedOutflows,
            upsideEnding, upsideEnding - authoritative.EndingCash, position.Currency,
            [$"User-selected additional inflows: {request.UpsideAdditionalInflows.ToString(CultureInfo.InvariantCulture)} {position.Currency}."], sourceIds);
        var delayed = Math.Min(authoritative.ProjectedInflows, request.DownsideDelayedInflows);
        var downsideEnding = authoritative.StartingCash + authoritative.ProjectedInflows - delayed -
                             authoritative.ProjectedOutflows - request.DownsideAdditionalOutflows;
        var downside = Scenario("downside", authoritative.StartingCash, authoritative.ProjectedInflows - delayed,
            authoritative.ProjectedOutflows + request.DownsideAdditionalOutflows, downsideEnding,
            downsideEnding - authoritative.EndingCash, position.Currency,
            [$"User-selected delayed inflows: {delayed.ToString(CultureInfo.InvariantCulture)} {position.Currency}.",
             $"User-selected additional outflows: {request.DownsideAdditionalOutflows.ToString(CultureInfo.InvariantCulture)} {position.Currency}."], sourceIds);

        var advice = await analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.CashLiquidity, HorizonDays: horizon,
                Objective: request.Objective, AsOfUtc: now), ct);
        var freshness = await CashFreshnessWarningsAsync(companyId, now, ct);
        return new FinanceCashScenarioAnalysisResult(advice, position, baseline, upside, downside, freshness,
            advice.RequiresReview || freshness.Count > 0 || data.HasCrossCurrencyEvidence);
    }

    public async Task<FinancePaymentRunAnalysisResult> AnalyzePaymentRunAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, FinancePaymentRunAnalysisRequest request, CancellationToken ct)
    {
        ValidatePaymentRequest(companyId, agentId, request);
        var now = NormalizeUtc(request.AsOfUtc ?? DateTime.UtcNow);
        var cutoff = NormalizeUtc(request.CutoffUtc);
        var currencies = NormalizeCurrencies(request.IncludedCurrencies);
        var bills = await db.FinanceBills.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DueUtc <= cutoff)
            .Include(x => x.Counterparty).OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(500).ToListAsync(ct);
        var warnings = await db.SupplierInvoiceEnrichmentActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && bills.Select(b => b.Id).Contains(x.BillId))
            .Select(x => new { x.BillId, x.Status, x.ReconciliationWarnings }).ToListAsync(ct);
        var warningByBill = warnings.GroupBy(x => x.BillId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Status).First());
        var cash = await LatestCashByCurrencyAsync(companyId, now, ct);
        var usedByCurrency = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var items = new List<FinancePaymentRunItemDto>();

        foreach (var bill in bills)
        {
            var outstanding = Math.Max(0m, Math.Abs(bill.Amount) - bill.PaidAmount);
            var reasons = new List<string>();
            var eligible = outstanding > 0m;
            if (string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase))
            { eligible = false; reasons.Add("already_paid"); }
            if (string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase))
            { eligible = false; reasons.Add("credited"); }
            if (bill.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase)) { eligible = false; reasons.Add("cancelled"); }
            if (bill.DocumentKind.Contains("credit", StringComparison.OrdinalIgnoreCase)) { eligible = false; reasons.Add("credit_note"); }
            if (currencies.Count > 0 && !currencies.Contains(bill.Currency)) { eligible = false; reasons.Add("currency_not_selected"); }
            var hasReviewWarning = warningByBill.TryGetValue(bill.Id, out var warning) &&
                                   (warning!.Status == SupplierInvoiceEnrichmentActionStatuses.ReconciliationWarning || warning.ReconciliationWarnings.Count > 0);
            if (hasReviewWarning) reasons.Add("reconciliation_warning");
            var duplicate = bills.Count(x => x.Id != bill.Id && x.CounterpartyId == bill.CounterpartyId &&
                x.BillNumber.Equals(bill.BillNumber, StringComparison.OrdinalIgnoreCase) && x.Amount == bill.Amount) > 0;
            if (duplicate) reasons.Add("duplicate_risk");

            var overdue = Math.Max(0, (now.Date - bill.DueUtc.Date).Days);
            var score = eligible ? Math.Clamp(35 + overdue * 4 + (bill.DueUtc <= now.AddDays(3) ? 25 : 0), 0, 100) : 0;
            var group = FinancePaymentRunGroups.NotEligible;
            if (eligible && (hasReviewWarning || duplicate)) group = FinancePaymentRunGroups.DisputeOrReview;
            else if (eligible)
            {
                var available = cash.GetValueOrDefault(bill.Currency);
                var alreadyUsed = usedByCurrency.GetValueOrDefault(bill.Currency);
                var maxRemaining = request.MaximumOutflow.HasValue ? Math.Max(0m, request.MaximumOutflow.Value - usedByCurrency.Values.Sum()) : decimal.MaxValue;
                if (outstanding <= maxRemaining && available - alreadyUsed - outstanding >= request.MinimumCashReserve)
                {
                    group = FinancePaymentRunGroups.Pay;
                    usedByCurrency[bill.Currency] = alreadyUsed + outstanding;
                    reasons.Add("within_cash_constraints");
                }
                else
                {
                    group = FinancePaymentRunGroups.Defer;
                    reasons.Add("cash_constraint");
                }
            }
            if (reasons.Count == 0) reasons.Add("due_within_cutoff");
            items.Add(new FinancePaymentRunItemDto(bill.Id, bill.BillNumber, bill.CounterpartyId,
                bill.Counterparty.Name, outstanding, bill.Currency, bill.DueUtc, score, group, reasons,
                group == FinancePaymentRunGroups.Pay, $"finance-bill:{bill.Id:N}", bill.UpdatedUtc));
        }

        var token = SnapshotToken(items.Select(x => $"{x.BillId:N}|{x.SourceVersionUtc:O}|{x.OutstandingAmount}|{x.Group}"));
        var advice = await analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.Payables, HorizonDays: Math.Max(1, (cutoff.Date - now.Date).Days),
                Objective: request.Objective, AsOfUtc: now), ct);
        var after = cash.ToDictionary(x => x.Key, x => x.Value - usedByCurrency.GetValueOrDefault(x.Key), StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var currency in items.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!cash.ContainsKey(currency)) missing.Add($"Current cash balance for {currency}");
        return new FinancePaymentRunAnalysisResult(advice, token, now, cash, usedByCurrency, after, items,
            missing, advice.RequiresReview || missing.Count > 0 || items.Any(x => x.Group == FinancePaymentRunGroups.DisputeOrReview));
    }

    public async Task<CommitFinancePaymentRunResult> CommitPaymentRunAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, CommitFinancePaymentRunCommand command, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        if (!command.Reviewed) throw new AgentAiConflictException("The payment-run recommendation must be explicitly reviewed before conversion.");
        if (command.SelectedBillIds.Count == 0) throw new ArgumentException("Select at least one bill.");
        var run = await reasoning.GetRunAsync(companyId, agentId, command.RecommendationRunId, ct)
                  ?? throw new KeyNotFoundException("Finance recommendation run not found.");
        if (run.Status is not (AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview))
            throw new AgentAiConflictException("Only a completed recommendation can be converted.");
        var refreshed = await AnalyzePaymentRunAsync(companyId, agentId, actorUserId, command.AnalysisRequest, ct);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(refreshed.SnapshotToken), Encoding.UTF8.GetBytes(command.SnapshotToken)))
            throw new AgentAiConflictException("The payment-run recommendation is stale. Refresh and review it again.");
        var selected = command.SelectedBillIds.Distinct().ToHashSet();
        var eligible = refreshed.Items.Where(x => selected.Contains(x.BillId) && x.Group == FinancePaymentRunGroups.Pay).ToArray();
        var rejected = selected.Except(eligible.Select(x => x.BillId)).ToArray();
        var results = new List<SupplierInvoicePaymentProposalDto>();
        foreach (var item in eligible)
            results.Add(await paymentProposals.RequestPaymentProposalAsync(new RequestSupplierInvoicePaymentProposalCommand(
                companyId, item.BillId, actorUserId, command.ActorDisplayName), ct));
        return new CommitFinancePaymentRunResult(command.RecommendationRunId, results, rejected,
            rejected.Length == 0 ? "converted" : results.Count == 0 ? "blocked" : "partially_converted");
    }

    public async Task<FinanceCollectionsPlanResult> AnalyzeCollectionsAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, FinanceCollectionsPlanRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = NormalizeUtc(request.AsOfUtc ?? DateTime.UtcNow);
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        var strategic = (request.StrategicCustomerIds ?? []).ToHashSet();
        if (request.CreateStrategicAccountHandoffs && (!request.SalesAgentId.HasValue || request.SalesAgentId == Guid.Empty))
            throw new ArgumentException("A Sales agent is required when strategic-account handoffs are requested.");
        var invoices = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .Where(x => x.CompanyId == companyId && x.DueUtc <= now && x.DueUtc >= now.AddDays(-horizon))
            .OrderBy(x => x.DueUtc).Take(500).ToListAsync(ct);
        var items = new List<FinanceCollectionsPlanItemDto>();
        foreach (var invoice in invoices)
        {
            var outstanding = Math.Max(0m, Math.Abs(invoice.Amount) - invoice.PaidAmount);
            if (outstanding <= 0m || invoice.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase)) continue;
            var days = Math.Max(0, (now.Date - invoice.DueUtc.Date).Days);
            var disputed = invoice.Status.Contains("dispute", StringComparison.OrdinalIgnoreCase) ||
                           invoice.DocumentKind.Contains("credit", StringComparison.OrdinalIgnoreCase);
            var score = Math.Clamp(30 + days * 3 + (outstanding >= 10000m ? 15 : 0), 0, 100);
            var risk = score >= 80 ? "critical" : score >= 60 ? "high" : score >= 40 ? "medium" : "low";
            var isStrategic = strategic.Contains(invoice.CounterpartyId);
            Guid? handoffId = null;
            if (isStrategic && request.CreateStrategicAccountHandoffs)
            {
                var handoff = await handoffs.CreateAsync(companyId, agentId, new CreateAgentHandoffCommand(
                    AgentHandoffTypes.CustomerPaymentRisk, request.SalesAgentId!.Value,
                    $"Coordinate overdue invoice {invoice.InvoiceNumber} for {invoice.Counterparty.Name}",
                    "Confirm relationship-sensitive contact ownership and return a reviewed follow-up recommendation.",
                    now.AddDays(2), [$"finance-invoice:{invoice.Id:N}"]), ct);
                handoffId = handoff.Id;
            }
            var factors = new List<string> { $"days_overdue:{days}", $"outstanding:{outstanding.ToString(CultureInfo.InvariantCulture)}" };
            if (disputed) factors.Add("dispute_or_credit_indicator");
            if (isStrategic) factors.Add("strategic_account");
            items.Add(new FinanceCollectionsPlanItemDto(invoice.Id, invoice.InvoiceNumber, invoice.CounterpartyId,
                invoice.Counterparty.Name, outstanding, invoice.Currency, days, score, risk,
                disputed ? "review_dispute" : isStrategic ? "coordinate_with_sales" : score >= 60 ? "review_reminder" : "monitor",
                now.Date.AddDays(score >= 80 ? 1 : score >= 60 ? 3 : 7), factors, !disputed && !isStrategic,
                isStrategic, handoffId, $"finance-invoice:{invoice.Id:N}"));
        }
        var advice = await analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.Receivables, HorizonDays: horizon,
                Objective: request.Objective, AsOfUtc: now), ct);
        var missing = invoices.Count == 0 ? new[] { "Open overdue receivables" } : [];
        return new FinanceCollectionsPlanResult(advice, items.OrderByDescending(x => x.PriorityScore).ToArray(),
            missing, advice.RequiresReview || items.Any(x => x.RequiresSalesHandoff || !x.RoutineReminderAllowed));
    }

    public async Task<FinanceAccountingTreatmentResult> RecommendAccountingTreatmentAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, FinanceAccountingTreatmentRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var bill = await db.FinanceBills.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.BillId, ct)
            ?? throw new KeyNotFoundException("Finance bill not found.");
        var accounts = await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId).OrderBy(x => x.Code).ToListAsync(ct);
        var history = await db.SupplierInvoiceEnrichmentActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Bill.CounterpartyId == bill.CounterpartyId)
            .Select(x => x.SuggestionPayload).ToListAsync(ct);
        var accountUse = history.Select(x => x["accountCode"]?.GetValue<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var excluded = new List<FinanceExcludedAccountingCandidateDto>();
        var valid = new List<FinanceAccountingCandidateDto>();
        foreach (var account in accounts)
        {
            var reason = ExclusionReason(account.AccountType, account.Code);
            if (reason is not null)
            {
                excluded.Add(new FinanceExcludedAccountingCandidateDto(account.Id, account.Code, account.Name, reason));
                continue;
            }
            var use = accountUse.GetValueOrDefault(account.Code);
            valid.Add(new FinanceAccountingCandidateDto(account.Id, account.Code, account.Name, account.AccountType, 0, use,
                use > 0 ? .75m : .45m, null, PeriodTreatment(request, bill),
                [$"finance-bill:{bill.Id:N}", $"finance-account:{account.Id:N}"],
                ["vat_evidence_requires_review"]));
        }
        var ranked = valid.OrderByDescending(x => x.HistoricalUseCount).ThenBy(x => x.AccountCode).Take(10)
            .Select((x, i) => x with { Rank = i + 1 }).ToArray();
        var advice = await analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.AccountingTreatment, bill.Id, 30,
                request.Objective, request.AsOfUtc), ct);
        var missing = new List<string>();
        if (bill.DocumentId is null) missing.Add("Processed supplier source document");
        missing.Add("Authoritative VAT treatment evidence");
        if (ranked.Length == 0) missing.Add("Eligible expense accounts");
        return new FinanceAccountingTreatmentResult(advice, bill.Id, ranked, excluded, missing, true);
    }

    public async Task<FinanceCloseAnalysisResult> AnalyzeCloseAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, FinanceCloseAnalysisRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        EnsureNonNegative(request.MaterialityAmount, nameof(request.MaterialityAmount));
        EnsureNonNegative(request.MaterialityPercentage, nameof(request.MaterialityPercentage));
        var period = await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.FiscalPeriodId, ct)
                     ?? throw new KeyNotFoundException("Fiscal period not found.");
        var validation = await periodClose.ValidateAsync(new ValidateReportingPeriodCloseQuery(companyId, period.Id), ct);
        var variance = await financeRead.GetVarianceAsync(new GetFinanceVarianceQuery(companyId, period.StartUtc,
            request.ComparisonType, period.EndUtc, request.PlanningVersion), ct);
        var contributions = variance.Rows.Select(x => new FinanceVarianceContributionDto(x.FinanceAccountId,
            x.AccountCode, x.AccountName, x.ActualAmount, x.ComparisonAmount, x.VarianceAmount, x.VariancePercentage,
            x.Currency, Math.Abs(x.VarianceAmount) >= request.MaterialityAmount ||
                        Math.Abs(x.VariancePercentage ?? 0m) >= request.MaterialityPercentage,
            $"finance-variance:{period.Id:N}:{x.FinanceAccountId:N}:{x.CostCenterId?.ToString("N") ?? "none"}"))
            .Where(x => x.IsMaterial).OrderByDescending(x => Math.Abs(x.VarianceAmount)).Take(30).ToArray();
        var checklist = validation.Issues.Select((issue, index) => new FinanceCloseChecklistItemDto(issue.Code,
            issue.Message, "blocked", "Finance", period.EndUtc.AddDays(index + 1), [],
            [$"finance-close-validation:{period.Id:N}:{issue.Code}"])).ToList();
        if (checklist.Count == 0)
            checklist.Add(new FinanceCloseChecklistItemDto("close_validation", "Authoritative close validation passed",
                "complete", "Finance", period.EndUtc, [], [$"finance-close-validation:{period.Id:N}"]));
        var token = SnapshotToken(new[] { $"{period.Id:N}|{period.UpdatedUtc:O}|{validation.IsReadyToClose}" }
            .Concat(variance.Rows.Select(x => $"{x.FinanceAccountId:N}|{x.CostCenterId:N}|{x.ActualAmount}|{x.ComparisonAmount}")));
        var advice = await analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.CloseAnalysis, period.Id,
                Objective: request.Objective, AsOfUtc: request.AsOfUtc), ct);
        var missing = validation.Issues.Where(x => x.Code == ReportingPeriodBlockingIssueCodes.MissingStatementMappings)
            .Select(x => x.Message).ToArray();
        return new FinanceCloseAnalysisResult(advice, period.Id, token, validation.IsReadyToClose,
            validation.IsClosed, validation.IsReportingLocked, contributions, checklist, missing,
            advice.RequiresReview || !validation.IsReadyToClose);
    }

    private async Task<ProjectionData> LoadProjectionDataAsync(Guid companyId, DateTime now, int horizon, CancellationToken ct)
    {
        var invoices = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .Where(x => x.CompanyId == companyId && x.DueUtc <= now.AddDays(horizon) && x.PaidAmount < Math.Abs(x.Amount))
            .Take(500).ToListAsync(ct);
        var bills = await db.FinanceBills.IgnoreQueryFilters().AsNoTracking().Include(x => x.Counterparty)
            .Where(x => x.CompanyId == companyId && x.DueUtc <= now.AddDays(horizon) && x.PaidAmount < Math.Abs(x.Amount))
            .Take(500).ToListAsync(ct);
        var invoiceDtos = invoices.Select(x => new FinanceOpenReceivableItemDto(x.Id, x.InvoiceNumber, x.Counterparty.Name,
            x.DueUtc, Math.Max(0m, Math.Abs(x.Amount) - x.PaidAmount), x.Currency, x.Status, x.CounterpartyId)).ToArray();
        var billDtos = bills.Select(x => new FinanceOpenPayableItemDto(x.Id, x.BillNumber, x.Counterparty.Name,
            x.DueUtc, Math.Max(0m, Math.Abs(x.Amount) - x.PaidAmount), x.Currency, x.Status, x.CounterpartyId)).ToArray();
        var sourceIds = invoices.Select(x => $"finance-invoice:{x.Id:N}").Concat(bills.Select(x => $"finance-bill:{x.Id:N}")).ToArray();
        return new ProjectionData(invoiceDtos, billDtos, [], sourceIds,
            invoiceDtos.Concat<object>(billDtos).Select(x => x switch { FinanceOpenReceivableItemDto i => i.Currency, FinanceOpenPayableItemDto b => b.Currency, _ => "" })
                .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
    }

    private static FinanceCashProjectionDto BuildProjection(decimal cash, string currency,
        IReadOnlyList<FinanceOpenReceivableItemDto> invoices, IReadOnlyList<FinanceOpenPayableItemDto> bills,
        DateTime now, int horizon)
    {
        var end = now.AddDays(horizon);
        var inflows = invoices.Where(x => x.DueUtc <= end && x.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)).Sum(x => x.OutstandingAmount);
        var outflows = bills.Where(x => x.DueUtc <= end && x.Currency.Equals(currency, StringComparison.OrdinalIgnoreCase)).Sum(x => x.OutstandingAmount);
        return new FinanceCashProjectionDto(horizon, cash, inflows, outflows, cash + inflows - outflows, inflows, outflows, 0m);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> LatestCashByCurrencyAsync(Guid companyId, DateTime now, CancellationToken ct)
    {
        var balances = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.AsOfUtc <= now)
            .OrderByDescending(x => x.AsOfUtc).Take(5000).ToListAsync(ct);
        return balances.GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key,
            x => x.GroupBy(y => y.AccountId).Sum(y => y.OrderByDescending(z => z.AsOfUtc).First().Amount), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<string>> CashFreshnessWarningsAsync(Guid companyId, DateTime now, CancellationToken ct)
    {
        var newest = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.AsOfUtc <= now)
            .MaxAsync(x => (DateTime?)x.AsOfUtc, ct);
        if (!newest.HasValue) return ["Current bank or cash balance evidence is missing."];
        return newest.Value < now.AddDays(-3) ? [$"Cash evidence is stale; newest balance is {newest.Value:O}."] : [];
    }

    private static FinanceCashScenarioDto Scenario(string name, decimal starting, decimal inflows, decimal outflows,
        decimal ending, decimal delta, string currency, IReadOnlyList<string> assumptions, IReadOnlyList<string> sources) =>
        new(name, starting, inflows, outflows, ending, delta, currency, assumptions, sources);

    private static string? ExclusionReason(string accountType, string code)
    {
        var value = $"{accountType} {code}".ToLowerInvariant();
        if (value.Contains("bank")) return "bank_account_not_expense";
        if (value.Contains("control")) return "control_account_not_expense";
        if (value.Contains("liability") || code.StartsWith('2')) return "liability_account_not_expense";
        if (value.Contains("receivable")) return "receivable_account_not_expense";
        if (value.Contains("asset") || code.StartsWith('1')) return "asset_account_requires_separate_policy";
        return null;
    }

    private static string? PeriodTreatment(FinanceAccountingTreatmentRequest request, FinanceBill bill)
    {
        if (!request.ServicePeriodStartUtc.HasValue || !request.ServicePeriodEndUtc.HasValue) return null;
        if (request.ServicePeriodEndUtc <= request.ServicePeriodStartUtc) throw new ArgumentException("Service period end must be after start.");
        if (request.ServicePeriodEndUtc.Value.Date <= bill.ReceivedUtc.Date) return "expense_current_period";
        return request.ServicePeriodEndUtc.Value - request.ServicePeriodStartUtc.Value > TimeSpan.FromDays(31)
            ? "review_prepayment_or_accrual" : "expense_service_period";
    }

    private static HashSet<string> NormalizeCurrencies(IReadOnlyList<string>? values) =>
        (values ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length == 3).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string SnapshotToken(IEnumerable<string> values)
    {
        var canonical = string.Join("\n", values.OrderBy(x => x, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void ValidatePaymentRequest(Guid companyId, Guid agentId, FinancePaymentRunAnalysisRequest request)
    {
        ValidateIds(companyId, agentId);
        if (request.CutoffUtc == default) throw new ArgumentException("A payment cutoff is required.");
        EnsureNonNegative(request.MinimumCashReserve, nameof(request.MinimumCashReserve));
        if (request.MaximumOutflow.HasValue) EnsureNonNegative(request.MaximumOutflow.Value, nameof(request.MaximumOutflow));
    }

    private static void ValidateIds(Guid companyId, Guid agentId)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
    }

    private static void EnsureNonNegative(decimal value, string name)
    {
        if (value < 0m) throw new ArgumentOutOfRangeException(name, "Value cannot be negative.");
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private sealed record ProjectionData(IReadOnlyList<FinanceOpenReceivableItemDto> Invoices,
        IReadOnlyList<FinanceOpenPayableItemDto> Bills, IReadOnlyList<FinanceHistoricalReceivablePaymentDto> History,
        IReadOnlyList<string> SourceIds, bool HasCrossCurrencyEvidence);
}
