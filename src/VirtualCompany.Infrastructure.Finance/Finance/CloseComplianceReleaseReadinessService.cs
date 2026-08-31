using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CloseComplianceReleaseReadinessTelemetry : IDisposable
{
    public const string MeterName = "VirtualCompany.Finance.CloseComplianceReleaseReadiness";
    public const string ActivitySourceName = "VirtualCompany.Finance.CloseComplianceReleaseReadiness";
    private readonly Meter _meter = new(MeterName);
    internal readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal Histogram<double> EvaluationDuration { get; }
    internal Counter<long> Evaluations { get; }
    internal Counter<long> ReleaseStops { get; }

    public CloseComplianceReleaseReadinessTelemetry()
    {
        EvaluationDuration = _meter.CreateHistogram<double>("finance.close_compliance_release.evaluation.duration", "ms");
        Evaluations = _meter.CreateCounter<long>("finance.close_compliance_release.evaluation.count");
        ReleaseStops = _meter.CreateCounter<long>("finance.close_compliance_release.stop.count");
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}

public static class CloseComplianceReleaseReadinessPolicy
{
    private const string CloseWorkspaceUrl = "/finance/accounting/close-workspace";
    private const string ReconciliationUrl = "/finance/accounting/reconciliation";
    private const string ReportsUrl = "/finance/accounting/reports";
    private const string ComplianceUrl = "/finance/accounting/compliance-calendar";
    private const string PackagesUrl = "/finance/accounting/audit-packages";
    private const string YearEndUrl = "/finance/accounting/year-end";
    private const string AccountantUrl = "/accountant/portfolio";

    public static CloseComplianceReleaseReadinessDto Evaluate(
        AccountingCloseWorkspaceDto workspace,
        IReadOnlyCollection<AccountantGrantDto> grants,
        IReadOnlyCollection<AccountantEngagementDto> engagements,
        DateTime generatedUtc)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(engagements);

        var activeTasks = workspace.Tasks
            .Where(task => task.Status is not "completed" and not "cancelled")
            .ToArray();
        var overdueTasks = activeTasks.Count(task => task.DueUtc < generatedUtc);
        var blockedTasks = activeTasks.Count(task =>
            task.Blockers.Any(blocker => blocker.Status == "open") || task.BlockingReasonCodes.Count > 0);
        var missingTaskEvidence = activeTasks.Count(task => task.Evidence.Count == 0);
        if (workspace.CloseInstanceId is null || workspace.Readiness is null)
            missingTaskEvidence++;

        var reconciliation = Panel(workspace, "reconciliations");
        var reports = Panel(workspace, "reports");
        var compliance = Panel(workspace, "compliance");
        var packages = Panel(workspace, "packages");
        var yearEnd = Panel(workspace, "year_end");

        var unresolvedReconciliations = reconciliation is null
            ? 1
            : Math.Max(reconciliation.AttentionCount, reconciliation.Status == "current" ? 0 : 1);
        var staleReports = reports is null || reports.TotalCount == 0 || reports.EvidenceUtc is null || reports.Status != "prepared"
            ? 1
            : workspace.Readiness is { IsStale: true }
                || reports.EvidenceUtc.Value < workspace.Readiness?.PreparedUtc
                ? 1
                : 0;
        var missingSignOffs = workspace.CloseInstanceId.HasValue && workspace.SignOffs.Count > 0 ? 0 : 1;
        var incompletePackages = packages is null || packages.TotalCount == 0 || packages.Status != "final"
            ? 1
            : packages.AttentionCount;
        var complianceAmbiguity = compliance is null
            ? 1
            : Math.Max(compliance.AttentionCount, compliance.Status == "current" && compliance.EvidenceUtc.HasValue ? 0 : 1);
        var accessAnomalies = CountAccessAnomalies(workspace, grants, engagements, generatedUtc);
        var failedRollover = yearEnd?.Status == "failed" ? 1 : 0;

        var signals = new[]
        {
            Signal(CloseComplianceReleaseSignalKeys.OverdueCloseTasks, overdueTasks,
                "All active close tasks are within their due date.", "Assign an owner, complete the task, and retain its evidence before release.",
                workspace.Tasks.Count == 0 ? null : workspace.Tasks.Max(x => x.DueUtc), CloseWorkspaceUrl),
            Signal(CloseComplianceReleaseSignalKeys.BlockedCloseTasks, blockedTasks,
                "No active close task has an unresolved blocker.", "Resolve the blocker from its source record, refresh readiness, and retain the resulting evidence.",
                Latest(workspace.Tasks.SelectMany(x => x.Blockers).Select(x => (DateTime?)x.ObservedUtc)), CloseWorkspaceUrl),
            Signal(CloseComplianceReleaseSignalKeys.UnresolvedReconciliations, unresolvedReconciliations,
                "All reconciliation exceptions are resolved.", "Resolve reconciliation conflicts and needs-review items, then refresh the close workspace.",
                reconciliation?.EvidenceUtc, ReconciliationUrl),
            Signal(CloseComplianceReleaseSignalKeys.StaleReports, staleReports,
                "Financial report snapshots are present and newer than close readiness evidence.", "Regenerate the report suite after the latest close changes and retain source drill-downs.",
                reports?.EvidenceUtc, ReportsUrl),
            Signal(CloseComplianceReleaseSignalKeys.MissingSignOffs, missingSignOffs,
                "The selected close has retained sign-off evidence.", "Complete the required preparer, reviewer, and accountant sign-offs before locking or releasing.",
                Latest(workspace.SignOffs.Select(x => (DateTime?)x.OccurredUtc)), CloseWorkspaceUrl, AccountantUrl),
            Signal(CloseComplianceReleaseSignalKeys.MissingEvidence, missingTaskEvidence,
                "Every active task has retained evidence and an authoritative readiness snapshot exists.", "Attach accessible source evidence to each active task and refresh authoritative readiness.",
                workspace.Readiness?.PreparedUtc, CloseWorkspaceUrl),
            Signal(CloseComplianceReleaseSignalKeys.IncompletePackages, incompletePackages,
                "A final audit package is available without incomplete items.", "Regenerate or recover the package, verify its manifest and object checksum, then approve the final package.",
                packages?.EvidenceUtc, PackagesUrl),
            Signal(CloseComplianceReleaseSignalKeys.ComplianceAmbiguity, complianceAmbiguity,
                "Compliance obligations have current evidence and no overdue or due-soon ambiguity.", "Resolve the obligation, confirm the supported submission boundary, and retain filing or manual-submission evidence.",
                compliance?.EvidenceUtc, ComplianceUrl),
            Signal(CloseComplianceReleaseSignalKeys.AccountantAccessAnomalies, accessAnomalies,
                "Accountant grants and engagements are company-bound, effective, approved, and segregated.", "Revoke or correct anomalous grants and engagements, then complete the access review.",
                Latest(grants.Select(x => (DateTime?)x.UpdatedUtc).Concat(engagements.Select(x => (DateTime?)x.UpdatedUtc))), AccountantUrl),
            Signal(CloseComplianceReleaseSignalKeys.FailedRollover, failedRollover,
                "No failed year-end rollover is associated with the selected close evidence.", "Run the documented restore or forward-fix procedure and prove the original evidence hash and source links are preserved.",
                yearEnd?.EvidenceUtc, YearEndUrl)
        };

        var releaseStops = signals.Count(signal => signal.Status == CloseComplianceReleaseSignalStatuses.ReleaseStop);
        var sourceLinks = signals.SelectMany(signal => signal.SourceLinks)
            .Concat(workspace.Panels.Select(panel => panel.DrilldownUrl))
            .Concat(workspace.Tasks.Select(task => task.DrilldownUrl))
            .Concat(workspace.Tasks.SelectMany(task => task.Evidence).Select(evidence => evidence.DrilldownUrl))
            .Concat(workspace.Tasks.SelectMany(task => task.Blockers).Select(blocker => blocker.DrilldownUrl))
            .Where(link => !string.IsNullOrWhiteSpace(link))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var sourceHashes = workspace.Tasks.SelectMany(task => task.Evidence).Select(evidence => evidence.ContentHash)
            .Concat(workspace.Tasks.SelectMany(task => task.Blockers).Select(blocker => blocker.EvidenceHash))
            .Concat(workspace.SignOffs.Select(signOff => signOff.EvidenceHash))
            .Append(workspace.Readiness?.EvidenceHash)
            .Where(hash => !string.IsNullOrWhiteSpace(hash))
            .Select(hash => hash!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var hash = ComputeHash(workspace.CompanyId, workspace.SelectedPeriod?.FiscalPeriodId, signals, sourceLinks,
            sourceHashes);
        return new(workspace.CompanyId, workspace.SelectedPeriod?.FiscalPeriodId, generatedUtc,
            releaseStops == 0 ? CloseComplianceReleaseDecisions.Ready : CloseComplianceReleaseDecisions.NoGo,
            releaseStops, hash, sourceLinks, signals);
    }

    private static int CountAccessAnomalies(AccountingCloseWorkspaceDto workspace,
        IReadOnlyCollection<AccountantGrantDto> grants,
        IReadOnlyCollection<AccountantEngagementDto> engagements,
        DateTime nowUtc)
    {
        var anomalies = grants.Count(grant =>
            grant.CompanyId != workspace.CompanyId
            || grant.Status == "active" && (grant.ApprovedByUserId is null
                || grant.EffectiveFromUtc > nowUtc
                || grant.EffectiveUntilUtc.HasValue && grant.EffectiveUntilUtc.Value <= nowUtc));
        var grantsById = grants.Where(x => x.CompanyId == workspace.CompanyId).ToDictionary(x => x.Id);
        anomalies += engagements.Count(engagement =>
        {
            if (engagement.CompanyId != workspace.CompanyId) return true;
            if (workspace.SelectedPeriod is not null && engagement.FiscalPeriodId.HasValue
                && engagement.FiscalPeriodId != workspace.SelectedPeriod.FiscalPeriodId) return true;
            if (!grantsById.TryGetValue(engagement.GrantId, out var grant) || grant.Status != "active") return true;
            return engagement.Status != "completed" && engagement.AssignedAccountantUserId == engagement.PreparedByUserId;
        });
        return anomalies;
    }

    private static AccountingCloseWorkspacePanelDto? Panel(AccountingCloseWorkspaceDto workspace, string key) =>
        workspace.Panels.FirstOrDefault(x => x.Key == key);

    private static CloseComplianceReleaseSignalDto Signal(string key, int count, string readyExplanation,
        string remediation, DateTime? evidenceUtc, params string[] sourceLinks) =>
        new(key,
            count == 0 ? CloseComplianceReleaseSignalStatuses.Ready : CloseComplianceReleaseSignalStatuses.ReleaseStop,
            count,
            count == 0 ? readyExplanation : $"{count.ToString(CultureInfo.InvariantCulture)} release-blocking item(s) require attention.",
            remediation,
            evidenceUtc,
            sourceLinks);

    private static DateTime? Latest(IEnumerable<DateTime?> values) => values.Where(x => x.HasValue).Max();

    private static string ComputeHash(Guid companyId, Guid? fiscalPeriodId,
        IReadOnlyCollection<CloseComplianceReleaseSignalDto> signals, IReadOnlyCollection<string> sourceLinks,
        IReadOnlyCollection<string> sourceHashes)
    {
        var canonical = new StringBuilder()
            .Append(companyId.ToString("D")).Append('|')
            .Append(fiscalPeriodId?.ToString("D") ?? "none");
        foreach (var signal in signals.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            canonical.Append('|').Append(signal.Key)
                .Append(':').Append(signal.Status)
                .Append(':').Append(signal.Count.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(signal.EvidenceUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "none");
        }
        foreach (var link in sourceLinks.Order(StringComparer.Ordinal)) canonical.Append('|').Append(link);
        foreach (var hash in sourceHashes.Order(StringComparer.Ordinal)) canonical.Append("|sha256:").Append(hash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}

public sealed class CloseComplianceReleaseReadinessService(
    IAccountingCloseWorkspaceService workspaceService,
    IAccountantCollaborationService collaborationService,
    CloseComplianceReleaseReadinessTelemetry telemetry,
    TimeProvider clock) : ICloseComplianceReleaseReadinessService
{
    public async Task<CloseComplianceReleaseReadinessDto> GetAsync(
        GetCloseComplianceReleaseReadinessQuery query,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = telemetry.ActivitySource.StartActivity("close-compliance-release.evaluate");
        activity?.SetTag("company.id", query.CompanyId);
        var workspace = await workspaceService.GetAsync(new(query.CompanyId, query.FiscalPeriodId), cancellationToken);
        var grants = await collaborationService.ListGrantsAsync(query.CompanyId, cancellationToken);
        var engagements = await collaborationService.ListEngagementsAsync(query.CompanyId, cancellationToken);
        var result = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, grants, engagements,
            clock.GetUtcNow().UtcDateTime);
        telemetry.Evaluations.Add(1, new KeyValuePair<string, object?>("decision", result.Decision));
        telemetry.ReleaseStops.Add(result.ReleaseStopCount);
        telemetry.EvaluationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("decision", result.Decision));
        return result;
    }
}
