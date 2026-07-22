using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportAnalyticsService : ISupportAnalyticsService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SupportAnalyticsService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportAnalyticsDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var summary = await new SupportCaseService(_dbContext, new NoopAuditEventWriter()).ListCasesAsync(companyId, new SupportCaseListQuery(Take: 1), cancellationToken);
        var byStatus = await BucketAsync(companyId, x => x.Status, cancellationToken);
        var byCategory = await BucketAsync(companyId, x => x.Category, cancellationToken);
        var byPriority = await BucketAsync(companyId, x => x.Priority, cancellationToken);
        var sla = await BuildSlaPerformanceAsync(companyId, cancellationToken);
        var learning = await BuildLearningEffectivenessAsync(companyId, cancellationToken);
        var insights = byCategory.Where(x => x.Count >= 3).Select(x => new SupportRootCauseInsight($"Recurring {x.Label.ToLowerInvariant()} cases", $"{x.Count} support cases share this category.", x.Key, x.Count, "Review related support knowledge and workflow steps.")).ToList();
        return new SupportAnalyticsDashboardResponse(summary.Summary, byStatus, byCategory, byPriority, sla, learning, insights);
    }

    private async Task<SupportSlaPerformanceSummary> BuildSlaPerformanceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SupportCases.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Status,
                x.IsSlaRisk,
                x.IsSlaBreached,
                x.FirstResponseDueUtc,
                x.FirstResponseSentUtc,
                x.ResolutionDueUtc,
                x.ResolvedUtc
            })
            .ToListAsync(cancellationToken);
        var open = rows.Where(x => x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed).ToList();
        var responded = rows.Where(x => x.FirstResponseDueUtc.HasValue && x.FirstResponseSentUtc.HasValue).ToList();
        var resolved = rows.Where(x => x.ResolutionDueUtc.HasValue && x.ResolvedUtc.HasValue).ToList();
        var missingTargets = rows.Count(x => !x.FirstResponseDueUtc.HasValue || !x.ResolutionDueUtc.HasValue);
        return new SupportSlaPerformanceSummary(
            open.Count(x => x.IsSlaRisk),
            open.Count(x => x.IsSlaBreached),
            responded.Count(x => x.FirstResponseSentUtc <= x.FirstResponseDueUtc),
            responded.Count(x => x.FirstResponseSentUtc > x.FirstResponseDueUtc),
            resolved.Count(x => x.ResolvedUtc <= x.ResolutionDueUtc),
            resolved.Count(x => x.ResolvedUtc > x.ResolutionDueUtc),
            missingTargets,
            missingTargets == 0
                ? "SLA reporting uses the targets stored on each support case."
                : "Some historical cases do not have stored SLA targets, so they are labeled as missing instead of counted as met or missed.");
    }

    private async Task<SupportLearningEffectivenessSummary> BuildLearningEffectivenessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var observations = await _dbContext.SupportMemoryObservations.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var drafts = await _dbContext.SupportReplyDrafts.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Status,
                x.Answerability,
                x.SourceReferencesJson,
                x.SentUtc
            })
            .ToListAsync(cancellationToken);
        var withMemory = drafts.Where(x => !string.IsNullOrWhiteSpace(x.SourceReferencesJson) && x.SourceReferencesJson.Contains("customer_memory", StringComparison.OrdinalIgnoreCase)).ToList();
        var withoutMemory = drafts.Except(withMemory).ToList();
        var reopened = await _dbContext.SupportCases.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.Status == SupportCaseStatuses.Reopened, cancellationToken);
        return new SupportLearningEffectivenessSummary(
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Approved)?.Count ?? 0,
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Review)?.Count ?? 0,
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Rejected)?.Count ?? 0,
            withMemory.Count,
            withMemory.Count == 0 ? null : decimal.Round(withMemory.Average(x => x.Answerability), 3, MidpointRounding.AwayFromZero),
            withoutMemory.Count == 0 ? null : decimal.Round(withoutMemory.Average(x => x.Answerability), 3, MidpointRounding.AwayFromZero),
            drafts.Count(x => x.Status == SupportReplyDraftStatuses.Approved),
            drafts.Count(x => x.Status == SupportReplyDraftStatuses.Rejected),
            drafts.Count(x => x.SentUtc.HasValue),
            reopened,
            "Learning metrics use draft metadata and governed memory observations only; they show association, not guaranteed causation.");
    }

    private async Task<IReadOnlyList<SupportMetricBucket>> BucketAsync(Guid companyId, System.Linq.Expressions.Expression<Func<SupportCase, string>> selector, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId)
            .GroupBy(selector)
            .Select(x => new { Key = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new SupportMetricBucket(x.Key, SupportLabels.Status(x.Key) == x.Key ? SupportLabels.Category(x.Key) : SupportLabels.Status(x.Key), x.Count)).ToList();
    }
}

