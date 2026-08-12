using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesOperatingSnapshotContributor(VirtualCompanyDbContext db) : ICompanyOperatingSnapshotContributor
{
    private const int Limit = 25;
    public string SectionName => "sales";

    public async Task<CompanyOperatingSnapshotContribution> CaptureAsync(Guid companyId, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var leads = await db.Leads.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedUtc).Take(Limit + 1)
            .Select(x => new { sourceId = $"sales-lead:{x.Id:N}", x.Id, x.Title, x.Status, x.EstimatedValue,
                x.Currency, x.Fit, x.Temperature, x.Priority, x.SuggestedNextAction, x.UpdatedUtc }).ToListAsync(ct);
        var deals = await db.Deals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted)
            .OrderByDescending(x => x.UpdatedUtc).Take(Limit + 1)
            .Select(x => new { sourceId = $"sales-deal:{x.Id:N}", x.Id, x.Title, x.Status, x.Amount,
                x.Currency, x.ExpectedCloseUtc, x.UpdatedUtc }).ToListAsync(ct);
        var gaps = new List<string>();
        if (leads.Count == 0) gaps.Add("No active sales leads are available.");
        if (deals.Count == 0) gaps.Add("No active sales opportunities are available.");
        var observed = DateTime.UtcNow;
        return new(SectionName, JsonSerializer.SerializeToNode(new
        {
            observedAtUtc = observed,
            leads = leads.Take(Limit),
            opportunities = deals.Take(Limit),
            dataGaps = gaps
        }), leads.Count + deals.Count, gaps, leads.Count > Limit || deals.Count > Limit, observed);
    }
}
