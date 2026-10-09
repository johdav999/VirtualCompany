using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceWeeklyWorkspaceContributor(IFinanceOperationalReportService reports,
    IFinanceReadService finance, VirtualCompanyDbContext db, ILogger<FinanceWeeklyWorkspaceContributor> logger) : IWeeklyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Finance;
    public async Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token)
    {
        var p = c.Period;
        // Current outstanding obligations use the existing Finance owner, never a reconstructed settlement balance.
        var report = await reports.GetAsync(new(c.CompanyId, DateOnly.FromDateTime(c.NowUtc), 14), token);
        var gaps = report.CoverageGaps.ToList();
        gaps.Add("Obligations are outstanding now, selected by the chosen week's due dates; past settlements and due-date revisions are not reconstructed. Cash is recorded ledger evidence, not live bank confirmation.");
        var metrics = new List<WeeklyWorkspaceMetricDto>();
        var obligations = report.Receivables.Concat(report.Payables).Where(x => x.DueUtc >= p.StartUtc && x.DueUtc < p.EndUtc).ToList();
        foreach (var group in obligations.GroupBy(x => (x.Kind, x.Currency)).OrderBy(x => x.Key.Kind).ThenBy(x => x.Key.Currency))
        {
            var amount = group.Sum(x => x.RemainingAmount);
            metrics.Add(new($"finance.due.{group.Key.Kind}.{group.Key.Currency}", $"Outstanding {(group.Key.Kind == "invoice" ? "receivables" : "payables")} due · {group.Key.Currency}",
                amount, $"{amount:0.##} {group.Key.Currency}", null, "Historical settlement balance unavailable", "current_due_schedule",
                "Current remaining booked operational obligations with due date in the full selected calendar week. Currency and receivable/payable direction are kept separate.",
                $"{group.Count()} source obligations; Finance operational calculation {report.CalculationVersion}. Current as of refresh, not a weekly actual cash flow.",
                group.Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), $"{x.Number} · {x.Counterparty} · {x.RemainingAmount:0.##} {x.Currency}", x.DueUtc, x.DeepLink)).ToList(),
                ObservedAtUtc: report.ObservedAtUtc));
        }
        if (obligations.Count == 0) metrics.Add(new("finance.due.count", "Outstanding obligations due this week", 0, "0", null,
            "Historical settlement balance unavailable", "current_due_schedule", "Current outstanding booked operational obligations whose due dates are inside the selected week.",
            "No matching obligations in the Finance operational scope; no historical settlement claim.", [], ObservedAtUtc: report.ObservedAtUtc));
        try
        {
            var cutoff = p.ActivityEndUtc.AddTicks(-1); var previousCutoff = p.ComparisonEndUtc.AddTicks(-1);
            var current = await finance.GetCashBalanceAsync(new(c.CompanyId, cutoff), token);
            var previous = await finance.GetCashBalanceAsync(new(c.CompanyId, previousCutoff), token);
            var accountIds = current.Accounts.Select(x => x.AccountId).ToArray();
            var accounts = await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && accountIds.Contains(x.Id))
                .Select(x => new { x.Id, x.OpenedUtc }).ToListAsync(token);
            var snapshots = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                    accountIds.Contains(x.AccountId) && x.AsOfUtc <= previousCutoff && x.SourceSimulationEventRecordId == null)
                .Select(x => x.AccountId).Distinct().ToListAsync(token);
            foreach (var group in current.Accounts.Where(x => accounts.Any(a => a.Id == x.AccountId && a.OpenedUtc <= cutoff))
                         .GroupBy(x => x.Currency).OrderBy(x => x.Key))
            {
                var ids = group.Select(x => x.AccountId).ToArray();
                var comparable = ids.All(id => snapshots.Contains(id) && accounts.Any(a => a.Id == id && a.OpenedUtc <= previousCutoff)) &&
                    previous.Accounts.Where(x => ids.Contains(x.AccountId)).All(x => x.Currency == group.Key) &&
                    previous.Accounts.Count(x => ids.Contains(x.AccountId)) == ids.Length;
                decimal? comparison = comparable ? previous.Accounts.Where(x => ids.Contains(x.AccountId)).Sum(x => x.Amount) : null;
                var value = group.Sum(x => x.Amount);
                metrics.Add(new($"finance.cash.{group.Key}", $"Cash at weekly cutoff · {group.Key}", value, $"{value:0.##} {group.Key}", comparison,
                    comparison.HasValue ? $"{comparison:0.##} {group.Key}" : "Unavailable: prior account evidence missing", "balance",
                    $"Owning Finance cash balances at {cutoff:O}, compared at {previousCutoff:O}; same account/currency cohort with retained prior snapshots. Posted cash movements after snapshots are included by Finance.",
                    "Recorded ledger calculation. Without a prior snapshot for every included account, comparison is unavailable. Imported/deleted/corrected history is not certified; opening evidence may underpin the selected balance.",
                    group.Select(x => new WeeklyWorkspaceSourceDto(x.AccountId.ToString("D"), $"{x.AccountName} · {x.Amount:0.##} {x.Currency}", cutoff,
                        $"/finance/cash-position?companyId={c.CompanyId:D}")).ToList(), comparison.HasValue, cutoff,
                    ComparisonSources: comparison.HasValue ? previous.Accounts.Where(x => ids.Contains(x.AccountId)).Select(x =>
                        new WeeklyWorkspaceSourceDto(x.AccountId.ToString("D"), $"{x.AccountName} · {x.Amount:0.##} {x.Currency}", previousCutoff,
                            $"/finance/cash-position?companyId={c.CompanyId:D}")).ToList() : []));
            }
            if (!metrics.Any(x => x.Key.StartsWith("finance.cash.")))
                metrics.Add(WeeklyWorkspaceMeasures.Unavailable("finance.cash", "Cash at weekly cutoff", "balance", "No recorded cash accounts existed at this cutoff."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Weekly cash projection failed for company {CompanyId}.", c.CompanyId);
            metrics.Add(WeeklyWorkspaceMeasures.Unavailable("finance.cash", "Cash at weekly cutoff", "balance", "Cash source unavailable; retry or review recorded cash evidence."));
            gaps.Add("Cash projection failed; obligations remain available from their owner.");
        }
        var commitments = report.Receivables.Concat(report.Payables).Where(x => x.DueUtc < p.EndUtc.AddDays(7))
            .OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(12).Select(x => new WeeklyWorkspaceCommitmentDto(x.Id.ToString("D"),
                $"{x.Number} · {x.Counterparty}", $"Current outstanding {x.RemainingAmount:0.##} {x.Currency}; review collection/payment through the owning workflow.",
                x.DueUtc, report.ObservedAtUtc, x.DeepLink, c.Access.ResponsiblePerson, c.Access.WorkingAgent)).ToList();
        return new(Lens, "Finance liquidity and obligations", metrics, commitments, gaps);
    }
}
