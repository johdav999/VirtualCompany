using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Persistence.Migrations.Persistence.Migrations;

namespace VirtualCompany.Api.Tests;

public sealed class CampaignPresentationActivityDomainTests
{
    [Fact]
    public void Configuration_pins_exact_version_and_enforces_scope_strategy_and_concurrency()
    {
        var actor = Guid.NewGuid(); var now = DateTime.UtcNow;
        var configuration = new SalesCampaignPresentationActivity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            SalesCampaignPresentationExecutionScope.PerAccount, SalesCampaignPresentationPresenterStrategy.Explicit,
            Guid.NewGuid(), SalesCampaignPresentationWorkStrategy.PreparationTask, false, null, 48, actor, now);
        Assert.Equal("per_account", configuration.ExecutionScope.ToValue());
        Assert.Equal(1, configuration.Version);
        Assert.Throws<InvalidOperationException>(() => configuration.Configure(Guid.NewGuid(), SalesCampaignPresentationExecutionScope.PerContact,
            SalesCampaignPresentationPresenterStrategy.CampaignOwner, null, SalesCampaignPresentationWorkStrategy.Handoff,
            false, null, 24, actor, now, expectedVersion: 0));
        Assert.Throws<ArgumentException>(() => new SalesCampaignPresentationActivity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            SalesCampaignPresentationExecutionScope.CampaignEvent, SalesCampaignPresentationPresenterStrategy.CampaignOwner,
            null, SalesCampaignPresentationWorkStrategy.PreparationTask, false, null, 24, actor, now));
    }

    [Fact]
    public void Subject_link_has_stable_retryable_state_and_campaign_run_context()
    {
        var now = DateTime.UtcNow; var actor = Guid.NewGuid(); var company = Guid.NewGuid();
        var run = new SalesPresentationRun(Guid.NewGuid(), company, Guid.NewGuid(), "campaign:activity:subject", null,
            Guid.NewGuid(), "Goal", "Account stakeholders", 30, null, "en", "manual", actor, now);
        Assert.Equal(SalesPresentationPresetContextType.CampaignActivity, run.ContextType);
        Assert.Null(run.MeetingSessionId);
        var link = new SalesCampaignPresentationRun(Guid.NewGuid(), company, Guid.NewGuid(), run.Id, "account", Guid.NewGuid(), new string('a', 64), now);
        link.Fail("temporary", "Preparation task could not be created.", now);
        link.QueueRetry(now.AddMinutes(1));
        Assert.Equal("retrying", link.Status);
        Assert.Null(link.FailureSummary);
    }
}

public sealed class CampaignPresentationActivityMigrationTests
{
    [Fact]
    public void Migration_is_additive_with_tenant_foreign_keys_and_idempotency_index()
    {
        var operations = Up(new AddCampaignPresentationActivities());
        Assert.DoesNotContain(operations, x => x is DropColumnOperation or DropTableOperation);
        var tables = operations.OfType<CreateTableOperation>().ToDictionary(x => x.Name);
        Assert.Contains("sales_campaign_presentation_activities", tables.Keys);
        Assert.Contains("sales_campaign_presentation_runs", tables.Keys);
        Assert.Contains(tables["sales_campaign_presentation_activities"].ForeignKeys,
            x => x.PrincipalTable == "sales_presentation_preset_versions" && x.Columns.SequenceEqual(["company_id", "preset_version_id"]));
        Assert.Contains(tables["sales_campaign_presentation_runs"].ForeignKeys,
            x => x.PrincipalTable == "sales_presentation_runs" && x.Columns.SequenceEqual(["company_id", "presentation_run_id"]));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), x => x.Table == "sales_campaign_presentation_runs" && x.IsUnique && x.Columns.SequenceEqual(["company_id", "idempotency_key"]));
    }
    private static IReadOnlyList<MigrationOperation> Up(Migration migration)
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        migration.GetType().GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);
        return builder.Operations;
    }
}
