using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;

namespace VirtualCompany.Api.Tests;

public sealed class Release3IntegratedReviewTests
{
    [Fact]
    public async Task Saved_scenario_to_reviewed_owned_work_to_scheduled_inbox_preserves_original_evidence()
    {
        var clock = new BriefingCadenceFixture.Clock();
        using var factory = new TestWebApplicationFactory(clock);
        var seed = await DecisionWorkFixture.Seed(factory);
        using var http = seed.Planning.Annual.Client(factory);
        var original = await AnnualPlanningFixture.Read<StrategicScenarioDocument>(
            await http.PostAsync(seed.Planning.Root + $"/versions/{seed.ScenarioId}/open", null));
        var alternative = await StrategicScenarioFixture.Save(http, seed.Planning,
            seed.Planning.Input with { Name = "Release 3 higher capacity", Drivers = seed.Planning.Input.Drivers with { CapacityUnits = 100 } });
        Assert.NotEqual(original.Scenario.Years[0].Revenue, alternative.Scenario.Years[0].Revenue);

        var work = await DecisionWorkFixture.Create(http, seed);
        using var evidence = JsonDocument.Parse(work.Created.Source.EvidenceJson);
        Assert.Equal(original.Scenario.Years.Single(x => x.Year == 2).Revenue,
            evidence.RootElement.GetProperty("Result").GetProperty("Revenue").GetDecimal());
        Assert.Equal(seed.ScenarioId, work.Created.Source.Reference.VersionId);
        var taskPath = $"/api/companies/{seed.Company}/tasks/{work.TaskId}/status";
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PatchAsJsonAsync(taskPath,
            new UpdateTaskStatusCommand("completed", null, "Review still required", null))).StatusCode);
        var review = await AnnualPlanningFixture.Read<DecisionWorkDocument>(
            await http.PostAsync(seed.Root + $"/tasks/{work.TaskId}/review", null));
        var approvalPath = $"/api/companies/{seed.Company}/approvals/{review.ApprovalId}";
        var approval = await AnnualPlanningFixture.Read<ApprovalRequestDto>(await http.GetAsync(approvalPath));
        await AnnualPlanningFixture.Read<ApprovalDecisionResultDto>(await http.PostAsJsonAsync(approvalPath + "/decisions",
            new ApprovalDecisionCommand(approval.Id, "approve", Comment: "Controlled internal follow-up",
                ClientRequestId: Guid.NewGuid(), ReviewToken: approval.Review!.Token)));
        Assert.Equal("in_progress", (await AnnualPlanningFixture.Read<DecisionWorkDocument>(
            await http.PostAsync(seed.Root + $"/tasks/{work.TaskId}/open", null))).Status);

        var cadence = new BriefingCadenceFixture(seed.Company, seed.Planning.Annual.Quarter.Company.Owner,
            Guid.Empty, Guid.Empty, work.TaskId, Guid.Empty);
        await cadence.Save(http, BriefingCadenceFixture.Settings with { Role = "ceo", FocusAreas = ["company"] });
        Assert.Equal(0, await cadence.Schedule(factory, clock.Now.AddHours(-1)));
        Assert.Equal(1, await cadence.Schedule(factory, clock.Now));
        Assert.Equal(0, await cadence.Schedule(factory, clock.Now));
        await cadence.RunJobs(factory);
        // The fixture contains earlier annual/work outbox records. Drain using the owning processor.
        for (var batch = 0; batch < 4; batch++) await cadence.Dispatch(factory);
        var preview = (await http.GetFromJsonAsync<BriefingCadencePreview>(cadence.Root + "/preview"))!;
        var delivery = Assert.Single(preview.Audit);
        Assert.Equal("sent", delivery.Status);
        var delivered = (await http.GetFromJsonAsync<BriefingCadencePreview>(cadence.Root + $"/deliveries/{delivery.Id}"))!;
        var item = Assert.Single(delivered.Items, x => x.Id == work.TaskId);
        Assert.Equal(work.OriginPath, item.RetainedSourcePath);
        await cadence.Dispatch(factory);
        await factory.SeedAsync(async db => Assert.Single(await db.CompanyNotifications.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.Company && x.RelatedEntityType == "briefing_cadence_delivery").ToArrayAsync()));
        var reopened = await AnnualPlanningFixture.Read<StrategicScenarioDocument>(
            await http.PostAsync(seed.Planning.Root + $"/versions/{seed.ScenarioId}/open", null));
        Assert.Equal(original.Scenario.Fingerprint, reopened.Scenario.Fingerprint);
        Assert.Equal(JsonSerializer.Serialize(original.Scenario.Years), JsonSerializer.Serialize(reopened.Scenario.Years));
    }

    [Fact]
    public async Task Executive_and_department_weekly_sources_and_saved_monthly_exports_reconcile_in_one_company()
    {
        using var factory = new TestWebApplicationFactory(new WeeklyWorkspaceFixture.Clock());
        var seed = await WeeklyWorkspaceFixture.Seed(factory);
        using var http = WeeklyWorkspaceFixture.Client(factory);
        var root = $"/api/companies/{seed.Company}/workspace";
        var executive = (await http.GetFromJsonAsync<WeeklyWorkspaceDto>(root + "/weekly?lens=company"))!;
        foreach (var lens in new[] { "company", "sales", "marketing", "finance", "customers" })
        {
            var department = (await http.GetFromJsonAsync<WeeklyWorkspaceDto>(root + $"/weekly?lens={lens}"))!;
            foreach (var contribution in department.Contributions)
            {
                var shared = executive.Contributions.Single(x => x.Lens == contribution.Lens);
                Assert.Equal(JsonSerializer.Serialize(shared.Metrics), JsonSerializer.Serialize(contribution.Metrics));
            }
            var saved = await MonthlyReviewSnapshotIntegrationTests.Save(http, seed.Company, lens);
            var export = await AnnualPlanningFixture.Read<MonthlyReviewExportDto>(
                await http.PostAsync(root + $"/monthly/reviews/{saved.Summary.Id}/export", null));
            Assert.Contains(saved.Checksum, export.Csv);
            Assert.Equal(saved.Workspace.Review!.Measures.Count + 1,
                export.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            var retained = await AnnualPlanningFixture.Read<MonthlyReviewSnapshotDto>(
                await http.PostAsync(root + $"/monthly/reviews/{saved.Summary.Id}/open", null));
            Assert.Equal(JsonSerializer.Serialize(saved.Workspace), JsonSerializer.Serialize(retained.Workspace));
        }
        using var manager = WeeklyWorkspaceFixture.Client(factory, seed.ManagerSubject);
        var restricted = (await manager.GetFromJsonAsync<WeeklyWorkspaceDto>(root + "/weekly?lens=finance"))!;
        Assert.Equal("sales", restricted.ActiveLens);
        Assert.Single(restricted.Contributions);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(
            $"/internal/companies/{seed.Company}/finance/planning/analysis?year=2026&month=9&months=1")).StatusCode);
    }
}
