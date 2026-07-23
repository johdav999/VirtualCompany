using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceAgentAnalysisService(
    VirtualCompanyDbContext db,
    IAgentReasoningGateway reasoning) : IFinanceAgentAnalysisService
{
    public async Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken)
    {
        Validate(companyId, agentId, request);
        var now = request.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        var evidence = await BuildEvidenceAsync(companyId, request, now, horizon, cancellationToken);
        var capabilityId = CapabilityId(request.AnalysisType);
        var result = await reasoning.ReasonAsync(new AgentReasoningRequest(
            companyId, agentId, capabilityId, "1.0.0", $"finance-role-v1:{NormalizeCadence(request.Cadence)}", "1.0.0",
            Instruction(request.AnalysisType, horizon, request.Objective), evidence.Sources,
            ["recommend"], [], actorUserId), cancellationToken);

        var missing = evidence.Missing.Concat(result.MissingEvidence).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RoleAgentAnalysisResult(result.RunId, capabilityId, result.Status, result.Summary,
            result.Confidence, now, evidence.Metrics, evidence.Priorities, result.Claims, evidence.Sources,
            missing, result.NextActions, result.Status != AgentAiRunStatuses.Completed || missing.Length > 0);
    }

    private async Task<Evidence> BuildEvidenceAsync(Guid companyId, RoleAgentAnalysisRequest request, DateTime now,
        int horizon, CancellationToken ct)
    {
        var type = request.AnalysisType.Trim().ToLowerInvariant();
        var sources = new List<AgentAiSource>();
        var metrics = new List<RoleAgentMetric>();
        var priorities = new List<RoleAgentPriority>();
        var missing = new List<string>();

        if (type is FinanceAgentAnalysisTypes.CashLiquidity or FinanceAgentAnalysisTypes.OperatingCadence)
        {
            var balances = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.AsOfUtc <= now)
                .OrderByDescending(x => x.AsOfUtc).Take(500).ToListAsync(ct);
            foreach (var currencyGroup in balances.GroupBy(x => x.Currency))
            {
                var latest = currencyGroup.GroupBy(x => x.AccountId).Select(x => x.OrderByDescending(y => y.AsOfUtc).First()).ToArray();
                var sourceId = $"finance-balance:{currencyGroup.Key}:{latest.Max(x => x.AsOfUtc):O}";
                var total = latest.Sum(x => x.Amount);
                sources.Add(new AgentAiSource(sourceId, "finance_balance", $"Cash position {currencyGroup.Key}",
                    $"Authoritative latest-account balance total is {total.ToString(CultureInfo.InvariantCulture)} {currencyGroup.Key} across {latest.Length} accounts.", latest.Max(x => x.AsOfUtc)));
                metrics.Add(new RoleAgentMetric($"cash_{currencyGroup.Key.ToLowerInvariant()}", $"Cash ({currencyGroup.Key})", total, currencyGroup.Key, sourceId, latest.Max(x => x.AsOfUtc)));
                if (latest.Max(x => x.AsOfUtc) < now.AddDays(-3)) missing.Add($"Fresh {currencyGroup.Key} balance data");
            }
            if (balances.Count == 0) missing.Add("Current cash balances");
        }

        if (type is FinanceAgentAnalysisTypes.Payables or FinanceAgentAnalysisTypes.CashLiquidity or FinanceAgentAnalysisTypes.OperatingCadence)
        {
            var bills = await db.FinanceBills.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DueUtc <= now.AddDays(horizon))
                .OrderBy(x => x.DueUtc).Take(30).ToListAsync(ct);
            foreach (var bill in bills)
            {
                var outstanding = Math.Max(0m, Math.Abs(bill.Amount) - bill.PaidAmount);
                var ineligible = outstanding == 0m ||
                                 string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase) ||
                                 bill.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase);
                var overdueDays = Math.Max(0, (now.Date - bill.DueUtc.Date).Days);
                var score = ineligible ? 0 : Math.Clamp(35 + overdueDays * 4 + (bill.DueUtc <= now.AddDays(3) ? 25 : 0), 0, 100);
                var sourceId = $"finance-bill:{bill.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "finance_bill", $"Supplier bill {bill.BillNumber}",
                    $"Amount {bill.Amount} {bill.Currency}; paid {bill.PaidAmount}; due {bill.DueUtc:O}; status {bill.Status}; settlement {bill.SettlementStatus}; posting {bill.PostingStatus}.", bill.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("supplier_bill", bill.Id, $"Bill {bill.BillNumber}", score,
                    ineligible ? "not_eligible" : score >= 75 ? "urgent" : score >= 50 ? "review" : "planned",
                    ineligible ? ["deterministically_ineligible"] : overdueDays > 0 ? ["overdue", "payment_state_requires_review"] : ["due_within_horizon"], sourceId));
            }
        }

        if (type is FinanceAgentAnalysisTypes.Receivables or FinanceAgentAnalysisTypes.CashLiquidity or FinanceAgentAnalysisTypes.OperatingCadence)
        {
            var invoices = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DueUtc <= now.AddDays(horizon))
                .OrderBy(x => x.DueUtc).Take(30).ToListAsync(ct);
            foreach (var invoice in invoices)
            {
                var outstanding = Math.Max(0m, Math.Abs(invoice.Amount) - invoice.PaidAmount);
                var overdueDays = Math.Max(0, (now.Date - invoice.DueUtc.Date).Days);
                var eligible = outstanding > 0m && !invoice.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase);
                var score = eligible ? Math.Clamp(30 + overdueDays * 4 + (outstanding > 10000m ? 15 : 0), 0, 100) : 0;
                var sourceId = $"finance-invoice:{invoice.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "finance_invoice", $"Customer invoice {invoice.InvoiceNumber}",
                    $"Amount {invoice.Amount} {invoice.Currency}; paid {invoice.PaidAmount}; due {invoice.DueUtc:O}; status {invoice.Status}; settlement {invoice.SettlementStatus}.", invoice.UpdatedUtc));
                priorities.Add(new RoleAgentPriority("customer_invoice", invoice.Id, $"Invoice {invoice.InvoiceNumber}", score,
                    !eligible ? "not_eligible" : score >= 75 ? "urgent" : score >= 50 ? "review" : "monitor",
                    !eligible ? ["deterministically_ineligible"] : overdueDays > 0 ? ["overdue", "open_balance"] : ["due_within_horizon"], sourceId));
            }
        }

        if (type == FinanceAgentAnalysisTypes.AccountingTreatment)
        {
            if (!request.SubjectId.HasValue) missing.Add("Supplier bill subject ID");
            else
            {
                var bill = await db.FinanceBills.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.SubjectId, ct);
                if (bill is null) throw new KeyNotFoundException("Finance bill not found.");
                var billSource = $"finance-bill:{bill.Id:N}";
                sources.Add(new AgentAiSource(billSource, "finance_bill", $"Supplier bill {bill.BillNumber}",
                    $"Amount {bill.Amount} {bill.Currency}; document kind {bill.DocumentKind}; posting {bill.PostingStatus}; processing {bill.ProcessingStatus}.", bill.UpdatedUtc));
                var accounts = await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
                        !x.AccountType.Contains("bank") && !x.AccountType.Contains("liability") &&
                        !x.AccountType.Contains("receivable") && !x.AccountType.Contains("control") &&
                        !x.AccountType.Contains("asset"))
                    .OrderBy(x => x.Code).Take(40).ToListAsync(ct);
                foreach (var account in accounts)
                {
                    var sourceId = $"finance-account:{account.Id:N}";
                    sources.Add(new AgentAiSource(sourceId, "chart_of_accounts", $"{account.Code} {account.Name}",
                        $"Account code {account.Code}; type {account.AccountType}; currency {account.Currency}.", account.UpdatedUtc));
                }
                if (accounts.Count == 0) missing.Add("Chart of accounts");
            }
        }

        if (type is FinanceAgentAnalysisTypes.CloseAnalysis or FinanceAgentAnalysisTypes.OperatingCadence)
        {
            var insights = await db.FinanceAgentInsights.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == FinanceInsightStatus.Active)
                .OrderByDescending(x => x.Severity).ThenByDescending(x => x.ObservedUtc).Take(20).ToListAsync(ct);
            foreach (var insight in insights)
            {
                var sourceId = $"finance-insight:{insight.Id:N}";
                sources.Add(new AgentAiSource(sourceId, "finance_insight", insight.EntityDisplayName ?? insight.CheckCode,
                    $"Severity {insight.Severity}; confirmed check: {insight.Message}; deterministic recommendation: {insight.Recommendation}.", insight.ObservedUtc));
                priorities.Add(new RoleAgentPriority(insight.EntityType, Guid.TryParse(insight.EntityId, out var id) ? id : insight.Id,
                    insight.EntityDisplayName ?? insight.CheckCode, insight.Severity == FinancialCheckSeverity.Critical ? 100 : 70,
                    insight.Severity.ToString().ToLowerInvariant(), ["authoritative_finance_check"], sourceId));
            }
        }

        if (sources.Count == 0)
            sources.Add(new AgentAiSource("finance-state:empty", "finance_state", "Finance evidence state", "No authoritative records matched this bounded analysis request.", now));
        var boundedSources = sources.Take(50).ToArray();
        var sourceIds = boundedSources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        return new Evidence(boundedSources, metrics.Where(x => sourceIds.Contains(x.SourceId)).ToArray(),
            priorities.Where(x => sourceIds.Contains(x.SourceId)).OrderByDescending(x => x.Score).Take(30).ToArray(), missing);
    }

    private static string Instruction(string type, int horizon, string? objective) =>
        $"Act as a Finance analysis adviser. Analyze '{type}' over {horizon} days. Use authoritative values exactly; do not calculate or change balances, eligibility, tax, posting, payment, or approval state. Separate facts, inferences, and unknowns. Explain deterministic priority and propose review-only next steps. Objective: {objective ?? "none"}.";

    private static string CapabilityId(string type) => type.Trim().ToLowerInvariant() switch
    {
        FinanceAgentAnalysisTypes.CashLiquidity => AgentCapabilityIds.FinanceCashLiquidity,
        FinanceAgentAnalysisTypes.Payables => AgentCapabilityIds.FinancePayables,
        FinanceAgentAnalysisTypes.Receivables => AgentCapabilityIds.FinanceReceivables,
        FinanceAgentAnalysisTypes.AccountingTreatment => AgentCapabilityIds.FinanceAccountingTreatment,
        FinanceAgentAnalysisTypes.CloseAnalysis => AgentCapabilityIds.FinanceCloseAnalysis,
        FinanceAgentAnalysisTypes.OperatingCadence => AgentCapabilityIds.FinanceOperatingCadence,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Unsupported Finance analysis type.")
    };

    private static void Validate(Guid companyId, Guid agentId, RoleAgentAnalysisRequest request)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (string.IsNullOrWhiteSpace(request.AnalysisType) || !FinanceAgentAnalysisTypes.All.Contains(request.AnalysisType))
            throw new ArgumentOutOfRangeException(nameof(request), "Unsupported Finance analysis type.");
    }

    private static string NormalizeCadence(string? value) => value?.Trim().ToLowerInvariant() is "daily" or "weekly" or "monthly" ? value.Trim().ToLowerInvariant() : "on_demand";

    private sealed record Evidence(IReadOnlyList<AgentAiSource> Sources, IReadOnlyList<RoleAgentMetric> Metrics,
        IReadOnlyList<RoleAgentPriority> Priorities, IReadOnlyList<string> Missing);
}
