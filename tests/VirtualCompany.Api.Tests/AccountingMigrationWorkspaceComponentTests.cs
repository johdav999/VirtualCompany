using Bunit;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Web.Components.Finance;
using VirtualCompany.Web.Localization.Formatting;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingMigrationWorkspaceComponentTests
{
    [Fact]
    public void Active_assessment_shows_plain_gap_ownership_evidence_impact_and_next_action()
    {
        using var context = CreateContext();
        var gap = new AccountingProviderSwitchGapResponse
        {
            Id = Guid.NewGuid(), Category = "missing_tax_mapping", DatasetKey = "tax", Severity = "blocking",
            IsBlocking = true, Explanation = "Three tax treatments have no safe target representation.",
            OperatorAction = "Review the tax mapping with the accounting owner.", CreatedUtc = DateTime.UtcNow
        };
        var cut = Render(context, assessment: new()
        {
            ProgressPercent = 100, HasBlockingGaps = true, Gaps = [gap],
            Datasets = [new() { EndpointRole = "source", DatasetKey = "tax", FinancialTotal = 12500m, Currency = "SEK" }]
        }, guidance: new()
        {
            SwitchVersion = 4, ResponsibleParty = "Laura and the accounting administrator",
            NextCheckpoint = "Resolve the blocking tax mapping.", Blockers = [gap.Explanation],
            AllowedActions = ["Review the mapping evidence"], DataSources = ["persisted assessment"]
        });

        Assert.Contains("Three tax treatments", cut.Find("[data-testid='migration-gap-detail']").TextContent);
        Assert.Contains("12", cut.Find("[data-testid='migration-gap-detail']").TextContent);
        Assert.Contains("SEK", cut.Find("[data-testid='migration-gap-detail']").TextContent);
        Assert.Contains("Laura and the accounting administrator", cut.Markup);
        Assert.Contains("Review the tax mapping", cut.Markup);
        Assert.DoesNotContain("accounting_provider_switch", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approval_and_ambiguous_outcome_states_are_distinct_and_action_safe()
    {
        using var context = CreateContext();
        var planApprovalId = Guid.NewGuid();
        var activationApprovalId = Guid.NewGuid();
        var cut = Render(context,
            plan: new()
            {
                Plan = new() { Id = Guid.NewGuid(), ApprovalRequestId = planApprovalId, ApprovalStatus = "approved", IsCurrent = true, IsApprovedAndCurrent = true },
                IsReady = true
            },
            cutover: new()
            {
                Id = Guid.NewGuid(), ProviderReconciliationRequired = true, Version = 2,
                ActivationApproval = new() { ApprovalRequestId = activationApprovalId, Status = "pending" },
                AllowedActions = new() { RequiresProviderReconciliation = true, CanRetry = false, CanRecoverSource = true }
            });

        var approvals = cut.Find("[data-testid='migration-approvals']").TextContent;
        Assert.Contains("Plan approval", approvals);
        Assert.Contains("Activation approval", approvals);
        Assert.NotEqual(planApprovalId, activationApprovalId);
        Assert.Contains(planApprovalId.ToString("D"), cut.Markup);
        Assert.Contains(activationApprovalId.ToString("D"), cut.Markup);
        Assert.Contains("Provider success is uncertain", cut.Find("[data-testid='ambiguous-provider-outcome']").TextContent);
        Assert.DoesNotContain("Activate target authority", cut.Markup);
    }

    [Fact]
    public void Partial_failure_and_view_only_states_remain_accessible()
    {
        using var context = CreateContext();
        var cut = Render(context, canManage: false, partialFailures: ["Rehearsal evidence"]);

        Assert.NotNull(cut.Find("[data-testid='partial-provider-failure'][role='status']"));
        Assert.Contains("View-only access", cut.Find("[data-testid='migration-no-permission']").TextContent);
        Assert.NotNull(cut.Find("[aria-current='step']"));
        Assert.DoesNotContain("Start read-only assessment", cut.Markup);
    }

    [Theory]
    [InlineData("blocked", "migration-recovery-state", "Migration blocked")]
    [InlineData("recovery", "migration-recovery-state", "Recovery in progress")]
    [InlineData("cancelled", "migration-cancelled-state", "Migration cancelled")]
    [InlineData("completed", "migration-completed-state", "Migration completed")]
    public void Lifecycle_states_are_explicit_and_plain_english(string status, string testId, string expected)
    {
        using var context = CreateContext();
        var cut = Render(context, status: status);

        Assert.Contains(expected, cut.Find($"[data-testid='{testId}']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_guidance_and_internal_readiness_explain_safe_setup_action()
    {
        using var context = CreateContext();
        var cut = Render(context,
            guidance: new()
            {
                SwitchVersion = 3, CurrentStep = "map", NextCheckpoint = "Refresh evidence.",
                ResponsibleParty = "Accounting administrator"
            },
            internalReadiness: new()
            {
                IsReady = false,
                Checks = [new() { CheckKey = "fiscal_period", IsReady = false, IsBlocking = true,
                    Explanation = "Create the target fiscal period before continuing." }]
            });

        Assert.Contains("Evidence changed", cut.Find("[data-testid='stale-migration-guidance']").TextContent);
        var readiness = cut.Find("[data-testid='migration-internal-readiness']");
        Assert.Contains("Create the target fiscal period", readiness.TextContent);
        Assert.Contains("/finance/accounting/setup", readiness.QuerySelector("a")?.GetAttribute("href"));
    }

    [Fact]
    public void Post_activation_monitoring_shows_checks_incidents_and_only_safe_actions()
    {
        using var context = CreateContext();
        var cut = Render(context, status: "monitoring", monitoring: new()
        {
            Status = "attention_required", WindowDays = 14, WindowEndsUtc = DateTime.UtcNow.AddDays(7),
            LastSuccessfulCheckUtc = DateTime.UtcNow, CheckSequence = 3, Version = 8,
            Checks = [new() { CheckKey = "provider_sync_health", Status = "healthy", Explanation = "Provider sync is healthy." }],
            Incidents = [new() { Id = Guid.NewGuid(), CheckKey = "financial_controls", IsBlocking = true,
                Status = "open", Explanation = "Trial balance differs.", Version = 2 }],
            AllowedActions = new() { CanRunNow = true, CanCreateCorrectiveCutover = true,
                Explanation = "Resolve blocking discrepancies before closure." }
        }, futurePeriodAvailable: true);

        var monitoring = cut.Find("[data-testid='migration-post-activation-monitoring']");
        Assert.Contains("Provider sync is healthy", monitoring.TextContent);
        Assert.Contains("Trial balance differs", monitoring.TextContent);
        Assert.Contains("Create corrective cutover", monitoring.TextContent);
        Assert.DoesNotContain("Accept exception", monitoring.TextContent);
        Assert.DoesNotContain("Close monitoring", monitoring.TextContent);
    }

    private static IRenderedComponent<AccountingMigrationWorkspace> Render(TestContext context,
        AccountingProviderSwitchAssessmentResponse? assessment = null,
        AccountingMigrationGuidanceResponse? guidance = null,
        AccountingProviderSwitchPlanReadinessResponse? plan = null,
        AccountingProviderSwitchCutoverResponse? cutover = null,
        AccountingProviderSwitchInternalReadinessResponse? internalReadiness = null,
        AccountingProviderSwitchMonitoringResponse? monitoring = null,
        string status = "ready_for_planning",
        bool canManage = true,
        IReadOnlyList<string>? partialFailures = null,
        bool futurePeriodAvailable = false)
    {
        var companyId = Guid.NewGuid();
        return context.RenderComponent<AccountingMigrationWorkspace>(parameters => parameters
            .Add(x => x.CompanyId, companyId)
            .Add(x => x.Switch, new AccountingProviderSwitchResponse
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Version = 4, Status = status,
                StatusLabel = "Migration status", EffectiveFrom = new DateOnly(2026, 10, 1),
                Source = new() { Kind = "external", ProviderKey = "fortnox", DisplayName = "Fortnox" },
                Target = new() { Kind = "internal", DisplayName = "Virtual Company" }
            })
            .Add(x => x.AllowedActions, new AccountingProviderSwitchAllowedActionsResponse
            {
                Version = 4, AllowedTransitions = ["plan_awaiting_approval"], Explanation = "Review current evidence."
            })
            .Add(x => x.Assessment, assessment)
            .Add(x => x.Guidance, guidance)
            .Add(x => x.PlanReadiness, plan)
            .Add(x => x.InternalReadiness, internalReadiness)
            .Add(x => x.Cutover, cutover)
            .Add(x => x.Monitoring, monitoring)
            .Add(x => x.FuturePeriodAvailable, futurePeriodAvailable)
            .Add(x => x.PartialFailures, partialFailures ?? [])
            .Add(x => x.CanManage, canManage));
    }

    private static TestContext CreateContext()
    {
        var context = new TestContext();
        context.Services.AddLocalization();
        var presentationContext = new CompanyPresentationContext();
        presentationContext.SetFormattingCulture("en-US");
        context.Services.AddSingleton<ICompanyPresentationContext>(presentationContext);
        context.Services.AddSingleton<ILocalDateTimeFormatter, LocalDateTimeFormatter>();
        context.Services.AddSingleton<INumberFormatter, NumberFormatter>();
        context.Services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        return context;
    }
}
