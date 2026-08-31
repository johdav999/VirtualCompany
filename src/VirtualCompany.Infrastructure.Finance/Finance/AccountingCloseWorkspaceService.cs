using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingCloseWorkspaceTelemetry : IDisposable
{
    public const string MeterName = "VirtualCompany.Finance.CloseWorkspace";
    public const string ActivitySourceName = "VirtualCompany.Finance.CloseWorkspace";
    private readonly Meter _meter = new(MeterName);
    internal readonly ActivitySource ActivitySource = new(ActivitySourceName);
    internal Histogram<double> LoadDuration { get; }
    internal Counter<long> Loads { get; }

    public AccountingCloseWorkspaceTelemetry()
    {
        LoadDuration = _meter.CreateHistogram<double>("finance.close_workspace.load.duration", "ms");
        Loads = _meter.CreateCounter<long>("finance.close_workspace.load.count");
    }

    public void Dispose()
    {
        ActivitySource.Dispose();
        _meter.Dispose();
    }
}

public static class AccountingCloseWorkspaceActionPolicy
{
    public static IReadOnlyList<string> Evaluate(CompanyMembershipRole role, AccountingCloseDto? close,
        AccountingCloseGovernanceDto? governance, AuditPackageDto? package,
        YearEndRunSummaryDto? yearEnd, bool hasOpenAccountantEngagement) => Evaluate(role,
            close?.Tasks.SelectMany(x => x.AllowedActions).ToArray() ?? [], governance?.AllowedActions ?? [],
            package?.Status, yearEnd is not null, hasOpenAccountantEngagement);

    public static IReadOnlyList<string> Evaluate(CompanyMembershipRole role,
        IReadOnlyCollection<string> taskActions, IReadOnlyCollection<string> governanceActions,
        string? packageStatus, bool hasYearEnd, bool hasOpenAccountantEngagement)
    {
        var canManage = role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;
        var actions = new List<string>();
        if (canManage && taskActions.Contains("complete", StringComparer.Ordinal))
            actions.Add(AccountingCloseWorkspaceActions.CompleteTask);
        if (canManage)
        {
            if (governanceActions.Any(x => x is "prepare" or "refresh")) actions.Add(AccountingCloseWorkspaceActions.RefreshReadiness);
            if (governanceActions.Contains("propose_waiver", StringComparer.Ordinal)) actions.Add(AccountingCloseWorkspaceActions.ProposeWaiver);
            if (governanceActions.Any(x => x is "submit" or "approve" or "reject")) actions.Add(AccountingCloseWorkspaceActions.SignOff);
            if (governanceActions.Contains("lock", StringComparer.Ordinal)) actions.Add(AccountingCloseWorkspaceActions.Lock);
            if (governanceActions.Contains("request_reopen", StringComparer.Ordinal)) actions.Add(AccountingCloseWorkspaceActions.RequestReopen);
            if (governanceActions.Contains("execute_reopen", StringComparer.Ordinal)) actions.Add(AccountingCloseWorkspaceActions.ExecuteReopen);
        }

        if (canManage)
        {
            if (packageStatus is null || packageStatus is AuditPackageStatuses.Cancelled or AuditPackageStatuses.Failed or AuditPackageStatuses.Expired)
                actions.Add(AccountingCloseWorkspaceActions.RequestPackage);
            if (packageStatus == AuditPackageStatuses.PendingApproval) actions.Add(AccountingCloseWorkspaceActions.ApprovePackage);
            if (packageStatus is AuditPackageStatuses.PendingApproval or AuditPackageStatuses.Queued or
                AuditPackageStatuses.Generating or AuditPackageStatuses.RetryScheduled)
                actions.Add(AccountingCloseWorkspaceActions.CancelPackage);
            actions.Add(AccountingCloseWorkspaceActions.OpenYearEnd);
            if (hasYearEnd) actions.Add(AccountingCloseWorkspaceActions.RunYearEndAction);
        }
        else if (role == CompanyMembershipRole.Accountant && hasOpenAccountantEngagement)
        {
            actions.Add(AccountingCloseWorkspaceActions.SignOff);
        }

        return actions.Distinct(StringComparer.Ordinal).ToArray();
    }
}

public sealed class AccountingCloseWorkspaceService(
    VirtualCompanyDbContext db,
    ICompanyMembershipContextResolver memberships,
    IAccountingCloseService closeService,
    IAccountingCloseGovernanceService governanceService,
    IAdvancedReconciliationReadService reconciliationService,
    IComplianceObligationService complianceService,
    IAuditPackageService packageService,
    IYearEndRolloverService yearEndService,
    IAccountantCollaborationService collaborationService,
    INotificationInboxService notifications,
    AccountingCloseWorkspaceTelemetry telemetry,
    TimeProvider clock) : IAccountingCloseWorkspaceService
{
    public async Task<AccountingCloseWorkspaceDto> GetAsync(GetAccountingCloseWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var activity = telemetry.ActivitySource.StartActivity("close-workspace.load");
        activity?.SetTag("company.id", query.CompanyId);

        var membership = await memberships.ResolveAsync(query.CompanyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Close workspace access is denied.");
        if (membership.Status != CompanyMembershipStatus.Active)
            throw new UnauthorizedAccessException("An active company membership is required.");

        var periods = await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderByDescending(x => x.StartUtc).Take(60)
            .Select(x => new { x.Id, x.Name, x.StartUtc, x.EndUtc, x.IsClosed })
            .ToArrayAsync(cancellationToken);
        var closeList = await closeService.ListAsync(new(query.CompanyId, query.FiscalPeriodId, null, 0, 100), cancellationToken);
        var closes = closeList.Items;
        var selectedClose = query.CloseInstanceId.HasValue
            ? closes.FirstOrDefault(x => x.Id == query.CloseInstanceId.Value)
                ?? await closeService.GetAsync(new(query.CompanyId, query.CloseInstanceId.Value), cancellationToken)
            : query.FiscalPeriodId.HasValue
                ? closes.FirstOrDefault(x => x.FiscalPeriodId == query.FiscalPeriodId.Value)
                : closes.OrderByDescending(x => x.UpdatedUtc).FirstOrDefault();
        var selectedPeriodId = query.FiscalPeriodId ?? selectedClose?.FiscalPeriodId ?? periods.FirstOrDefault()?.Id;
        var periodDtos = periods.Select(period =>
        {
            var close = closes.FirstOrDefault(x => x.FiscalPeriodId == period.Id);
            return new AccountingCloseWorkspacePeriodDto(period.Id, period.Name, period.StartUtc, period.EndUtc,
                period.IsClosed, close?.Id, close?.Status, close?.UpdatedUtc);
        }).ToArray();
        var selectedPeriod = periodDtos.FirstOrDefault(x => x.FiscalPeriodId == selectedPeriodId);

        AccountingCloseGovernanceDto? governance = null;
        if (selectedClose is not null)
            governance = await governanceService.GetAsync(new(query.CompanyId, selectedClose.Id), cancellationToken);

        var reconciliation = await reconciliationService.ListAsync(new(query.CompanyId, Limit: 100), cancellationToken);
        var from = selectedPeriod is null ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMonths(-1)) : DateOnly.FromDateTime(selectedPeriod.StartUtc);
        var to = selectedPeriod is null ? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddMonths(2)) : DateOnly.FromDateTime(selectedPeriod.EndUtc);
        var compliance = await complianceService.GetCalendarAsync(new(query.CompanyId, from, to), cancellationToken);
        var packageWorkspace = await packageService.ListAsync(new(query.CompanyId, selectedPeriodId, 0, 100), cancellationToken);
        var latestPackage = packageWorkspace.Packages.OrderByDescending(x => x.UpdatedUtc).FirstOrDefault();
        var yearEnds = await yearEndService.ListAsync(new(query.CompanyId, 20), cancellationToken);
        var yearEnd = yearEnds.FirstOrDefault(x => selectedPeriod is not null && x.FiscalYearEnd >= DateOnly.FromDateTime(selectedPeriod.StartUtc) && x.FiscalYearEnd <= DateOnly.FromDateTime(selectedPeriod.EndUtc))
            ?? yearEnds.FirstOrDefault();
        var engagements = await collaborationService.ListEngagementsAsync(query.CompanyId, cancellationToken);
        var periodEngagements = engagements.Where(x => !selectedPeriodId.HasValue || x.FiscalPeriodId == selectedPeriodId).ToArray();
        var inbox = await notifications.GetInboxAsync(query.CompanyId, cancellationToken);

        var reportCount = selectedPeriodId.HasValue
            ? await db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == selectedPeriodId.Value)
                .Select(x => x.ReportKind).Distinct().CountAsync(cancellationToken)
            : 0;
        var reportEvidenceUtc = selectedPeriodId.HasValue
            ? await db.FinancialReportSuiteSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == selectedPeriodId.Value)
                .MaxAsync(x => (DateTime?)x.CreatedUtc, cancellationToken)
            : null;
        var latestJournalUtc = selectedPeriodId.HasValue
            ? await db.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == query.CompanyId && x.FiscalPeriodId == selectedPeriodId.Value)
                .MaxAsync(x => (DateTime?)x.UpdatedUtc, cancellationToken)
            : null;

        var readiness = BuildReadiness(governance, selectedClose, latestJournalUtc);
        var tasks = (selectedClose?.Tasks ?? []).Select(MapTask).ToArray();
        var panels = BuildPanels(reconciliation, compliance, packageWorkspace, latestPackage, reportCount,
            reportEvidenceUtc, yearEnd, periodEngagements);
        var allowed = AccountingCloseWorkspaceActionPolicy.Evaluate(membership.MembershipRole, selectedClose,
            governance, latestPackage, yearEnd, periodEngagements.Any(x => x.Status != "completed"));
        var generated = clock.GetUtcNow().UtcDateTime;

        telemetry.Loads.Add(1, new KeyValuePair<string, object?>("role", membership.MembershipRole.ToStorageValue()));
        telemetry.LoadDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new KeyValuePair<string, object?>("has_close", selectedClose is not null));

        return new(query.CompanyId, membership.CompanyName, membership.MembershipRole.ToStorageValue(), generated,
            selectedPeriod, selectedClose?.Id, selectedClose?.Name, selectedClose?.Status, selectedClose?.Version,
            periodDtos, readiness, tasks, panels,
            (governance?.SignOffs ?? []).OrderByDescending(x => x.OccurredUtc).Select(x =>
                new AccountingCloseWorkspaceSignOffDto(x.Id, x.Action, x.ActorRole, x.ActorUserId,
                    x.OccurredUtc, x.Reason, x.EvidenceHash)).ToArray(),
            inbox.Notifications.OrderByDescending(x => x.CreatedAt).Take(8).Select(x =>
                new AccountingCloseWorkspaceNotificationDto(x.Id, x.Priority, x.Title, x.Body,
                    x.Status, x.CreatedAt, x.ActionUrl)).ToArray(), allowed);
    }

    private static AccountingCloseWorkspaceReadinessDto? BuildReadiness(AccountingCloseGovernanceDto? governance,
        AccountingCloseDto? close, DateTime? latestJournalUtc)
    {
        var snapshot = governance?.CurrentSnapshot;
        if (snapshot is null) return null;
        var checkBlockers = snapshot.Checks.Where(x => x.IsBlocking).Select(check => new AccountingCloseWorkspaceBlockerDto(
            check.Code, check.Category, check.Message, SafeNextAction(check.Category), null, "open", 1,
            check.ObservedUtc, Drilldown(check.Category), check.IsWaivable, check.EvidenceHash));
        var taskBlockers = (close?.Tasks ?? []).SelectMany(task => task.Blockers.Where(x => x.Status == "open").Select(blocker =>
            new AccountingCloseWorkspaceBlockerDto(blocker.ReasonCode, task.Title, blocker.Explanation,
                blocker.SafeNextAction, task.OwnerUserId, blocker.Status, task.Evidence.Count, blocker.CreatedUtc,
                $"/finance/accounting/close-workspace?taskId={task.Id:D}", false, null)));
        var blockers = checkBlockers.Concat(taskBlockers).OrderBy(x => x.ObservedUtc).ToArray();
        var warningCount = snapshot.Checks.Count(x => !x.IsBlocking);
        DateTime? latestTaskUtc = close?.Tasks.Count > 0 ? (DateTime?)close.Tasks.Max(x => x.UpdatedUtc) : null;
        var newestSource = new[] { latestTaskUtc, latestJournalUtc }.Where(x => x.HasValue).Max();
        return new(snapshot.Id, snapshot.SnapshotNumber, snapshot.Status, snapshot.IsReady,
            snapshot.EvidenceHash, snapshot.PreparedUtc, snapshot.Version, blockers.Length, warningCount,
            newestSource.HasValue && newestSource.Value > snapshot.PreparedUtc, blockers);
    }

    private static AccountingCloseWorkspaceTaskDto MapTask(AccountingCloseTaskDto task)
    {
        var evidence = task.Evidence.Select(x => new AccountingCloseWorkspaceEvidenceDto(x.Id, x.DocumentId, x.EvidenceType,
            x.DocumentTitle, x.ContentHash, x.LinkedUtc, $"/documents/{x.DocumentId:D}")).ToArray();
        var blockers = task.Blockers.Where(x => x.Status == "open").Select(x => new AccountingCloseWorkspaceBlockerDto(
            x.ReasonCode, task.Title, x.Explanation, x.SafeNextAction, task.OwnerUserId, x.Status,
            evidence.Length, x.CreatedUtc, $"/finance/accounting/close-workspace?taskId={task.Id:D}", false, null)).ToArray();
        return new(task.Id, task.Key, task.Title, task.Status, task.OwnerUserId, task.OwnerRole, task.DueUtc,
            task.Sequence, task.Version, task.PredecessorTaskIds, task.BlockingReasonCodes, evidence, blockers,
            task.AllowedActions, $"/finance/accounting/close-workspace?taskId={task.Id:D}");
    }

    private static IReadOnlyList<AccountingCloseWorkspacePanelDto> BuildPanels(
        AdvancedReconciliationWorkspaceDto reconciliation, ComplianceCalendarDto compliance,
        AuditPackageWorkspaceDto packages, AuditPackageDto? latestPackage, int reportCount,
        DateTime? reportEvidenceUtc, YearEndRunSummaryDto? yearEnd,
        IReadOnlyList<AccountantEngagementDto> engagements) =>
    [
        new("reconciliations", "Reconciliations", reconciliation.Metrics.NeedsReviewCount == 0 ? "current" : "attention",
            reconciliation.Groups.Count, reconciliation.Metrics.NeedsReviewCount + reconciliation.Metrics.ConflictCount,
            reconciliation.Groups.Count == 0 ? null : reconciliation.Groups.Max(x => x.UpdatedUtc),
            "/finance/accounting/reconciliation", []),
        new("reports", "Reports", reportCount > 0 ? "prepared" : "not_started", reportCount, 0,
            reportEvidenceUtc, "/finance/accounting/reports", []),
        new("compliance", "Compliance obligations", compliance.OverdueCount == 0 ? "current" : "attention",
            compliance.Obligations.Count, compliance.OverdueCount + compliance.DueSoonCount,
            compliance.Obligations.Count == 0 ? null : compliance.Obligations.Max(x => x.UpdatedUtc),
            "/finance/accounting/compliance-calendar", []),
        new("packages", "Audit package", latestPackage?.Status ?? "not_started", packages.TotalCount,
            packages.IncompleteCount, latestPackage?.UpdatedUtc, "/finance/accounting/audit-packages", []),
        new("year_end", "Year-end rollover", yearEnd?.Status ?? "not_started", yearEnd is null ? 0 : 1,
            yearEnd?.BlockerCount ?? 0, yearEnd?.UpdatedUtc, "/finance/accounting/year-end", yearEnd is null ? [] : ["open"]),
        new("accountant", "Accountant engagement", engagements.Any(x => x.Status != "completed") ? "in_review" : "current",
            engagements.Count, engagements.Sum(x => x.ReviewItems.Count(i => i.Status != "resolved") + x.EvidenceRequests.Count(i => i.Status != "resolved")),
            engagements.Count == 0 ? null : engagements.Max(x => x.UpdatedUtc), "/accountant/portfolio", [])
    ];

    private static string Drilldown(string category) => category switch
    {
        "bank" => "/finance/accounting/reconciliation",
        "vat_tax" => "/finance/accounting/compliance-calendar",
        "exports" => "/finance/accounting/reports",
        "documents" => "/documents",
        _ => "/finance/accounting/journals"
    };

    private static string SafeNextAction(string category) => category switch
    {
        "bank" => "Review and complete the reconciliation evidence.",
        "vat_tax" => "Review the VAT obligation and retained submission evidence.",
        "exports" => "Refresh the report snapshot and inspect its source drill-down.",
        "documents" => "Attach an accessible source document to the close task.",
        _ => "Open the source records, correct the issue, then refresh readiness."
    };
}
