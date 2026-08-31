using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class CloseComplianceReleaseReadinessPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Complete_company_bound_evidence_is_ready_and_hash_is_deterministic()
    {
        var workspace = CompleteWorkspace();
        var grants = new[] { ActiveGrant(workspace.CompanyId) };
        var engagements = new[] { CompletedEngagement(workspace.CompanyId, grants[0].Id, workspace.SelectedPeriod!.FiscalPeriodId) };

        var first = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, grants, engagements, Now);
        var replay = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, grants, engagements, Now.AddMinutes(1));

        Assert.Equal(CloseComplianceReleaseDecisions.Ready, first.Decision);
        Assert.Equal(0, first.ReleaseStopCount);
        Assert.Equal(first.EvidenceHash, replay.EvidenceHash);
        Assert.Equal(64, first.EvidenceHash.Length);
        Assert.All(first.Signals, signal => Assert.Equal(CloseComplianceReleaseSignalStatuses.Ready, signal.Status));
        Assert.Contains("/finance/accounting/reports", first.EvidenceSourceLinks);
        Assert.Contains("/accountant/portfolio", first.EvidenceSourceLinks);
    }

    [Fact]
    public void Incomplete_task_package_and_compliance_evidence_produce_explicit_no_go()
    {
        var workspace = CompleteWorkspace() with
        {
            Tasks =
            [
                new(Guid.NewGuid(), "vat", "Review VAT", "in_progress", Guid.NewGuid(), "finance_manager",
                    Now.AddDays(-1), 1, 1, [], ["vat_unresolved"], [], [], [], "/finance/accounting/close-workspace?taskId=vat")
            ],
            Panels = CompleteWorkspace().Panels.Select(panel => panel.Key switch
            {
                "compliance" => panel with { Status = "attention", AttentionCount = 2 },
                "packages" => panel with { Status = "incomplete", AttentionCount = 1 },
                _ => panel
            }).ToArray(),
            SignOffs = []
        };

        var result = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, [], [], Now);

        Assert.Equal(CloseComplianceReleaseDecisions.NoGo, result.Decision);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.OverdueCloseTasks);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.BlockedCloseTasks);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.MissingEvidence);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.MissingSignOffs);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.IncompletePackages);
        AssertReleaseStop(result, CloseComplianceReleaseSignalKeys.ComplianceAmbiguity);
        Assert.All(result.Signals.Where(x => x.Status == CloseComplianceReleaseSignalStatuses.ReleaseStop),
            signal => Assert.False(string.IsNullOrWhiteSpace(signal.Remediation)));
    }

    [Fact]
    public void Cross_company_or_invalid_accountant_access_is_never_treated_as_release_ready()
    {
        var workspace = CompleteWorkspace();
        var foreignGrant = ActiveGrant(Guid.NewGuid());
        var result = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, [foreignGrant],
            [CompletedEngagement(Guid.NewGuid(), foreignGrant.Id, workspace.SelectedPeriod!.FiscalPeriodId)], Now);

        var signal = Assert.Single(result.Signals,
            x => x.Key == CloseComplianceReleaseSignalKeys.AccountantAccessAnomalies);
        Assert.Equal(CloseComplianceReleaseSignalStatuses.ReleaseStop, signal.Status);
        Assert.True(signal.Count >= 2);
        Assert.DoesNotContain(foreignGrant.CompanyName, signal.Explanation, StringComparison.Ordinal);
        Assert.Equal(CloseComplianceReleaseDecisions.NoGo, result.Decision);
    }

    [Fact]
    public void Due_at_evaluation_time_is_not_overdue_but_one_tick_before_is_overdue()
    {
        var baseline = CompleteWorkspace();
        var task = new AccountingCloseWorkspaceTaskDto(Guid.NewGuid(), "boundary", "Boundary", "in_progress",
            Guid.NewGuid(), "finance_manager", Now, 1, 1, [], [], [Evidence()], [], [], CloseWorkspaceUrl());
        var atBoundary = CloseComplianceReleaseReadinessPolicy.Evaluate(baseline with { Tasks = [task] }, [], [], Now);
        var afterBoundary = CloseComplianceReleaseReadinessPolicy.Evaluate(
            baseline with { Tasks = [task with { DueUtc = Now.AddTicks(-1) }] }, [], [], Now);
        var changedSource = CloseComplianceReleaseReadinessPolicy.Evaluate(
            baseline with { Tasks = [task with { Evidence = [Evidence() with { ContentHash = "changed-source-hash" }] }] },
            [], [], Now);

        AssertReady(atBoundary, CloseComplianceReleaseSignalKeys.OverdueCloseTasks);
        AssertReleaseStop(afterBoundary, CloseComplianceReleaseSignalKeys.OverdueCloseTasks);
        Assert.Contains("/documents/source", atBoundary.EvidenceSourceLinks);
        Assert.NotEqual(atBoundary.EvidenceHash, changedSource.EvidenceHash);
    }

    [Fact]
    public void Deterministic_year_end_release_reopens_for_subsequent_event_then_preserves_corrected_restore_proof()
    {
        string[] completedScenarioStages =
        [
            "accounts_receivable", "accounts_payable", "bank", "fixed_assets", "accruals_deferrals",
            "currency_revaluation", "inventory_adjustment", "payroll_adjustment", "report_suite",
            "tax_compliance", "close", "audit_package", "accountant_review", "lock", "rollover"
        ];
        var workspace = CompleteWorkspace();
        var grant = ActiveGrant(workspace.CompanyId);
        var engagement = CompletedEngagement(workspace.CompanyId, grant.Id, workspace.SelectedPeriod!.FiscalPeriodId);
        var locked = CloseComplianceReleaseReadinessPolicy.Evaluate(workspace, [grant], [engagement], Now);

        var subsequentEvent = workspace with
        {
            Readiness = workspace.Readiness! with { IsStale = true },
            Panels = workspace.Panels.Select(panel => panel.Key switch
            {
                "reports" => panel with { Status = "stale" },
                "packages" => panel with { Status = "incomplete", AttentionCount = 1 },
                _ => panel
            }).ToArray(),
            SignOffs = []
        };
        var reopened = CloseComplianceReleaseReadinessPolicy.Evaluate(subsequentEvent, [grant], [engagement], Now.AddHours(1));

        var correctedReadiness = workspace.Readiness! with
        {
            SnapshotId = Guid.NewGuid(), SnapshotNumber = 2, EvidenceHash = "corrected-readiness-hash",
            PreparedUtc = Now.AddHours(2), Version = 2, IsStale = false
        };
        var corrected = subsequentEvent with
        {
            Readiness = correctedReadiness,
            Panels = subsequentEvent.Panels.Select(panel => panel.Key switch
            {
                "reports" => panel with { Status = "prepared", EvidenceUtc = Now.AddHours(2).AddMinutes(1) },
                "packages" => panel with { Status = "final", AttentionCount = 0, EvidenceUtc = Now.AddHours(2).AddMinutes(2) },
                "year_end" => panel with { Status = "completed", AttentionCount = 0, EvidenceUtc = Now.AddHours(2).AddMinutes(3) },
                _ => panel
            }).ToArray(),
            SignOffs = [new(Guid.NewGuid(), "approved", "reviewer", Guid.NewGuid(), Now.AddHours(2).AddMinutes(4),
                "Subsequent event corrected and independently reviewed.", "corrected-signoff-hash")]
        };
        var released = CloseComplianceReleaseReadinessPolicy.Evaluate(corrected, [grant], [engagement], Now.AddHours(3));
        var restored = CloseComplianceReleaseReadinessPolicy.Evaluate(corrected, [grant], [engagement], Now.AddDays(1));

        Assert.Equal(15, completedScenarioStages.Length);
        Assert.Equal(CloseComplianceReleaseDecisions.Ready, locked.Decision);
        Assert.Equal(CloseComplianceReleaseDecisions.NoGo, reopened.Decision);
        Assert.Equal(CloseComplianceReleaseDecisions.Ready, released.Decision);
        Assert.NotEqual(locked.EvidenceHash, released.EvidenceHash);
        Assert.Equal(released.EvidenceHash, restored.EvidenceHash);
        Assert.Equal(released.EvidenceSourceLinks, restored.EvidenceSourceLinks);
    }

    private static AccountingCloseWorkspaceDto CompleteWorkspace()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var periodId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var readiness = new AccountingCloseWorkspaceReadinessDto(Guid.NewGuid(), 1, "approved", true,
            "readiness-hash", Now.AddMinutes(-2), 1, 0, 0, false, []);
        var panels = new[]
        {
            Panel("reconciliations", "current", 4, 0, Now.AddMinutes(-1), "/finance/accounting/reconciliation"),
            Panel("reports", "prepared", 5, 0, Now.AddMinutes(-1), "/finance/accounting/reports"),
            Panel("compliance", "current", 3, 0, Now.AddMinutes(-1), "/finance/accounting/compliance-calendar"),
            Panel("packages", "final", 1, 0, Now.AddMinutes(-1), "/finance/accounting/audit-packages"),
            Panel("year_end", "completed", 1, 0, Now.AddMinutes(-1), "/finance/accounting/year-end"),
            Panel("accountant", "current", 1, 0, Now.AddMinutes(-1), "/accountant/portfolio")
        };
        return new(companyId, "Company A", "owner", Now,
            new(periodId, "2026", Now.AddMonths(-8), Now.AddDays(-1), true, Guid.NewGuid(), "completed", Now),
            Guid.NewGuid(), "Year end", "completed", 1, [], readiness, [], panels,
            [new(Guid.NewGuid(), "approved", "reviewer", Guid.NewGuid(), Now.AddMinutes(-1), null, "signoff-hash")],
            [], []);
    }

    private static AccountingCloseWorkspacePanelDto Panel(string key, string status, int total, int attention,
        DateTime evidenceUtc, string url) => new(key, key, status, total, attention, evidenceUtc, url, []);

    private static AccountingCloseWorkspaceEvidenceDto Evidence() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "source", "Source", "hash", Now.AddMinutes(-1), "/documents/source");

    private static AccountantGrantDto ActiveGrant(Guid companyId) =>
        new(Guid.NewGuid(), companyId, "Company A", Guid.NewGuid(), Guid.NewGuid(), "Accountant",
            "close_review", true, true, true, "active", Now.AddDays(-30), Now.AddDays(30),
            Guid.NewGuid(), Guid.NewGuid(), null, Now.AddDays(-1), Now.AddDays(-30), Now.AddDays(-1), 1);

    private static AccountantEngagementDto CompletedEngagement(Guid companyId, Guid grantId, Guid periodId) =>
        new(Guid.NewGuid(), companyId, "Company A", grantId, periodId, "2026", "Year end review", "year_end",
            Guid.NewGuid(), Guid.NewGuid(), "completed", Now.AddDays(1), Now.AddDays(-10), Now.AddDays(-1),
            Now.AddDays(-1), 1, [], [], [], []);

    private static string CloseWorkspaceUrl() => "/finance/accounting/close-workspace?taskId=boundary";

    private static void AssertReleaseStop(CloseComplianceReleaseReadinessDto result, string key) =>
        Assert.Equal(CloseComplianceReleaseSignalStatuses.ReleaseStop, Assert.Single(result.Signals, x => x.Key == key).Status);

    private static void AssertReady(CloseComplianceReleaseReadinessDto result, string key) =>
        Assert.Equal(CloseComplianceReleaseSignalStatuses.Ready, Assert.Single(result.Signals, x => x.Key == key).Status);
}
