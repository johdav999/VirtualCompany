using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportOperatingSnapshotContributor(VirtualCompanyDbContext db) : ICompanyOperatingSnapshotContributor
{
    private const int Limit = 25;
    public string SectionName => "support";

    public async Task<CompanyOperatingSnapshotContribution> CaptureAsync(Guid companyId, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var cases = await db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != "closed")
            .OrderByDescending(x => x.IsSlaBreached).ThenByDescending(x => x.IsSlaRisk)
            .ThenByDescending(x => x.UpdatedUtc).Take(Limit + 1)
            .Select(x => new { sourceId = $"support-case:{x.Id:N}", x.Id, x.CaseNumber, x.Subject, x.Status,
                x.Priority, x.Category, x.Sentiment, x.IsSlaRisk, x.IsSlaBreached, x.IsVipRisk, x.IsChurnRisk,
                x.SuggestedNextAction, x.ResolutionDueUtc, x.UpdatedUtc }).ToListAsync(ct);
        var gaps = cases.Count == 0 ? new List<string> { "No open support cases are available." } : [];
        var observed = DateTime.UtcNow;
        return new(SectionName, JsonSerializer.SerializeToNode(new
        {
            observedAtUtc = observed,
            openCases = cases.Take(Limit),
            attentionCount = cases.Count(x => x.IsSlaBreached || x.IsSlaRisk || x.IsChurnRisk || x.IsVipRisk),
            dataGaps = gaps
        }), cases.Count, gaps, cases.Count > Limit, observed);
    }
}
