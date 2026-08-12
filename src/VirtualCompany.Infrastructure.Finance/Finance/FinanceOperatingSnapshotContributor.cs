using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceOperatingSnapshotContributor(VirtualCompanyDbContext db) : ICompanyOperatingSnapshotContributor
{
    private const int Limit = 25;
    public string SectionName => "finance";

    public async Task<CompanyOperatingSnapshotContribution> CaptureAsync(Guid companyId, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var balances = await db.FinanceBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.AsOfUtc).Take(Limit + 1)
            .Select(x => new { sourceId = $"finance-balance:{x.Id:N}", x.Id, x.AccountId, x.AsOfUtc,
                x.Amount, x.Currency, x.CreatedUtc }).ToListAsync(ct);
        var insights = await db.FinanceAgentInsights.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != FinanceInsightStatus.Resolved)
            .OrderByDescending(x => x.Severity).ThenByDescending(x => x.ObservedUtc).Take(Limit + 1)
            .Select(x => new { sourceId = $"finance-insight:{x.Id:N}", x.Id, x.CheckCode, x.EntityType,
                x.EntityId, x.Severity, x.Message, x.Recommendation, x.Confidence, x.Status,
                x.ObservedUtc, x.UpdatedUtc }).ToListAsync(ct);
        var gaps = new List<string>();
        if (balances.Count == 0) gaps.Add("No current finance balances are available.");
        if (insights.Count == 0) gaps.Add("No open finance insights are available.");
        var observed = DateTime.UtcNow;
        return new(SectionName, JsonSerializer.SerializeToNode(new
        {
            observedAtUtc = observed,
            balances = balances.Take(Limit),
            insights = insights.Take(Limit),
            dataGaps = gaps
        }), balances.Count + insights.Count, gaps, balances.Count > Limit || insights.Count > Limit, observed);
    }
}
